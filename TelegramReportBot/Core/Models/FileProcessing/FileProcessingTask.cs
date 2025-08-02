using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramReportBot.Core.Enum;
using TelegramReportBot.Core.Enums;

namespace TelegramReportBot.Core.Models.FileProcessing
{
    /// <summary>
    /// Задача обработки файла для очереди
    /// </summary>
    public class FileProcessingTask
    {
        /// <summary>
        /// Путь к файлу
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Тип отчета
        /// </summary>
        public ReportType ReportType { get; set; }

        /// <summary>
        /// Приоритет обработки
        /// </summary>
        public ProcessingPriority Priority { get; set; }

        /// <summary>
        /// Время создания задачи
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Количество попыток повтора
        /// </summary>
        public int RetryCount { get; set; } = 0;

        /// <summary>
        /// Кто запросил обработку
        /// </summary>
        public string? RequestedBy { get; set; }
    }
}
