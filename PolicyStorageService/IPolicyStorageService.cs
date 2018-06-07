using Microsoft.ServiceFabric.Services.Remoting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolicyStorageService
{
    public interface IPolicyStorageService : IService
    {
        Task PostStorageDetails(string backupPolicy, BackupStorage backupStorage);

        Task<BackupStorage> GetPolicyStorageDetails(String policy);
    }
}
