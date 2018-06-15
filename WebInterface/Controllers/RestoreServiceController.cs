using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Net.Http;
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

namespace WebInterface.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]")]
    public class RestoreServiceController : Controller
    {
        // GET: api/Restore
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET: api/Restore/5
        [HttpGet("{pc}/{hp}", Name = "Get")]
        public async Task<IActionResult> Get(String pc, String hp)
        {
            FabricClient fabricClient = new FabricClient(pc + ":" + GetClientConnectionEndpoint(pc + ":" + hp));
            List<String> applicationsList = new List<String>();
            FabricClient.QueryClient queryClient = fabricClient.QueryManager;
            System.Fabric.Query.ApplicationList appsList = await queryClient.GetApplicationListAsync();
            foreach (System.Fabric.Query.Application application in appsList)
            {
                ///ServiceEventSource.Current.ServiceMessage(this.Context, "Application is " + application.ApplicationName);
                string applicationName = application.ApplicationName.ToString();
                applicationName = applicationName.Replace("fabric:/", "");
                applicationsList.Add(applicationName);
                //await GetPartitionsOfApplication(application.ApplicationName, clusterConnectionString, clusterConnectionString);
            }
            return this.Json(applicationsList);

        }

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

                // List data response.
                HttpResponseMessage response = await client.GetAsync(urlParameters);  // Blocking call!
                if (response.IsSuccessStatusCode)
                {
                    // Parse the response body. Blocking!
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
                    PolicyStorageEntity policyStorageEntity = new PolicyStorageEntity();
                    policyStorageEntity.policy = policyName;
                    URL = "http://" + cs + "/BackupRestore/BackupPolicies/" + policyName;
                    urlParameters = "?api-version=6.2-preview";
                    client = new HttpClient
                    {
                        BaseAddress = new Uri(URL)
                    };
                    client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                    // List data response.
                    response = await client.GetAsync(urlParameters);  // Blocking call!
                    if (response.IsSuccessStatusCode)
                    {
                        // Parse the response body. Blocking!
                        var content = response.Content.ReadAsAsync<JObject>().Result;
                        JObject objectData = (JObject)content["Storage"];
                        BackupStorage backupStorage = JsonConvert.DeserializeObject<BackupStorage>(objectData.ToString());
                        policyStorageEntity.backupStorage = backupStorage;
                        policyDetails.Add(policyStorageEntity);
                    }
                    else
                    {
                        Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                        return null;
                    }
                }
            }
            return this.Json(policyDetails);
            
        }

        // POST: api/Restore
        [HttpPost]
        [Route("add/{policyName}/{pccs}")]
        public bool Post([FromBody]BackupStorage backupStorage, string policyName, string pccs)
        {
            IPolicyStorageService policyStorageServiceClient = ServiceProxy.Create<IPolicyStorageService>(new Uri("fabric:/StandByApplication/PolicyStorageService"));
            try
            {
               return policyStorageServiceClient.PostStorageDetails(null, pccs).Result;
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Message("Web Service: Exception posting storage details {0} : {1}", policyName,ex);
                throw;
            }
        }

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
            // BackupStorage backupStorage = JsonConvert.DeserializeObject<BackupStorage>(new JavaScriptSerializer().Serialize(value));
            FabricClient fabricClient = new FabricClient();
            ServicePartitionList partitionList = fabricClient.QueryManager.GetPartitionListAsync(new Uri("fabric:/StandByApplication/RestoreService")).Result;
            foreach(Partition partition in partitionList)
            {
                var int64PartitionInfo = partition.PartitionInformation as Int64RangePartitionInformation;
                long lowKey = (long)int64PartitionInfo?.LowKey;
                IRestoreService restoreServiceClient = ServiceProxy.Create<IRestoreService>(new Uri("fabric:/StandByApplication/RestoreService"), new ServicePartitionKey(lowKey));
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

        [HttpGet]
        [Route("disconfigure/{applicationName}")]
        public async Task<string> Disconfigure(string applicationName)
        {
            bool successfullyRemoved = true;
            FabricClient fabricClient = new FabricClient();
            ServicePartitionList partitionList = fabricClient.QueryManager.GetPartitionListAsync(new Uri("fabric:/StandByApplication/RestoreService")).Result;
            foreach (Partition partition in partitionList)
            {
                var int64PartitionInfo = partition.PartitionInformation as Int64RangePartitionInformation;
                long lowKey = (long)int64PartitionInfo?.LowKey;
                IRestoreService restoreServiceClient = ServiceProxy.Create<IRestoreService>(new Uri("fabric:/StandByApplication/RestoreService"), new ServicePartitionKey(lowKey));
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

        [HttpGet]
        [Route("status")]
        public async Task<IEnumerable<PartitionWrapper>> GetPartitionStatus()
        {
            FabricClient fabricClient = new FabricClient();
            ServicePartitionList partitionList = fabricClient.QueryManager.GetPartitionListAsync(new Uri("fabric:/StandByApplication/RestoreService")).Result;
            List<PartitionStatusModel> partitionStatusList = new List<PartitionStatusModel>();
            List<PartitionWrapper> mappedPartitions = new List<PartitionWrapper>();
            foreach (Partition partition in partitionList)
            {
                List<PartitionWrapper> servicePartitions = new List<PartitionWrapper>();
                var int64PartitionInfo = partition.PartitionInformation as Int64RangePartitionInformation;
                long lowKey = (long)int64PartitionInfo?.LowKey;
                IRestoreService restoreServiceClient = ServiceProxy.Create<IRestoreService>(new Uri("fabric:/StandByApplication/RestoreService"), new ServicePartitionKey(lowKey));
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

            //            if (partitionStatusList.Count == 0) return null;
            if (mappedPartitions.Count == 0) return null;
            return mappedPartitions;
            /*var response = JsonConvert.SerializeObject(new
            {
                partitionStatusList
            });
            return response;*/

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

        // PUT: api/Restore/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody]string value)
        {
        }
        
        // DELETE: api/ApiWithActions/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }


}
