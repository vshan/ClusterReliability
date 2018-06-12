using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PolicyStorageService
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class PolicyStorageService : StatefulService,IPolicyStorageService
    {
        public PolicyStorageService(StatefulServiceContext context)
            : base(context)
        { }

        /// <summary>
        /// Optional override to create listeners (e.g., HTTP, Service Remoting, WCF, etc.) for this service replica to handle client or user requests.
        /// </summary>
        /// <remarks>
        /// For more information on service communication, see https://aka.ms/servicefabricservicecommunication
        /// </remarks>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[]
            {
                new ServiceReplicaListener(context => this.CreateServiceRemotingListener(context))
            };
        }

        /// <summary>
        /// This is the main entry point for your service replica.
        /// This method executes when this replica of your service becomes primary and has write status.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service replica.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // TODO: Replace the following sample code with your own logic 
            //       or remove this RunAsync override if it's not needed in your service.

            
        }

        public async Task<bool> PostStorageDetails(List<PolicyStorageEntity> policies, string primaryClusterConnectionString)
        {
            IReliableDictionary<string, BackupStorage> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, BackupStorage>>("storageDictionary");
            foreach (var entity in policies)
            {
                BackupStorage backupStorage = await GetStorageInfo(entity.policy, primaryClusterConnectionString);
                if (backupStorage != null && entity.backupStorage != null)
                {
                    backupStorage.connectionString = entity.backupStorage.connectionString;
                    backupStorage.primaryUsername = entity.backupStorage.primaryUsername;
                    backupStorage.primaryPassword = entity.backupStorage.primaryPassword;
                    backupStorage.secondaryUsername = entity.backupStorage.secondaryUsername;
                    backupStorage.secondaryPassword = entity.backupStorage.secondaryPassword;
                    backupStorage.friendlyname = entity.backupStorage.friendlyname;
                }
                else
                {
                    return false;
                }
                using (var tx = this.StateManager.CreateTransaction())
                {
                    var result = await myDictionary.TryAddAsync(tx, entity.policy, backupStorage);

                    ServiceEventSource.Current.ServiceMessage(this.Context, result ? "Successfully added policy {0} storgae details" : "Already Exists", entity.policy);

                    // If an exception is thrown before calling CommitAsync, the transaction aborts, all changes are 
                    // discarded, and nothing is saved to the secondary replicas.
                    await tx.CommitAsync();
                }
            }
            return true;
        }

        public async Task<BackupStorage> GetPolicyStorageDetails(String policy)
        {
            IReliableDictionary<string, BackupStorage> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, BackupStorage>>("storageDictionary");
            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalValue<BackupStorage> backupStorage = await myDictionary.TryGetValueAsync(tx, policy);
                if (backupStorage.HasValue)
                {
                    return backupStorage.Value;
                }
                else
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, "Policy not found");
                    return null;
                }
            }
        }

        public async Task<BackupStorage> GetStorageInfo(string policy, string primaryClusterConnectionString)
        {
            string URL = "http://" + primaryClusterConnectionString + "/BackupRestore/BackupPolicies/" + policy;
            string urlParameters = "?api-version=6.2-preview";
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(URL);
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
                return backupStorage;
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }
        } 
    }
}
