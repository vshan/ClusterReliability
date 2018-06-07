using Newtonsoft.Json.Linq;
using PolicyStorageService;
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
    [Serializable]
    [DataContract]
    public class PartitionWrapper
    {
        [DataMember]
        public Guid partitionId { get; set; }

        [DataMember]
        public BackupInfo LastBackup { get; set; }

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

        public PartitionWrapper(Partition partition, JToken backupItem)
        {
            this.partitionId = partition.PartitionInformation.Id;
            this.PartitionInformation = partition.PartitionInformation;
            this.ServiceKind = partition.ServiceKind;
            this.HealthState = partition.HealthState;
            this.PartitionStatus = partition.PartitionStatus;
            this.LastBackup.backupId = backupItem["BackupId"].ToString();
            this.LastBackup.backupLocation = backupItem["BackupLocation"].ToString();
            this.LastBackup.latestBackupRestored = (DateTime)backupItem["CreationTimeUtc"];
        }

        public PartitionWrapper(PartitionWrapper partitionWrapper)
        {
            this.partitionId = partitionWrapper.PartitionInformation.Id;
            this.PartitionInformation = partitionWrapper.PartitionInformation;
            this.ServiceKind = partitionWrapper.ServiceKind;
            this.HealthState = partitionWrapper.HealthState;
            this.PartitionStatus = partitionWrapper.PartitionStatus;
        }
    }

    [DataContract]
    public class BackupInfo
    {
        [DataMember]
        public string backupId { get; set; }

        [DataMember]
        public string backupLocation { get; set; }

        [DataMember]
        public DateTime latestBackupRestored { get; set; }

        public BackupStorage storageDetails;

        public BackupInfo(string backupId, string backupLocation, BackupStorage storageDetails, DateTime latestBackupRestored)
        {
            this.backupId = backupId;
            this.backupLocation = backupLocation;
            this.storageDetails = storageDetails;
            this.latestBackupRestored = latestBackupRestored;
        }

        public BackupInfo(string backupId, string backupLocation, DateTime latestBackupRestored)
        {
            this.backupId = backupId;
            this.backupLocation = backupLocation;
            this.latestBackupRestored = latestBackupRestored;
        }
    }
}
