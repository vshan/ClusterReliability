using System;
using System.Collections;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Query;
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
    internal sealed class RestoreService : Microsoft.ServiceFabric.Services.Runtime.StatefulService, IRestoreService
    {

        public static Dictionary<Guid, Task<RestoreResult>> workFlowsInProgress;

        private System.Threading.Timer timer;

        long periodTimeSpan = 60000;

        public RestoreService(StatefulServiceContext context)
            : base(context)
        {
            workFlowsInProgress = new Dictionary<Guid, Task<RestoreResult>>();
            this.timer = new System.Threading.Timer(this.TimerTickCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

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
            var startTimeSpan = TimeSpan.Zero;
            var periodTimeSpan = TimeSpan.FromMinutes(1);

            timer.Change(0, Timeout.Infinite);

            //var timer = new System.Threading.Timer(async (e) =>
            //{
            //    await OnTimerTick();
            //}, null, startTimeSpan, periodTimeSpan);
            //List<String> list = await GetApplicationsDeployed("localhost:19000");
            //ServiceEventSource.Current.ServiceMessage(this.Context, "Application name is :"+list[0]);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        private void TimerTickCallback(object state)
        {
            try
            {
                this.OnTimerTick().Wait();
            }
            finally
            {
                // Configure timer to trigger after 1 Min.
                timer.Change(this.periodTimeSpan, Timeout.Infinite);
            }
        }

        public async Task OnTimerTick()
        {
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>("partitionDictionary");
            List<Guid> keysToRemove = new List<Guid>();
            if (workFlowsInProgress.Count != 0)
            {
                try
                {
                    foreach (KeyValuePair<Guid, Task<RestoreResult>> workFlow in workFlowsInProgress)
                    {
                        Task<RestoreResult> task = workFlow.Value;
                        if (task.IsCompleted)
                        {
                            RestoreResult restoreResult = task.Result;
                            using (ITransaction tx = this.StateManager.CreateTransaction())
                            {
                                ConditionalValue<PartitionWrapper> partitionWrapper = await myDictionary.TryGetValueAsync(tx, workFlow.Key);
                                if (partitionWrapper.HasValue)
                                {
                                    if (restoreResult.restoreState.Equals("Success"))
                                    {
                                        PartitionWrapper updatedPartitionWrapper = ObjectExtensions.Copy(partitionWrapper.Value);
                                        updatedPartitionWrapper.LastBackupRestored = restoreResult.restoreInfo;
                                        updatedPartitionWrapper.CurrentlyUnderRestore = null;
                                        await myDictionary.SetAsync(tx, workFlow.Key, updatedPartitionWrapper);
                                    }
                                    ServiceEventSource.Current.ServiceMessage(this.Context, "Successfully Restored!!! ");
                                }
                                await tx.CommitAsync();
                            }
                            keysToRemove.Add(workFlow.Key);
                        }
                    }
                    foreach(var key in keysToRemove)
                    {
                        workFlowsInProgress.Remove(key);
                    }
                }
                catch(Exception ex)
                {
                    ServiceEventSource.Current.Message("exception caught : {0}",ex);
                }
            }
            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                IAsyncEnumerable<KeyValuePair<Guid, PartitionWrapper>> enumerable = await myDictionary.CreateEnumerableAsync(tx);
                IAsyncEnumerator<KeyValuePair<Guid, PartitionWrapper>> asyncEnumerator = enumerable.GetAsyncEnumerator();
                while (await asyncEnumerator.MoveNextAsync(CancellationToken.None))
                {
                    Guid primaryPartition = asyncEnumerator.Current.Key;
                    PartitionWrapper secondaryPartition = asyncEnumerator.Current.Value;
                    if (secondaryPartition == null)
                        continue;
                    JToken backupInfoToken = await GetLatestBackupAvailable(primaryPartition, "http://" + secondaryPartition.primaryCluster + ":" + secondaryPartition.httpEndpoint);
                    if (backupInfoToken == null)
                        continue;
                    BackupInfo backupInfo = new BackupInfo(backupInfoToken["BackupId"].ToString(), backupInfoToken["BackupLocation"].ToString(), (DateTime)backupInfoToken["CreationTimeUtc"]);
                    string backupPolicy = await GetPolicy("http://" + secondaryPartition.primaryCluster + ":" + secondaryPartition.httpEndpoint, primaryPartition);
                    if (backupPolicy == null)
                        continue;
                    Task<RestoreResult> task = workFlowsInProgress.TryGetValue(primaryPartition, out Task<RestoreResult> value) ? value : null;
                    if (task == null)
                    {
                        if (secondaryPartition.LastBackupRestored == null || DateTime.Compare(backupInfo.backupTime, secondaryPartition.LastBackupRestored.backupTime) > 0)
                        {
                            Task<RestoreResult> restoreTask = Task<RestoreResult>.Run(() => RestoreWorkFlow(backupInfoToken, backupPolicy, secondaryPartition, "http://" + secondaryPartition.secondaryCluster + ":" + secondaryPartition.httpEndpoint, "partitionDictionary"));
                            workFlowsInProgress.Add(asyncEnumerator.Current.Key, restoreTask);
                            PartitionWrapper updatedPartitionWrapper = ObjectExtensions.Copy(secondaryPartition);
                            updatedPartitionWrapper.LatestBackupAvailable = backupInfo;
                            updatedPartitionWrapper.CurrentlyUnderRestore = backupInfo;
                            await myDictionary.SetAsync(tx, primaryPartition, updatedPartitionWrapper);
                        }
                        else
                            continue;
                    }
                    else if (task.IsCompleted)
                    {
                        RestoreResult restoreResult = task.Result;
                        if (restoreResult.restoreState.Equals("Success"))
                        {
                            PartitionWrapper updatedPartitionWrapper = ObjectExtensions.Copy(secondaryPartition);
                            updatedPartitionWrapper.LastBackupRestored = restoreResult.restoreInfo;
                            updatedPartitionWrapper.CurrentlyUnderRestore = null;
                            await myDictionary.SetAsync(tx, primaryPartition, updatedPartitionWrapper);
                            ServiceEventSource.Current.ServiceMessage(this.Context, "Successfully Restored!!! ");
                        }
                        workFlowsInProgress.Remove(primaryPartition);
                        if (secondaryPartition.LastBackupRestored == null || DateTime.Compare(backupInfo.backupTime, secondaryPartition.LastBackupRestored.backupTime) > 0)
                        {
                            Task<RestoreResult> restoreTask = Task<string>.Run(() => RestoreWorkFlow(backupInfoToken, backupPolicy, secondaryPartition, "http://" + secondaryPartition.secondaryCluster + ":" + secondaryPartition.httpEndpoint, "partitionDictionary"));
                            workFlowsInProgress.Add(primaryPartition, restoreTask);
                            PartitionWrapper updatedPartitionWrapper = ObjectExtensions.Copy(secondaryPartition);
                            updatedPartitionWrapper.LatestBackupAvailable = backupInfo;
                            updatedPartitionWrapper.CurrentlyUnderRestore = backupInfo;
                            await myDictionary.SetAsync(tx, primaryPartition, updatedPartitionWrapper);
                        }
                        else
                            continue;
                    }
                    else
                    {
                        PartitionWrapper updatedPartitionWrapper = ObjectExtensions.Copy(secondaryPartition);
                        updatedPartitionWrapper.LatestBackupAvailable = backupInfo;
                        await myDictionary.SetAsync(tx, primaryPartition, updatedPartitionWrapper);
                    }
                }
                await tx.CommitAsync();
            }

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

        public async Task Configure(List<string> applications, List<PolicyStorageEntity> policyDeatils, String primaryCluster, String secondaryCluster, String httpEndpoint, String clientConnectionEndpoint)
        {
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>("partitionDictionary");
            IPolicyStorageService policyStorageClient = ServiceProxy.Create<IPolicyStorageService>(new Uri("fabric:/CBA/PolicyStorageService"));
            bool stored = await policyStorageClient.PostStorageDetails(policyDeatils, primaryCluster + ':' + httpEndpoint);
            foreach(string application in applications)
            {
                await MapPartitionsOfApplication(new Uri("fabric:/" + application), primaryCluster, secondaryCluster, httpEndpoint, clientConnectionEndpoint, "partitionDictionary");
            }

            /*using (ITransaction tx = this.StateManager.CreateTransaction())
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
                    if (secondaryPartition.LastBackupRestored == null || DateTime.Compare((DateTime)backupInfo["CreationTimeUtc"], secondaryPartition.LastBackupRestored.backupTime) > 0)
                        await RestoreLatestBackupAvailable(backupInfo, backupPolicy, secondaryPartition, "http://" + secondaryCluster + ":" + httpEndpoint, "partitionDictionary");
                    else
                        continue;
                }
                await tx.CommitAsync();
            }
            */
//            await OnTimerTick();
        }

        public async Task<string> Disconfigure(string applicationName)
        {
            List<Guid> keysToRemove = new List<Guid>();
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>("partitionDictionary");
            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                IAsyncEnumerable<KeyValuePair<Guid, PartitionWrapper>> enumerable = await myDictionary.CreateEnumerableAsync(tx);
                IAsyncEnumerator<KeyValuePair<Guid, PartitionWrapper>> asyncEnumerator = enumerable.GetAsyncEnumerator();
                while (await asyncEnumerator.MoveNextAsync(CancellationToken.None))
                {
                    PartitionWrapper secondaryPartition = asyncEnumerator.Current.Value;
                    if (secondaryPartition.applicationName.ToString().Equals(applicationName))
                    {
                        keysToRemove.Add(asyncEnumerator.Current.Key);
                    }
                }
                await tx.CommitAsync();
            }
            bool allPartitionsRemoved = true;
            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                foreach(Guid key in keysToRemove)
                {
                    ConditionalValue<PartitionWrapper> value  = myDictionary.TryRemoveAsync(tx, key).Result;
                    if (!value.HasValue)
                    {
                        allPartitionsRemoved = false;
                    }
                }
                await tx.CommitAsync();
            }
            if (allPartitionsRemoved) return applicationName;
            return null;
        }

        public async Task<List<PartitionWrapper>> GetStatus()
        {
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>("partitionDictionary");
            List<PartitionWrapper> mappedPartitions = new List<PartitionWrapper>();
            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                IAsyncEnumerable<KeyValuePair<Guid, PartitionWrapper>> enumerable = await myDictionary.CreateEnumerableAsync(tx);
                IAsyncEnumerator<KeyValuePair<Guid, PartitionWrapper>> asyncEnumerator = enumerable.GetAsyncEnumerator();
                while (await asyncEnumerator.MoveNextAsync(CancellationToken.None))
                {
                    ConditionalValue<PartitionWrapper> partitionWrapper = await myDictionary.TryGetValueAsync(tx,asyncEnumerator.Current.Key);
                    if (partitionWrapper.HasValue)
                    {
                        PartitionWrapper mappedPartition = partitionWrapper.Value;
                        mappedPartitions.Add(mappedPartition);
                        ServiceEventSource.Current.ServiceMessage(this.Context, "Successfully Retrieved!!! ");
                    }
                }
                await tx.CommitAsync();
            }
            return mappedPartitions;
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
                if (content == null)
                    return null;
                string policy = content["PolicyName"].ToString();
                return policy;
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }
        }

        public async Task<RestoreResult> RestoreWorkFlow(JToken latestbackupInfo, string policy, PartitionWrapper partition, String clusterConnectionString,String partitionDictionary)
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
                return null;
            }
            BackupInfo backupInfo = new BackupInfo(latestbackupInfo["BackupId"].ToString(), latestbackupInfo["BackupLocation"].ToString(), backupStorage, (DateTime)latestbackupInfo["CreationTimeUtc"]);
            // List data response.
            HttpResponseMessage response = await client.PostAsJsonAsync(urlParameters,backupInfo);  // Blocking call!
            if (response.IsSuccessStatusCode)
            {
                string restoreState = "";
                do
                {
                    await Task.Delay(10000);
                    restoreState = GetRestoreState(partition, clusterConnectionString);
                } while (restoreState.Equals("Accepted") || restoreState.Equals("RestoreInProgress"));
                return new RestoreResult(backupInfo, restoreState);
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }

        }

        public async Task MapPartitionsOfApplication(Uri applicationName, string primaryCluster, string secondaryCluster, string httpEndpoint, string clientConnectionEndpoint, String reliableDictionary)
        {
            FabricClient primaryFabricClient = new FabricClient(primaryCluster + ':' + clientConnectionEndpoint);
            FabricClient secondaryFabricClient = new FabricClient(secondaryCluster + ':' + clientConnectionEndpoint);
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>(reliableDictionary);
            ServiceList services = await primaryFabricClient.QueryManager.GetServiceListAsync(applicationName);
            foreach(Service service in services)
            {
                ServicePartitionList primaryPartitions = await primaryFabricClient.QueryManager.GetPartitionListAsync(service.ServiceName);
                ServicePartitionList secondaryPartitions = await secondaryFabricClient.QueryManager.GetPartitionListAsync(service.ServiceName);
                await MapPartitions(applicationName, service.ServiceName, httpEndpoint, primaryCluster, primaryPartitions, secondaryCluster, secondaryPartitions, reliableDictionary);
                using(var tx = this.StateManager.CreateTransaction())
                {
                    var result = await myDictionary.GetCountAsync(tx);
                    ServiceEventSource.Current.ServiceMessage(this.Context, "The number of items in dictionary are : {0}", result);
                    await tx.CommitAsync();
                }
            }
        }

        public async Task MapPartitions(Uri applicationName, Uri serviceName, string httpEndpoint, String primaryCluster, ServicePartitionList partitionsInPrimary, String secondaryCluster,ServicePartitionList partitionsInSecondary, string reliableDictionary)
        {
            if (partitionsInPrimary != null)
            {
                ServicePartitionKind partitionKind = partitionsInPrimary[0].PartitionInformation.Kind;
                if (partitionKind.Equals(ServicePartitionKind.Int64Range))
                {
                    await MapInt64Partitions(applicationName, serviceName, httpEndpoint, primaryCluster, partitionsInPrimary, secondaryCluster, partitionsInSecondary, reliableDictionary);
                }
                else if (partitionKind.Equals(ServicePartitionKind.Named))
                {
                    await MapNamedPartitions(applicationName, serviceName, httpEndpoint, primaryCluster, partitionsInPrimary, secondaryCluster, partitionsInSecondary, reliableDictionary);
                }
                else if (partitionKind.Equals(ServicePartitionKind.Singleton))
                {
                    await MapSingletonPartition(applicationName, serviceName, httpEndpoint, primaryCluster, partitionsInPrimary, secondaryCluster, partitionsInSecondary, reliableDictionary);
                }
            }
        }

        public async Task MapInt64Partitions(Uri applicationName, Uri serviceName, string httpEndpoint, String primaryCluster, ServicePartitionList primaryPartitions, String secondaryCluster, ServicePartitionList secondaryPartitions, string reliableDictionary)
        {
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>(reliableDictionary);
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
                                var result = await myDictionary.TryAddAsync(tx, primaryPartition.PartitionInformation.Id, new PartitionWrapper(secondaryPartition, primaryPartition.PartitionInformation.Id, applicationName, serviceName, httpEndpoint, primaryCluster, secondaryCluster));

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

        public async Task MapNamedPartitions(Uri applicationName, Uri serviceName, string httpEndpoint, string primaryCluster, ServicePartitionList primaryPartitions, string secondaryCluster, ServicePartitionList secondaryPartitions, string reliableDictionary)
        {
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>(reliableDictionary);
            foreach (var primaryPartition in primaryPartitions)
            {
                var partitionNamePrimary = (primaryPartition.PartitionInformation as NamedPartitionInformation).Name;
                foreach (var secondaryPartition in secondaryPartitions)
                {
                    string partitionNameSecondary = (secondaryPartition.PartitionInformation as NamedPartitionInformation).Name;
                    if (partitionNamePrimary == partitionNameSecondary)
                    {
                        using (var tx = this.StateManager.CreateTransaction())
                        {
                            var result = await myDictionary.TryAddAsync(tx, primaryPartition.PartitionInformation.Id, new PartitionWrapper(secondaryPartition, primaryPartition.PartitionInformation.Id, applicationName, serviceName, httpEndpoint, primaryCluster, secondaryCluster));

                            ServiceEventSource.Current.ServiceMessage(this.Context, result ? "Successfully Mapped Partition-{0} to Partition-{1}" : "Already Exists", primaryPartition.PartitionInformation.Id, secondaryPartition.PartitionInformation.Id);
                            // If an exception is thrown before calling CommitAsync, the transaction aborts, all changes are 
                            // discarded, and nothing is saved to the secondary replicas.
                            await tx.CommitAsync();
                        }
                    }
                }
            }
        }

        public async Task MapSingletonPartition(Uri applicationName, Uri serviceName, string httpEndpoint, String primaryCluster, ServicePartitionList primaryPartitions, String secondaryCluster, ServicePartitionList secondaryPartitions, string reliableDictionary)
        {
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>(reliableDictionary);
            using (var tx = this.StateManager.CreateTransaction())
            {
                var result = await myDictionary.TryAddAsync(tx, primaryPartitions[0].PartitionInformation.Id, new PartitionWrapper(secondaryPartitions[0], primaryPartitions[0].PartitionInformation.Id, applicationName, serviceName, httpEndpoint, primaryCluster, secondaryCluster));

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

        public string GetRestoreState(PartitionWrapper partition, string clusterConnectionString)
        {
            string URL =  clusterConnectionString + "/Partitions/" + partition.partitionId + "/$/GetRestoreProgress";
            string urlParameters = "?api-version=6.2-preview";
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(URL);
            client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

            // List data response.
            HttpResponseMessage response = client.GetAsync(urlParameters).Result;  // Blocking call!
            if (response.IsSuccessStatusCode)
            {
                // Parse the response body. Blocking!
                var content = response.Content.ReadAsAsync<JObject>().Result;
                string restoreState = content["RestoreState"].ToString();
                return restoreState;
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

