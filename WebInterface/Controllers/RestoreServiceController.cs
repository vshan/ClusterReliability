﻿using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Services.Client;
using Microsoft.ServiceFabric.Services.Remoting.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestoreService;
using System.Web.Script.Serialization;
using PolicyStorageService;
using System.Fabric.Query;
using WebInterface.Models;
using System.Xml.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Net.Security;
using System.Net.Http;

namespace WebInterface.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]")]
    public class RestoreServiceController : Controller
    {

        // Returns the applications deployed in primary cluster
        // GET: api/RestoreService/{primaryCluster}/{httpendpoint}
        [HttpGet("{pc}/{hp}", Name = "Get")]
        public async Task<IActionResult> Get(String pc, String hp)
        {
            List<String> applicationsList = new List<String>();

            FabricClient fabricClient;

            try
            {
                fabricClient = new FabricClient(pc + ":" + GetClientConnectionEndpoint(pc + ":" + hp));
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.Message("Web Service: Exception while trying to connect securely: {0}", e);
                throw;
            }


            FabricClient.QueryClient queryClient = fabricClient.QueryManager;
            ApplicationList appsList = await queryClient.GetApplicationListAsync();

            foreach (Application application in appsList)
            {
                string applicationName = application.ApplicationName.ToString();
                applicationsList.Add(applicationName);
            }
            return this.Json(applicationsList);

        }

        [HttpGet]
        [Route("apps/{primarycs}/{primaryThumbprint}/{secondarycs}/{secondaryThumbprint}")]
        public async Task<IActionResult> GetApplications(String primarycs, String primaryThumbprint, String secondarycs, String secondaryThumbprint)
        {
            Dictionary<String, List<List<String>>> applicationsServicesMap = new Dictionary<String, List< List<String> >>();

            FabricClient primaryfc = GetSecureFabricClient(primarycs, primaryThumbprint);
            FabricClient secondaryfc = GetSecureFabricClient(secondarycs, secondaryThumbprint);

            FabricClient.QueryClient queryClient = primaryfc.QueryManager;
            ApplicationList appsList = await queryClient.GetApplicationListAsync();

            HashSet<String> configuredServices = await GetConfiguredServices();
            HashSet<String> secServices = new HashSet<string>();

            foreach (Application application in appsList)
            {
                string applicationName = application.ApplicationName.ToString();

                ServiceList services = await primaryfc.QueryManager.GetServiceListAsync(new Uri(applicationName));

                ServiceList secondaryServices = await secondaryfc.QueryManager.GetServiceListAsync(new Uri(applicationName));

                foreach (Service service in secondaryServices)
                {
                    secServices.Add(service.ServiceName.ToString());
                }



                List<List<String>> serviceList = new List<List<String>>();
                foreach (Service service in services)
                {
                    List<String> serviceInfo = new List<String>();
                    string serviceName = service.ServiceName.ToString();

                    if (secServices.Contains(serviceName))
                    {

                        if (configuredServices.Contains(serviceName))
                        {
                            //Configured
                            serviceInfo.Add(serviceName);
                            serviceInfo.Add("Configured");
                        }
                        else if (service.ServiceKind == ServiceKind.Stateless)
                        {
                            //Stateless
                            serviceInfo.Add(serviceName);
                            serviceInfo.Add("Stateless");
                        }
                        else
                        {
                            //NotConfigured
                            serviceInfo.Add(serviceName);
                            serviceInfo.Add("NotConfigured");
                        }
                    }
                    else
                    {
                        //NotExist
                        serviceInfo.Add(serviceName);
                        serviceInfo.Add("NotExist");
                    }


                    serviceList.Add(serviceInfo);
                }

                applicationsServicesMap.Add(applicationName, serviceList);
            }

            return this.Json(applicationsServicesMap);

        }

        public static FabricClient GetSecureFabricClient(string connectionEndpoint, string thumbprint)
        {
            string CommonName = "southindia.cloudapp.azure.com";
            var xc = GetCredentials(thumbprint, thumbprint, CommonName);

            FabricClient fc;

            try
            {
                fc = new FabricClient(xc, connectionEndpoint);
                return fc;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.Message("Web Service: Exception while trying to connect securely: {0}", e);
                throw;
            }
        }

        static X509Credentials GetCredentials(string clientCertThumb, string serverCertThumb, string name)
        {
            X509Credentials xc = new X509Credentials();
            xc.StoreLocation = StoreLocation.CurrentUser;
            xc.StoreName = "My";
            xc.FindType = X509FindType.FindByThumbprint;
            xc.FindValue = clientCertThumb;
            xc.RemoteCommonNames.Add(name);
            xc.RemoteCertThumbprints.Add(serverCertThumb);
            xc.ProtectionLevel = System.Fabric.ProtectionLevel.EncryptAndSign;
            return xc;
        }

        static X509Certificate2 GetClientCertificate()
        {
            X509Store userCaStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            try
            {
                userCaStore.Open(OpenFlags.ReadOnly);
                X509Certificate2Collection certificatesInStore = userCaStore.Certificates;
                X509Certificate2Collection findResult = certificatesInStore.Find(X509FindType.FindByThumbprint, "45E894C34014B198B157F95A57EF98BD7D051194", false);
                X509Certificate2 clientCertificate = null;

                if (findResult.Count == 1)
                {
                    clientCertificate = findResult[0];
                }
                else
                {
                    throw new Exception("Unable to locate the correct client certificate.");
                }
                return clientCertificate;
            }
            catch
            {
                throw;
            }
            finally
            {
                userCaStore.Close();
            }
        }



        // Gets the policies associated with the chosen applications
        [Route("policies/{cs}")]
        [HttpPost]
        public async Task<IActionResult> GetPolicies(String cs, [FromBody]List<string> applications)
        {
            List<PolicyStorageEntity> policyDetails = new List<PolicyStorageEntity>();
            List<string> policyNames = new List<string>();
            foreach (string application in applications)
            {
                string URL = "http://" + cs + "/Applications/" + application + "/$/GetBackupConfigurationInfo";
                string urlParameters = "?api-version=6.2-preview";
                HttpClient client = new HttpClient
                {
                    BaseAddress = new Uri(URL)
                };
                client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = await client.GetAsync(urlParameters);
                if (response.IsSuccessStatusCode)
                {
                    var content = response.Content.ReadAsAsync<JObject>().Result;
                    JArray array = (JArray)content["Items"];
                    foreach (var item in array)
                    {
                        string policy = item["PolicyName"].ToString();
                        if (!policyNames.Contains(policy))
                            policyNames.Add(policy);
                    }
                }
                else
                {
                    Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                    return null;
                }

                foreach(string policyName in policyNames)
                {
                    PolicyStorageEntity policyStorageEntity = await getPolicyDetails(cs, policyName);

                    if (policyStorageEntity != null)
                    {
                        policyDetails.Add(policyStorageEntity);
                    }
                    else
                    {
                        //Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                        return null;
                    }
                }
            }
            return this.Json(policyDetails);
            
        }

        private async Task<PolicyStorageEntity> getPolicyDetails(string cs, string policyName)
        {
            PolicyStorageEntity policyStorageEntity = new PolicyStorageEntity();
            policyStorageEntity.policy = policyName;
            string URL = "https://" + cs + "/";
            string urlParameters = "BackupRestore/BackupPolicies/" + policyName + "?api-version=6.2-preview";


            X509Certificate2 clientCert = GetClientCertificate();
            WebRequestHandler requestHandler = new WebRequestHandler();
            requestHandler.ClientCertificates.Add(clientCert);
            requestHandler.ServerCertificateValidationCallback = this.MyRemoteCertificateValidationCallback;


            HttpClient client = new HttpClient(requestHandler)
            {
                BaseAddress = new Uri(URL)
            };
            client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

            // List data response.
            HttpResponseMessage response = await client.GetAsync(urlParameters);  // Blocking call!
            if (response.IsSuccessStatusCode)
            {
                // Parse the response body. Blocking!
                var content = response.Content.ReadAsAsync<JObject>().Result;
                JObject objectData = (JObject)content["Storage"];
                BackupStorage backupStorage = JsonConvert.DeserializeObject<BackupStorage>(objectData.ToString());
                policyStorageEntity.backupStorage = backupStorage;
                return policyStorageEntity;
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }
        }

        [Route("servicepolicies/{cs}/{serviceName}")]
        [HttpGet]
        public async Task<IActionResult> GetServicePolicies(String cs, String serviceName)
        {
            List<PolicyStorageEntity> policyDetails = new List<PolicyStorageEntity>();
            List<string> policyNames = new List<string>();
            string mServiceName = serviceName.Replace("_", "/");
            string URL = "https://" + cs + "/";
                
            string urlParameters = "Services/" + mServiceName + "/$/GetBackupConfigurationInfo" + "?api-version=6.2-preview";


            X509Certificate2 clientCert = GetClientCertificate();
            WebRequestHandler requestHandler = new WebRequestHandler();
            requestHandler.ClientCertificates.Add(clientCert);
            requestHandler.ServerCertificateValidationCallback = this.MyRemoteCertificateValidationCallback;

            HttpClient client = new HttpClient(requestHandler)
            {
                BaseAddress = new Uri(URL)
            };
            client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(urlParameters);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception occured.");
                return null;
            }
            if (response.IsSuccessStatusCode)
            {
                var content = response.Content.ReadAsAsync<JObject>().Result;
                JArray array = (JArray)content["Items"];
                foreach (var item in array)
                {
                    string policy = item["PolicyName"].ToString();
                    if (!policyNames.Contains(policy))
                        policyNames.Add(policy);
                }
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }

            foreach (string policyName in policyNames)
            {
                PolicyStorageEntity policyStorageEntity = await this.getPolicyDetails(cs, policyName);

                if (policyStorageEntity != null)
                {
                    policyDetails.Add(policyStorageEntity);
                }
                else
                {
                    //Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                    return null;
                }
            }
            return this.Json(policyDetails);

        }

        [Route("apppolicies/{cs}/{appName}")]
        [HttpGet]
        public async Task<IActionResult> GetApplicationPolicies(String cs, String appName)
        {
            List<PolicyStorageEntity> policyDetails = new List<PolicyStorageEntity>();
            List<string> policyNames = new List<string>();
            string URL = "https://" + cs + "/";

            string urlParameters = "Applications/" + appName + "/$/GetBackupConfigurationInfo" + "?api-version=6.2-preview";


            X509Certificate2 clientCert = GetClientCertificate();
            WebRequestHandler requestHandler = new WebRequestHandler();
            requestHandler.ClientCertificates.Add(clientCert);
            requestHandler.ServerCertificateValidationCallback = this.MyRemoteCertificateValidationCallback;

            HttpClient client = new HttpClient(requestHandler)
            {
                BaseAddress = new Uri(URL)
            };
            client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(urlParameters);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception occured.");
                return null;
            }
            if (response.IsSuccessStatusCode)
            {
                var content = response.Content.ReadAsAsync<JObject>().Result;
                JArray array = (JArray)content["Items"];
                foreach (var item in array)
                {
                    string policy = item["PolicyName"].ToString();
                    if (!policyNames.Contains(policy))
                        policyNames.Add(policy);
                }
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }

            foreach (string policyName in policyNames)
            {
                PolicyStorageEntity policyStorageEntity = await this.getPolicyDetails(cs, policyName);

                if (policyStorageEntity != null)
                {
                    policyDetails.Add(policyStorageEntity);
                }
                else
                {
                    //Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                    return null;
                }
            }
            return this.Json(policyDetails);
        }

        private bool MyRemoteCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        // Calls configure method of restore service
        [HttpPost]
        [Route("configure/{primaryClusterAddress}/{secondaryClusterAddress}/{primaryHttpEndpoint}/{secondaryHttpEndpoint}")]
        public void Configure([FromBody]JObject content, string primaryClusterAddress,string secondaryClusterAddress, string primaryHttpEndpoint, string secondaryHttpEndpoint)
        {
            string primaryClientConnectionEndpoint = GetClientConnectionEndpoint(primaryClusterAddress + ":" + primaryHttpEndpoint);
            string secondaryClientConnectionEndpoint = GetClientConnectionEndpoint(secondaryClusterAddress + ":" + secondaryHttpEndpoint);

            ClusterDetails primaryCluster = new ClusterDetails(primaryClusterAddress, primaryHttpEndpoint, primaryClientConnectionEndpoint);
            ClusterDetails secondaryCluster = new ClusterDetails(secondaryClusterAddress, secondaryHttpEndpoint, secondaryClientConnectionEndpoint);

            JArray applicationsData = (JArray)content["ApplicationsList"];
            JArray policiesData = (JArray)content["PoliciesList"];

            List<string> applicationsList = JsonConvert.DeserializeObject<List<string>>(applicationsData.ToString());
            List<PolicyStorageEntity> policicesList = JsonConvert.DeserializeObject<List<PolicyStorageEntity>>(policiesData.ToString());

            FabricClient fabricClient = new FabricClient();
            ServicePartitionList partitionList = fabricClient.QueryManager.GetPartitionListAsync(new Uri("fabric:/SFAppDRTool/RestoreService")).Result;

            foreach(Partition partition in partitionList)
            {
                var int64PartitionInfo = partition.PartitionInformation as Int64RangePartitionInformation;
                long lowKey = (long)int64PartitionInfo?.LowKey;
                IRestoreService restoreServiceClient = ServiceProxy.Create<IRestoreService>(new Uri("fabric:/SFAppDRTool/RestoreService"), new ServicePartitionKey(lowKey));
                try
                {
                    restoreServiceClient.Configure(applicationsList, policicesList, primaryCluster, secondaryCluster);
                }
                catch (Exception ex)
                {
                    ServiceEventSource.Current.Message("Web Service: Exception configuring the application {0}", ex);
                    throw;
                }
            }
        }

        [HttpPost]
        [Route("configureapp/{primaryClusterAddress}/{primaryThumbprint}/{secondaryClusterAddress}/{secondaryThumbprint}")]
        public void ConfigureApplication([FromBody]JObject content, string primaryClusterAddress, string primaryThumbprint, string secondaryClusterAddress, string secondaryThumbprint)
        {
            //string primaryClientConnectionEndpoint = GetClientConnectionEndpoint(primaryClusterAddress + ":" + primaryHttpEndpoint);
            //string secondaryClientConnectionEndpoint = GetClientConnectionEndpoint(secondaryClusterAddress + ":" + secondaryHttpEndpoint);

            string primaryHttpEndpoint = "19080";
            string secondaryHttpEndpoint = "19080";

            string[] primaryClusterDetails = primaryClusterAddress.Split(':');
            string[] secondaryClusterDetails = secondaryClusterAddress.Split(':');

            ClusterDetails primaryCluster = new ClusterDetails(primaryClusterDetails[0], primaryHttpEndpoint, primaryClusterDetails[1], primaryThumbprint);
            ClusterDetails secondaryCluster = new ClusterDetails(secondaryClusterDetails[0], secondaryHttpEndpoint, secondaryClusterDetails[1], secondaryThumbprint);

            JArray applicationData = (JArray)content["ApplicationList"];
            JArray policiesData = (JArray)content["PoliciesList"];

            List<string> applicationDataObj = JsonConvert.DeserializeObject<List<string>>(applicationData.ToString());
            List<PolicyStorageEntity> policicesList = JsonConvert.DeserializeObject<List<PolicyStorageEntity>>(policiesData.ToString());

            FabricClient fabricClient = new FabricClient();
            ServicePartitionList partitionList = fabricClient.QueryManager.GetPartitionListAsync(new Uri("fabric:/SFAppDRTool/RestoreService")).Result;

            foreach (Partition partition in partitionList)
            {
                var int64PartitionInfo = partition.PartitionInformation as Int64RangePartitionInformation;
                long lowKey = (long)int64PartitionInfo?.LowKey;
                IRestoreService restoreServiceClient = ServiceProxy.Create<IRestoreService>(new Uri("fabric:/SFAppDRTool/RestoreService"), new ServicePartitionKey(lowKey));
                try
                {
                    restoreServiceClient.ConfigureApplication(applicationDataObj[0], policicesList, primaryCluster, secondaryCluster);
                }
                catch (Exception ex)
                {
                    ServiceEventSource.Current.Message("Web Service: Exception configuring the application {0}", ex);
                    throw;
                }
            }
        }

        // Calls configure service method of restore service
        [HttpPost]
        [Route("configureservice/{primaryClusterAddress}/{primaryThumbprint}/{secondaryClusterAddress}/{secondaryThumbprint}")]
        public void ConfigureService([FromBody]JObject content, string primaryClusterAddress, string primaryThumbprint, string secondaryClusterAddress, string secondaryThumbprint)
        {
            string primaryHttpEndpoint = "19080";
            string secondaryHttpEndpoint = "19080";

            string[] primaryClusterDetails = primaryClusterAddress.Split(':');
            string[] secondaryClusterDetails = secondaryClusterAddress.Split(':');

            ClusterDetails primaryCluster = new ClusterDetails(primaryClusterDetails[0], primaryHttpEndpoint, primaryClusterDetails[1], primaryThumbprint);
            ClusterDetails secondaryCluster = new ClusterDetails(secondaryClusterDetails[0], secondaryHttpEndpoint, secondaryClusterDetails[1], secondaryThumbprint);

            JArray serviceData = (JArray)content["ServiceList"];
            JArray policiesData = (JArray)content["PoliciesList"];

            List<string> serviceDataObj = JsonConvert.DeserializeObject<List<string>>(serviceData.ToString());
            List<PolicyStorageEntity> policicesList = JsonConvert.DeserializeObject<List<PolicyStorageEntity>>(policiesData.ToString());

            FabricClient fabricClient = new FabricClient();
            ServicePartitionList partitionList = fabricClient.QueryManager.GetPartitionListAsync(new Uri("fabric:/SFAppDRTool/RestoreService")).Result;

            foreach (Partition partition in partitionList)
            {
                var int64PartitionInfo = partition.PartitionInformation as Int64RangePartitionInformation;
                long lowKey = (long)int64PartitionInfo?.LowKey;
                IRestoreService restoreServiceClient = ServiceProxy.Create<IRestoreService>(new Uri("fabric:/SFAppDRTool/RestoreService"), new ServicePartitionKey(lowKey));
                try
                {
                    restoreServiceClient.ConfigureService(serviceDataObj[0], serviceDataObj[1], policicesList, primaryCluster, secondaryCluster);
                }
                catch (Exception ex)
                {
                    ServiceEventSource.Current.Message("Web Service: Exception configuring the service {0}", ex);
                    throw;
                }
            }
        }


        /// <summary>
        /// Disconfigures the application for standby by calling disconfigure of the restore service method
        /// </summary>
        /// <param name="applicationName"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("disconfigure/{applicationName}")]
        public async Task<string> Disconfigure(string applicationName)
        {
            bool successfullyRemoved = true;
            FabricClient fabricClient = new FabricClient();
            ServicePartitionList partitionList = fabricClient.QueryManager.GetPartitionListAsync(new Uri("fabric:/SFAppDRTool/RestoreService")).Result;
            foreach (Partition partition in partitionList)
            {
                var int64PartitionInfo = partition.PartitionInformation as Int64RangePartitionInformation;
                long lowKey = (long)int64PartitionInfo?.LowKey;
                IRestoreService restoreServiceClient = ServiceProxy.Create<IRestoreService>(new Uri("fabric:/SFAppDRTool/RestoreService"), new ServicePartitionKey(lowKey));
                try
                {
                    string applicationRemoved = await restoreServiceClient.Disconfigure("fabric:/" + applicationName);
                    if(applicationRemoved == null) successfullyRemoved = false;
                }
                catch (Exception ex)
                {
                    ServiceEventSource.Current.Message("Web Service: Exception Disconfiguring {0}", ex);
                    throw;
                }
            }
            if (successfullyRemoved) return applicationName;
            return null;
        }


        /// <summary>
        /// This calls GetStatus method of restore service.
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("status")]
        public async Task<IEnumerable<PartitionWrapper>> GetPartitionStatus()
        {
            FabricClient fabricClient = new FabricClient();
            ServicePartitionList partitionList = fabricClient.QueryManager.GetPartitionListAsync(new Uri("fabric:/SFAppDRTool/RestoreService")).Result;

            List<PartitionStatusModel> partitionStatusList = new List<PartitionStatusModel>();
            List<PartitionWrapper> mappedPartitions = new List<PartitionWrapper>();

            foreach (Partition partition in partitionList)
            {
                List<PartitionWrapper> servicePartitions = new List<PartitionWrapper>();
                var int64PartitionInfo = partition.PartitionInformation as Int64RangePartitionInformation;
                long lowKey = (long)int64PartitionInfo?.LowKey;
                IRestoreService restoreServiceClient = ServiceProxy.Create<IRestoreService>(new Uri("fabric:/SFAppDRTool/RestoreService"), new ServicePartitionKey(lowKey));
                try
                {
                    servicePartitions = await restoreServiceClient.GetStatus();
                    mappedPartitions.AddRange(servicePartitions);
                }
                catch (Exception ex)
                {
                    ServiceEventSource.Current.Message("Web Service: Exception getting the status {0}", ex);
                    throw;
                }
            }

            //if (mappedPartitions.Count == 0) return null;
            return mappedPartitions;
        }

        public async Task<HashSet<String>> GetConfiguredServices()
        {
            IEnumerable<PartitionWrapper> configuredPartitions = await GetPartitionStatus();
            HashSet<String> configuredServices = new HashSet<String>();
            foreach (var partition in configuredPartitions)
            {
                configuredServices.Add(partition.serviceName.ToString());
            }
            return configuredServices;
        }

        public string GetClientConnectionEndpoint(string clusterConnectionString)
        {
            string URL = "http://" + clusterConnectionString + "/$/GetClusterManifest";
            string urlParameters = "?api-version=6.2";
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(URL);
            client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response = client.GetAsync(urlParameters).Result;
            if (response.IsSuccessStatusCode)
            {
                var content = response.Content.ReadAsAsync<JObject>().Result;
                JValue objectData = (JValue)content["Manifest"];
                XElement xel = XElement.Parse(objectData.ToString());
                XElement xElement = xel.Descendants().First().Descendants().First().Descendants().First().Descendants().First();

                return xElement.Attribute("Port").Value.ToString();
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }
        }
    }


}
