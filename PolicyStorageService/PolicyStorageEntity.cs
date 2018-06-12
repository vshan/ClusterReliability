using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolicyStorageService
{
    public class PolicyStorageEntity
    {
        public string policy { get; set; }

        public BackupStorage backupStorage { get; set; }
    }
}
