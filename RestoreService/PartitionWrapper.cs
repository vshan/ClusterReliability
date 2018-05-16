using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Health;
using System.Fabric.Query;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace RestoreService
{
    [DataContract]
    class PartitionWrapper
    {
        [DataMember]
        public Guid partitionId { get; set; }

        [DataMember]
        public string lastBackupAvailable{ get; set; }

        [DataMember]
        public string lastBackupRestored { get; set; }

        public ServiceKind ServiceKind { get; set; }

        public HealthState HealthState { get; set; }

        public ServicePartitionInformation PartitionInformation { get;  set; }

        public ServicePartitionStatus PartitionStatus { get;  set; }

        public PartitionWrapper(Partition partition)
        {
            this.partitionId = partition.PartitionInformation.Id;
            this.PartitionInformation = partition.PartitionInformation;
            this.ServiceKind = partition.ServiceKind;
            this.HealthState = partition.HealthState;
            this.PartitionStatus = partition.PartitionStatus;
        }
    }
}
