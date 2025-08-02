using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models
{
    /// <summary>
    /// Статистика производительности
    /// </summary>
    public class PerformanceStatistics
    {
        public double AverageProcessingTimeMs { get; set; }
        public double MinProcessingTimeMs { get; set; }
        public double MaxProcessingTimeMs { get; set; }

        public int FilesPerHour { get; set; }
        public int FilesPerDay { get; set; }

        public double SuccessRate { get; set; }
        public double ErrorRate { get; set; }

        public TimeSpan TotalProcessingTime { get; set; }
    }

}
