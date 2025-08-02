using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Core.Models.Statistics
{
    /// <summary>
    /// Статистика ошибок
    /// </summary>
    public class ErrorStatistics
    {
        /// <summary>
        /// Всего ошибок
        /// </summary>
        public int TotalErrors { get; set; }

        /// <summary>
        /// Ошибок сегодня
        /// </summary>
        public int ErrorsToday { get; set; }

        /// <summary>
        /// Ошибок на этой неделе
        /// </summary>
        public int ErrorsThisWeek { get; set; }

        /// <summary>
        /// Сетевые ошибки
        /// </summary>
        public int NetworkErrors { get; set; }

        /// <summary>
        /// Ошибки файловой системы
        /// </summary>
        public int FileSystemErrors { get; set; }

        /// <summary>
        /// Ошибки Telegram API
        /// </summary>
        public int TelegramApiErrors { get; set; }

        /// <summary>
        /// Ошибки валидации
        /// </summary>
        public int ValidationErrors { get; set; }

        /// <summary>
        /// Время последней ошибки
        /// </summary>
        public DateTime? LastError { get; set; }

        /// <summary>
        /// Сообщение последней ошибки
        /// </summary>
        public string? LastErrorMessage { get; set; }
    }
}
