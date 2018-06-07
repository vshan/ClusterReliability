using System;
using System.Collections;
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
using Microsoft.ServiceFabric.Services.Remoting.Client;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Newtonsoft.Json.Linq;
using PolicyStorageService;

namespace RestoreService
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class RestoreService : StatefulService, IRestoreService
    {
        public RestoreService(StatefulServiceContext context)
            : base(context)
        { }

        private string primaryClusterConnectionString;

        private string secondaryClusterConnectionString;

        public static Dictionary<Guid, Task<RestoreResult>> workFlowsInProgress;

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

            //var myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, long>>("myDictionary");
            /*
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (var tx = this.StateManager.CreateTransaction())
                {
                    var result = await myDictionary.TryGetValueAsync(tx, "Counter");

                    ServiceEventSource.Current.ServiceMessage(this.Context, "Current Counter Value: {0}",
                        result.HasValue ? result.Value.ToString() : "Value does not exist.");

                    await myDictionary.AddOrUpdateAsync(tx, "Counter", 0, (key, value) => ++value);

                    // If an exception is thrown before calling CommitAsync, the transaction aborts, all changes are 
                    // discarded, and nothing is saved to the secondary replicas.
                    await tx.CommitAsync();
                }
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }*/
            /*
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>("myDictionary");
            await GetAndMapPartitionsOfApplication(new Uri("fabric:/BrsTestApp1"),"localhost:19000","localhost:19000", myDictionary);
            using(ITransaction tx = this.StateManager.CreateTransaction())
            {
                IAsyncEnumerable<KeyValuePair<Guid, PartitionWrapper>> enumerable = await myDictionary.CreateEnumerableAsync(tx);
                IAsyncEnumerator<KeyValuePair<Guid, PartitionWrapper>> asyncEnumerator = enumerable.GetAsyncEnumerator();
                while(await asyncEnumerator.MoveNextAsync(CancellationToken.None))
                {
                    JToken backupInfo = await GetLatestBackupAvailable(asyncEnumerator.Current.Key, "http://localhost:19080");
                    if (backupInfo == null)
                        continue;
                    PartitionWrapper secondaryPartition = asyncEnumerator.Current.Value;
                    if (secondaryPartition.LastBackup == null || DateTime.Compare((DateTime)backupInfo["CreationTimeUtc"], secondaryPartition.LastBackup.latestBackupRestored) > 0)
                        await RestoreLatestBackupAvailable(backupInfo, secondaryPartition, "http://localhost:19080", "myDictionary");
                    else
                        continue;
                }
                await tx.CommitAsync();
            }
            */
            List<String> list = await GetApplicationsDeployed("localhost:19000");
            ServiceEventSource.Current.ServiceMessage(this.Context, "Application name is :"+list[0]);
        }

        public async Task<List<String>> GetApplicationsDeployed(String clusterConnectionString)
        {
            FabricClient fabricClient = new FabricClient(clusterConnectionString);
            List<String> applicationsList = new List<String>();
            FabricClient.QueryClient queryClient = fabricClient.QueryManager;
            System.Fabric.Query.ApplicationList appsList = await queryClient.GetApplicationListAsync();
            foreach(System.Fabric.Query.Application application in appsList)
            {
                ServiceEventSource.Current.ServiceMessage(this.Context, "Application is "+ application.ApplicationName);
                string applicationName = application.ApplicationName.ToString();
                applicationName.Replace("fabric:/", "");
                applicationsList.Add(applicationName);
                //await GetPartitionsOfApplication(application.ApplicationName, clusterConnectionString, clusterConnectionString);
            }
            return applicationsList;
        }

        public async Task Configure(String applicationName, String primaryCluster, String secondaryCluster, String httpEndpoint, String clientConnectionEndpoint)
        {
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>("partitionDictionary");
            await MapPartitionsOfApplication(new Uri("fabric:/" + applicationName), primaryCluster + ':' + clientConnectionEndpoint, secondaryCluster+ ":" + clientConnectionEndpoint, "partitionDictionary");
            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                IAsyncEnumerable<KeyValuePair<Guid, PartitionWrapper>> enumerable = await myDictionary.CreateEnumerableAsync(tx);
                IAsyncEnumerator<KeyValuePair<Guid, PartitionWrapper>> asyncEnumerator = enumerable.GetAsyncEnumerator();
                while (await asyncEnumerator.MoveNextAsync(CancellationToken.None))
                {
                    JToken backupInfo = await GetLatestBackupAvailable(asyncEnumerator.Current.Key, "http://" + primaryCluster + ":" + httpEndpoint);
                    if (backupInfo == null)
                        continue;
                    PartitionWrapper secondaryPartition = asyncEnumerator.Current.Value;
                    string backupPolicy = await GetPolicy("http://" + primaryCluster + ":" + httpEndpoint, asyncEnumerator.Current.Key);
                    if (secondaryPartition.LastBackup == null || DateTime.Compare((DateTime)backupInfo["CreationTimeUtc"], secondaryPartition.LastBackup.latestBackupRestored) > 0)
                        await RestoreLatestBackupAvailable(backupInfo, backupPolicy, secondaryPartition, "http://" + secondaryCluster + ":" + httpEndpoint, "partitionDictionary");
                    else
                        continue;
                }
                await tx.CommitAsync();
            }
        }

        public async Task<PartitionWrapper> GetStatus(Guid partitionId)
        {
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>("partitionDictionary");
            PartitionWrapper mappedPartition = null ;
            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalValue<PartitionWrapper> partitionWrapper = await myDictionary.TryGetValueAsync(tx, partitionId);
                if (partitionWrapper.HasValue)
                {
                    mappedPartition = partitionWrapper.Value;
                    ServiceEventSource.Current.ServiceMessage(this.Context, "Successfully Retrieved!!! ");
                }
                await tx.CommitAsync();
            }
            return mappedPartition;
        }

        public async Task<String> GetPolicy(string primaryCluster, Guid partitionId)
        {
            HttpClient client = new HttpClient();
            string URL = primaryCluster + "/Partitions/" + partitionId + "/$/GetBackupConfigurationInfo";
            string urlParameters = "?api-version=6.2-preview";
            client.BaseAddress = new Uri(URL);
            client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
            // List data response.
            HttpResponseMessage response = await client.GetAsync(urlParameters);  // Blocking call!
            if (response.IsSuccessStatusCode)
            {
                var content = response.Content.ReadAsAsync<JObject>().Result;
                string policy = content["PolicyName"].ToString();
                return policy;
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }
        }

        public async Task RestoreLatestBackupAvailable(JToken latestbackupInfo, string policy, PartitionWrapper partition, String clusterConnectionString,String partitionDictionary)
        {
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>(partitionDictionary);
            HttpClient client = new HttpClient();
            string URL = clusterConnectionString + "/Partitions/" + partition.partitionId + "/$/Restore";
            string urlParameters = "?api-version=6.2-preview";
            client.BaseAddress = new Uri(URL);
            client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
            BackupStorage backupStorage = await GetBackupStorageDetails(policy);
            if (backupStorage == null)
            {
                ServiceEventSource.Current.ServiceMessage(this.Context, "backupstorage is null");
                return;
            }
            BackupInfo backupInfo = new BackupInfo(latestbackupInfo["BackupId"].ToString(), latestbackupInfo["BackupLocation"].ToString(), backupStorage, (DateTime)latestbackupInfo["CreationTimeUtc"]);
            // List data response.
            HttpResponseMessage response = await client.PostAsJsonAsync(urlParameters,backupInfo);  // Blocking call!
            if (response.IsSuccessStatusCode)
            {
                ServiceEventSource.Current.ServiceMessage(this.Context, "Successfully Restored!!! ");
                using(ITransaction tx = this.StateManager.CreateTransaction())
                {
                    ConditionalValue<PartitionWrapper> partitionWrapper = await myDictionary.TryGetValueAsync(tx, partition.partitionId);
                    if (partitionWrapper.HasValue)
                    {
                        PartitionWrapper updatedPartitionWrapper = ObjectExtensions.Copy(partitionWrapper.Value);
                        updatedPartitionWrapper.LastBackup = backupInfo;
                        await myDictionary.SetAsync(tx, partition.partitionId, updatedPartitionWrapper);
                        ServiceEventSource.Current.ServiceMessage(this.Context, "Successfully Restored!!! ");
                    }
                    await tx.CommitAsync();
                }
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
            }

        }

        public async Task MapPartitionsOfApplication(Uri applicationName, string primaryClusterConnectionString, string secondaryClusterConnectionString, String reliableDictionary)
        {
            FabricClient primaryFabricClient = new FabricClient(primaryClusterConnectionString);
            FabricClient secondaryFabricClient = new FabricClient(secondaryClusterConnectionString);
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>(reliableDictionary);
            System.Fabric.Query.ServiceList services = await primaryFabricClient.QueryManager.GetServiceListAsync(applicationName);
            foreach(System.Fabric.Query.Service service in services)
            {
                System.Fabric.Query.ServicePartitionList primaryPartitions = await primaryFabricClient.QueryManager.GetPartitionListAsync(service.ServiceName);
                System.Fabric.Query.ServicePartitionList secondaryPartitions = await secondaryFabricClient.QueryManager.GetPartitionListAsync(service.ServiceName);
                await MapPartitions(primaryPartitions, secondaryPartitions, myDictionary);
                using(var tx = this.StateManager.CreateTransaction())
                {
                    var result = await myDictionary.GetCountAsync(tx);
                    ServiceEventSource.Current.ServiceMessage(this.Context, "The number of items in dictionary are : {0}", result);
                    await tx.CommitAsync();
                }
            }
        }

        public async Task MapPartitions(System.Fabric.Query.ServicePartitionList partitionsInPrimary, System.Fabric.Query.ServicePartitionList partitionsInSecondary, IReliableDictionary<Guid, PartitionWrapper> myDictionary)
        {
            if (partitionsInPrimary != null)
            {
                ServicePartitionKind partitionKind = partitionsInPrimary[0].PartitionInformation.Kind;
                if (partitionKind.Equals(ServicePartitionKind.Int64Range))
                {
                    await MapInt64Partitions(partitionsInPrimary, partitionsInSecondary, myDictionary);
                }
                else if (partitionKind.Equals(ServicePartitionKind.Named))
                {
                    await MapNamedPartitions(partitionsInPrimary, partitionsInSecondary, myDictionary);
                }
                else if (partitionKind.Equals(ServicePartitionKind.Singleton))
                {
                    await MapSingletonPartition(partitionsInPrimary, partitionsInSecondary, myDictionary);
                }
            }
        }

        public async Task MapInt64Partitions(System.Fabric.Query.ServicePartitionList primaryPartitions, System.Fabric.Query.ServicePartitionList secondaryPartitions, IReliableDictionary<Guid, PartitionWrapper> myDictionary)
        {
            foreach (var primaryPartition in primaryPartitions)
            {
                long hashCode = HashUtil.getLongHashCode(primaryPartition.PartitionInformation.Id.ToString());
                if (await BelongsToPartition(hashCode))
                {
                    var int64PartitionInfo = primaryPartition.PartitionInformation as Int64RangePartitionInformation;
                    long? lowKeyPrimary = int64PartitionInfo?.LowKey;
                    foreach (var secondaryPartition in secondaryPartitions)
                    {
                        long? lowKeySecondary = (secondaryPartition.PartitionInformation as Int64RangePartitionInformation)?.LowKey;
                        if (lowKeyPrimary == lowKeySecondary)
                        {

                            using (var tx = this.StateManager.CreateTransaction())
                            {
                                var result = await myDictionary.TryAddAsync(tx, primaryPartition.PartitionInformation.Id, new PartitionWrapper(secondaryPartition));

                                ServiceEventSource.Current.ServiceMessage(this.Context, result ? "Successfully Mapped Partition-{0} to Partition-{1}" : "Already Exists", primaryPartition.PartitionInformation.Id, secondaryPartition.PartitionInformation.Id);
                                // If an exception is thrown before calling CommitAsync, the transaction aborts, all changes are 
                                // discarded, and nothing is saved to the secondary replicas.
                                await tx.CommitAsync();
                            }
                        }
                    }
                }
            }
        }

        public async Task MapNamedPartitions(System.Fabric.Query.ServicePartitionList primaryPartitions, System.Fabric.Query.ServicePartitionList secondaryPartitions, IReliableDictionary<Guid, PartitionWrapper> myDictionary)
        {
            foreach(var primaryPartition in primaryPartitions)
            {
                var partitionNamePrimary = (primaryPartition.PartitionInformation as NamedPartitionInformation).Name;
                foreach (var secondaryPartition in secondaryPartitions)
                {
                    string partitionNameSecondary = (secondaryPartition.PartitionInformation as NamedPartitionInformation).Name;
                    if (partitionNamePrimary == partitionNameSecondary)
                    {
                        using (var tx = this.StateManager.CreateTransaction())
                        {
                            var result = await myDictionary.TryAddAsync(tx, primaryPartition.PartitionInformation.Id, new PartitionWrapper(secondaryPartition));

                            ServiceEventSource.Current.ServiceMessage(this.Context, result ? "Successfully Mapped Partition-{0} to Partition-{1}" : "Already Exists", primaryPartition.PartitionInformation.Id, secondaryPartition.PartitionInformation.Id);
                            // If an exception is thrown before calling CommitAsync, the transaction aborts, all changes are 
                            // discarded, and nothing is saved to the secondary replicas.
                            await tx.CommitAsync();
                        }
                    }
                }
            }
        }

        public async Task MapSingletonPartition(System.Fabric.Query.ServicePartitionList primaryPartitions, System.Fabric.Query.ServicePartitionList secondaryPartitions, IReliableDictionary<Guid, PartitionWrapper> myDictionary)
        {
            using (var tx = this.StateManager.CreateTransaction())
            {
                var result = await myDictionary.TryAddAsync(tx, primaryPartitions[0].PartitionInformation.Id, new PartitionWrapper(secondaryPartitions[0]));

                ServiceEventSource.Current.ServiceMessage(this.Context, result ? "Successfully Mapped Partition-{0} to Partition-{1}" : "Already Exists", primaryPartitions[0].PartitionInformation.Id, secondaryPartitions[0].PartitionInformation.Id);
                // If an exception is thrown before calling CommitAsync, the transaction aborts, all changes are 
                // discarded, and nothing is saved to the secondary replicas.
                await tx.CommitAsync();
            }
        }

        public async Task<JToken> GetLatestBackupAvailable(Guid partitionId, String clusterConnnectionString)
        {
            string URL = clusterConnnectionString+"/Partitions/"+partitionId+"/$/GetBackups";
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
                JArray array = (JArray)content["Items"];
                foreach (var item in array)
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, "BackUpID :" + item["BackupId"]);
                }
                return array.Last;
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }
        }

        public async Task<BackupStorage> GetBackupStorageDetails(string policy)
        {
            IPolicyStorageService policyStorageClient = ServiceProxy.Create<IPolicyStorageService>(new Uri("fabric:/CBA/PolicyStorageService"));
            BackupStorage backupStorage = await policyStorageClient.GetPolicyStorageDetails(policy);
            return backupStorage;
        }

        public async Task<bool> BelongsToPartition(long hashCode)
        {
            FabricClient fabricClient = new FabricClient();
            System.Fabric.Query.ServicePartitionList partitions = await fabricClient.QueryManager.GetPartitionAsync(this.Context.PartitionId);
            foreach(var partition in partitions)
            {
                var int64PartitionInfo = partition.PartitionInformation as Int64RangePartitionInformation;
                long? lowKey = int64PartitionInfo?.LowKey;
                long? highKey = int64PartitionInfo?.HighKey;
                if (hashCode >= lowKey && hashCode <= highKey)
                    return true;
            }
            return false;
        }
    }
}

