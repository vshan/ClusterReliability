using Microsoft.ServiceFabric.Services.Remoting;
using Microsoft.ServiceFabric.Services.Remoting.FabricTransport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace RestoreService
{
    public interface IRestoreService :  IService
    {
        Task Configure(String applicationName, String primaryCluster, String secondaryCluster, String httpEndpoint, String clientConnectionEndpoint);

        Task<PartitionWrapper> GetStatus(Guid partitionId);

    }
}
