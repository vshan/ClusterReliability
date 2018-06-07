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
        [HttpGet("{cs}", Name = "Get")]
        public async Task<IActionResult> Get(String cs)
        {
            if (cs.Contains("http://"))
                cs = cs.Replace("http://", "");
            if (cs.Contains("https://"))
                cs = cs.Replace("https://", "");
            FabricClient fabricClient = new FabricClient(cs);
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
        [HttpGet]
        public async Task<IActionResult> GetPolicies(String cs)
        {
            string URL = "http://" + cs + "/BackupRestore/BackupPolicies";
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
                List<String> policies = new List<string>();
                foreach (var item in array)
                {
                    policies.Add(item["Name"].ToString());
                }
                return this.Json(policies);
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }
            
        }

        // POST: api/Restore
        [HttpPost]
        [Route("add/{policyName}")]
        public void Post([FromBody]BackupStorage backupStorage, string policyName)
        {
            IPolicyStorageService policyStorageServiceClient = ServiceProxy.Create<IPolicyStorageService>(new Uri("fabric:/CBA/PolicyStorageService"));
            try
            {
               policyStorageServiceClient.PostStorageDetails(policyName, backupStorage);
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Message("Web Service: Exception posting storage details {0} : {1}", policyName,ex);
                throw;
            }
        }

        [HttpPost]
        [Route("configure/{applicationName}/{primaryCluster}/{secondaryCluster}/{httpEndpoint}/{clientConnectionEndpoint}")]
        public void Configure(string applicationName, string primaryCluster,string secondaryCluster, string httpEndpoint, string clientConnectionEndpoint)
        {
            // BackupStorage backupStorage = JsonConvert.DeserializeObject<BackupStorage>(new JavaScriptSerializer().Serialize(value));
            FabricClient fabricClient = new FabricClient();
            ServicePartitionList partitionList = fabricClient.QueryManager.GetPartitionListAsync(new Uri("fabric:/CBA/RestoreService")).Result;
            foreach(Partition partition in partitionList)
            {
                var int64PartitionInfo = partition.PartitionInformation as Int64RangePartitionInformation;
                long lowKey = (long)int64PartitionInfo?.LowKey;
                IRestoreService restoreServiceClient = ServiceProxy.Create<IRestoreService>(new Uri("fabric:/CBA/RestoreService"), new ServicePartitionKey(lowKey));
                try
                {
                    restoreServiceClient.Configure(applicationName, primaryCluster, secondaryCluster,httpEndpoint, clientConnectionEndpoint);
                }
                catch (Exception ex)
                {
                    ServiceEventSource.Current.Message("Web Service: Exception configuring the application {0} : {1}", applicationName, ex);
                    throw;
                }
            }
        }

        [HttpGet]
        [Route("status/{primaryCluster}/{httpEndpoint}/{clientConnectionEndpoint}/{applicationName}")]
        public async Task<IEnumerable<PartitionStatusModel>> GetPartitionStatus(string primaryCluster, string httpEndpoint, string clientConnectionEndpoint, string applicationName)
        {
            FabricClient fabricClient = new FabricClient(primaryCluster + ":" + clientConnectionEndpoint);
            ServiceList serviceList = await fabricClient.QueryManager.GetServiceListAsync(new Uri("fabric:/" + applicationName));
            List<PartitionStatusModel> partitionStatusList = new List<PartitionStatusModel>();
            foreach(Service service in serviceList)
            {
                ServicePartitionList partitionList = await fabricClient.QueryManager.GetPartitionListAsync(service.ServiceName);
                foreach(Partition partition in partitionList)
                {
                    IRestoreService restoreServiceClient = ServiceProxy.Create<IRestoreService>(new Uri("fabric:/CBA/RestoreService"), new ServicePartitionKey(HashUtil.getLongHashCode(partition.PartitionInformation.Id.ToString())));
                    PartitionWrapper mappedPartition = await restoreServiceClient.GetStatus(partition.PartitionInformation.Id);
                    if (mappedPartition == null)
                    {
                        ServiceEventSource.Current.Message("It returned null");
                        return null;
                    }
                    try
                    {
                        if (mappedPartition.LastBackup == null)
                        {
                            PartitionStatusModel partitionStatus = new PartitionStatusModel(service.ServiceName.ToString(), partition.PartitionInformation.Id.ToString(), mappedPartition.partitionId.ToString());
                            partitionStatusList.Add(partitionStatus);
                        }
                        else
                        {
                            PartitionStatusModel partitionStatus = new PartitionStatusModel(service.ServiceName.ToString(), partition.PartitionInformation.Id.ToString(), mappedPartition.partitionId.ToString(), mappedPartition.LastBackup.latestBackupRestored.ToString(), mappedPartition.LastBackup.backupId.ToString());
                            partitionStatusList.Add(partitionStatus);
                        }
                    }
                    catch(Exception ex)
                    {
                        ServiceEventSource.Current.Message("Exception thrown while creating object : {0}", ex);
                    }

                }
            }
            if (partitionStatusList.Count == 0) return null;
            return partitionStatusList;
            /*var response = JsonConvert.SerializeObject(new
            {
                partitionStatusList
            });
            return response;*/

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
