using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Core.Models.FileProcessing
{
    /// <summary>
    /// Информация о файле для статистики
    /// </summary>
    public class FileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime ProcessedAt { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public int TopicId { get; set; }
    }

}
