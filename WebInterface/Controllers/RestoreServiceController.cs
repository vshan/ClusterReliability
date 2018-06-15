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

        // Returns the applications deployed in primary cluster
        // GET: api/RestoreService/{primaryCluster}/{httpendpoint}
        [HttpGet("{pc}/{hp}", Name = "Get")]
        public async Task<IActionResult> Get(String pc, String hp)
        {
            List<String> applicationsList = new List<String>();
            FabricClient fabricClient = new FabricClient(pc + ":" + GetClientConnectionEndpoint(pc + ":" + hp));

            FabricClient.QueryClient queryClient = fabricClient.QueryManager;
            ApplicationList appsList = await queryClient.GetApplicationListAsync();

            foreach (Application application in appsList)
            {
                string applicationName = application.ApplicationName.ToString();
                applicationsList.Add(applicationName);
            }
            return this.Json(applicationsList);

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


        /// <summary>
        /// This calls GetStatus method of restore service.
        /// </summary>
        /// <returns></returns>
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

            if (mappedPartitions.Count == 0) return null;
            return mappedPartitions;
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
