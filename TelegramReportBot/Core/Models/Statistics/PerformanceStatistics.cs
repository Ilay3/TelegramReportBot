using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Core.Models.Statistics
{
    /// <summary>
    /// Статистика производительности
    /// </summary>
    public class PerformanceStatistics
    {
        /// <summary>
        /// Среднее время обработки в миллисекундах
        /// </summary>
        public double AverageProcessingTimeMs { get; set; }

        /// <summary>
        /// Минимальное время обработки в миллисекундах
        /// </summary>
        public double MinProcessingTimeMs { get; set; }

        /// <summary>
        /// Максимальное время обработки в миллисекундах
        /// </summary>
        public double MaxProcessingTimeMs { get; set; }

        /// <summary>
        /// Файлов в час
        /// </summary>
        public int FilesPerHour { get; set; }

        /// <summary>
        /// Файлов в день
        /// </summary>
        public int FilesPerDay { get; set; }

        /// <summary>
        /// Процент успешных обработок
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Процент ошибок
        /// </summary>
        public double ErrorRate { get; set; }

        /// <summary>
        /// Общее время обработки
        /// </summary>
        public TimeSpan TotalProcessingTime { get; set; }
    }


}
