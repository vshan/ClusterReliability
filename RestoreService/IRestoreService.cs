using Microsoft.ServiceFabric.Services.Remoting;
using Microsoft.ServiceFabric.Services.Remoting.FabricTransport;
using PolicyStorageService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace RestoreService
{
    public interface IRestoreService :  IService
    {
        Task Configure(List<String> applications, List<PolicyStorageEntity> policies, String primaryCluster, String secondaryCluster, String httpEndpoint, String clientConnectionEndpoint);

        Task<string> Disconfigure(string applicationName);

        Task<List<PartitionWrapper>> GetStatus();

    }
}
