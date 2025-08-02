using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models
{
    /// <summary>
    /// Настройки производительности
    /// </summary>
    public class PerformanceSettings
    {
        /// <summary>
        /// Максимум одновременных загрузок
        /// </summary>
        public int MaxConcurrentUploads { get; set; } = 3;

        /// <summary>
        /// Размер буфера FileWatcher
        /// </summary>
        public int FileWatcherBufferSize { get; set; } = 8192;

        /// <summary>
        /// Порог памяти в МБ
        /// </summary>
        public int MemoryThresholdMB { get; set; } = 500;

        /// <summary>
        /// Максимальный размер лог-файла в МБ
        /// </summary>
        public int MaxLogFileSizeMB { get; set; } = 100;

        /// <summary>
        /// Интервал сохранения статистики в минутах
        /// </summary>
        public int StatisticsSaveIntervalMinutes { get; set; } = 10;

        /// <summary>
        /// Интервал проверки здоровья в минутах
        /// </summary>
        public int HealthCheckIntervalMinutes { get; set; } = 5;
    }

}
