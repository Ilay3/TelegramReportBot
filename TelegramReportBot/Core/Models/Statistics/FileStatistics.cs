using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Core.Models.Statistics
{
    /// <summary>
    /// Расширенная статистика файлов
    /// </summary>
    public class FileStatistics
    {
        /// <summary>
        /// Всего файлов обработано
        /// </summary>
        public int TotalFilesProcessed { get; set; }

        /// <summary>
        /// Файлов обработано сегодня
        /// </summary>
        public int FilesProcessedToday { get; set; }

        /// <summary>
        /// Файлов обработано на этой неделе
        /// </summary>
        public int FilesProcessedThisWeek { get; set; }

        /// <summary>
        /// Файлов обработано в этом месяце
        /// </summary>
        public int FilesProcessedThisMonth { get; set; }

        /// <summary>
        /// Файлы пользовательских ошибок
        /// </summary>
        public int UserErrorFiles { get; set; }

        /// <summary>
        /// Файлы серверных ошибок
        /// </summary>
        public int ServerErrorFiles { get; set; }

        /// <summary>
        /// Файлы предупреждений
        /// </summary>
        public int WarningFiles { get; set; }

        /// <summary>
        /// Файлы неизвестного типа
        /// </summary>
        public int UnknownTypeFiles { get; set; }

        /// <summary>
        /// Общий размер файлов в байтах
        /// </summary>
        public long TotalSizeBytes { get; set; }

        /// <summary>
        /// Средний размер файла в байтах
        /// </summary>
        public long AverageFileSizeBytes { get; set; }

        /// <summary>
        /// Файлов в ожидании обработки
        /// </summary>
        public int PendingFiles { get; set; }

        /// <summary>
        /// Неудачно обработанных файлов
        /// </summary>
        public int FailedFiles { get; set; }

        /// <summary>
        /// Время последней обработки файла
        /// </summary>
        public DateTime? LastProcessedFile { get; set; }

        /// <summary>
        /// Имя последнего обработанного файла
        /// </summary>
        public string? LastProcessedFileName { get; set; }
    }

}
