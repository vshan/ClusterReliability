using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Query;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace WebInterface.Models
{
    [DataContract]
    public class PartitionStatusModel
    {

        public PartitionStatusModel(string serviceName, string partitionId, string mappedPartitionId, string lastBackupRestored, string backupId)
        {
            this.serviceName = serviceName;
            this.partitionId = partitionId;
            this.lastBackupRestored = lastBackupRestored;
            this.backupId = backupId;
            this.mappedPartitionId = mappedPartitionId;
        }

        public PartitionStatusModel(string serviceName, string partitionId, string mappedPartitionId)
        {
            this.serviceName = serviceName;
            this.partitionId = partitionId;
            this.mappedPartitionId = mappedPartitionId;
        }

        [DataMember]
        string serviceName { get; set; }

        [DataMember]
        string partitionId { get; set; }

        [DataMember]
        string mappedPartitionId { get; set; }

        [DataMember]
        string lastBackupRestored { get; set; }

        [DataMember]
        string backupId { get; set; }
    }
}
