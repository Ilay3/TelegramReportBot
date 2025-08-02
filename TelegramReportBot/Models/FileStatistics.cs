using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models
{
    /// <summary>
    /// Расширенная статистика файлов
    /// </summary>
    public class FileStatistics
    {
        public int TotalFilesProcessed { get; set; }
        public int FilesProcessedToday { get; set; }
        public int FilesProcessedThisWeek { get; set; }
        public int FilesProcessedThisMonth { get; set; }

        public int UserErrorFiles { get; set; }
        public int ServerErrorFiles { get; set; }
        public int WarningFiles { get; set; }
        public int UnknownTypeFiles { get; set; }

        public long TotalSizeBytes { get; set; }
        public long AverageFileSizeBytes { get; set; }

        public int PendingFiles { get; set; }
        public int FailedFiles { get; set; }

        public DateTime? LastProcessedFile { get; set; }
        public string? LastProcessedFileName { get; set; }
    }

}
