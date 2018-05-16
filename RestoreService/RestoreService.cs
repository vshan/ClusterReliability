using System;
using System.Collections;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;

namespace RestoreService
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class RestoreService : StatefulService
    {
        public RestoreService(StatefulServiceContext context)
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
            return new ServiceReplicaListener[0];
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
            await GetApplicationsDeployedInCluster("localhost:19000");
        }

        public async  Task GetApplicationsDeployedInCluster(String clusterConnectionString)
        {
            FabricClient fabricClient = new FabricClient(clusterConnectionString);
            FabricClient.QueryClient queryClient = fabricClient.QueryManager;
            System.Fabric.Query.ApplicationList appsList = await queryClient.GetApplicationListAsync();
            foreach(System.Fabric.Query.Application application in appsList)
            {
                ServiceEventSource.Current.ServiceMessage(this.Context, "Application is "+ application.ApplicationName);
                await GetPartitionsOfApplication(application.ApplicationName, clusterConnectionString);
            }
        }

        public async Task GetPartitionsOfApplication(Uri applicationName, string clusterConnectionString)
        {
            FabricClient fabricClient = new FabricClient(clusterConnectionString);
            IReliableDictionary<Guid, PartitionWrapper> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, PartitionWrapper>>("myDictionary");
            System.Fabric.Query.ServiceList services = await fabricClient.QueryManager.GetServiceListAsync(applicationName);
            foreach(System.Fabric.Query.Service service in services)
            {
                System.Fabric.Query.ServicePartitionList partitions = await fabricClient.QueryManager.GetPartitionListAsync(service.ServiceName);
                await MapPartitions(partitions, partitions, myDictionary);
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
                PartitionWrapper partitionWrapper = new PartitionWrapper(primaryPartition);
                var int64PartitionInfo = primaryPartition.PartitionInformation as Int64RangePartitionInformation;
                long? lowKeyPrimary = int64PartitionInfo?.LowKey;
                foreach(var secondaryPartition in secondaryPartitions)
                {
                    long? lowKeySecondary = (secondaryPartition.PartitionInformation as Int64RangePartitionInformation)?.LowKey;
                    if(lowKeyPrimary == lowKeySecondary)
                    {
                        using (var tx = this.StateManager.CreateTransaction())
                        {
                            var result = await myDictionary.TryAddAsync(tx, primaryPartition.PartitionInformation.Id, new PartitionWrapper(secondaryPartition));

                            ServiceEventSource.Current.ServiceMessage(this.Context, result ? "Successfully Mapped Partition-{0} to Partition-{1}" : "Already Exists",primaryPartition.PartitionInformation.Id,secondaryPartition.PartitionInformation.Id);
                            // If an exception is thrown before calling CommitAsync, the transaction aborts, all changes are 
                            // discarded, and nothing is saved to the secondary replicas.
                            await tx.CommitAsync();
                        }
                    }
                }
            }
        }

        public async Task MapNamedPartitions(System.Fabric.Query.ServicePartitionList primaryPartitions, System.Fabric.Query.ServicePartitionList secondaryPartitions, IReliableDictionary<Guid, PartitionWrapper> myDictionary)
        {
            foreach(var primaryPartition in primaryPartitions)
            {
                PartitionWrapper primaryPartitionWrapper = new PartitionWrapper(primaryPartition);
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
    }
}
