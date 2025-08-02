using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models
{
    /// <summary>
    /// Настройки резервного копирования
    /// </summary>
    public class BackupSettings
    {
        public bool EnableAutoBackup { get; set; } = true;
        public int BackupIntervalHours { get; set; } = 24;
        public int BackupRetentionDays { get; set; } = 30;
        public string BackupLocation { get; set; } = "Backups\\";
        public bool CompressBackups { get; set; } = true;
        public bool BackupDatabase { get; set; } = true;
        public bool BackupLogs { get; set; } = true;
    }

}
