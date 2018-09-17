using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupAndStart
{
    public class BackupFile
    {
        public BackupFile(String file)
        {
            backupName = System.IO.Path.GetFileNameWithoutExtension(file);
            backupDate = System.IO.File.GetCreationTime(file);
            System.IO.FileInfo fileInfo = new System.IO.FileInfo(file);
            size = fileInfo.Length / 1000000f;
        }

        private string backupName;
        private float size;
        private DateTime backupDate;

        public string BackupName
        {
            get
            {
                return backupName;
            }
            set
            {
                backupName = value;
            }
        }

        public String Size
        {
            get
            {
                return size + " MB";
            }

        }
        [DisplayName("Date")]
        public DateTime CreationDate
        {
            get
            {
                return backupDate;
            }
        }


    }
}
