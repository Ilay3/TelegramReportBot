using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FileInfo = TelegramReportBot.Core.Models.FileProcessing.FileInfo;

namespace TelegramReportBot.Core.Models.Statistics
{
    /// <summary>
    /// Отчёт о статистике работы бота
    /// </summary>
    public class StatisticsReport
    {
        /// <summary>
        /// Время генерации отчёта
        /// </summary>
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Время работы бота
        /// </summary>
        public TimeSpan Uptime { get; set; }

        /// <summary>
        /// Общая статистика файлов
        /// </summary>
        public FileStatistics FileStats { get; set; } = new();

        /// <summary>
        /// Статистика производительности
        /// </summary>
        public PerformanceStatistics Performance { get; set; } = new();

        /// <summary>
        /// Статистика ошибок
        /// </summary>
        public ErrorStatistics Errors { get; set; } = new();

        /// <summary>
        /// Использование ресурсов
        /// </summary>
        public ResourceUsage Resources { get; set; } = new();

        /// <summary>
        /// Активность пользователей
        /// </summary>
        public UserActivity UserActivity { get; set; } = new();

        /// <summary>
        /// Топ-файлы по размеру
        /// </summary>
        public List<FileInfo> LargestFiles { get; set; } = new();

        /// <summary>
        /// Последние ошибки
        /// </summary>
        public List<ErrorInfo> RecentErrors { get; set; } = new();
    }

}
