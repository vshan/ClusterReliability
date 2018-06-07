using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;

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

        public async Task PostStorageDetails(string backupPolicy, BackupStorage backupStorage)
        {
            IReliableDictionary<string, BackupStorage> myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, BackupStorage>>("storageDictionary");
            using (var tx = this.StateManager.CreateTransaction())
            {
                var result = await myDictionary.TryAddAsync(tx, backupPolicy, backupStorage);

                ServiceEventSource.Current.ServiceMessage(this.Context, result ? "Successfully added policy {0} storgae details" : "Already Exists", backupPolicy);

                // If an exception is thrown before calling CommitAsync, the transaction aborts, all changes are 
                // discarded, and nothing is saved to the secondary replicas.
                await tx.CommitAsync();
            }
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
    }
}
