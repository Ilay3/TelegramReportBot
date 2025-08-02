using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Core.Models.Statistics
{
    /// <summary>
    /// Информация об ошибке
    /// </summary>
    public class ErrorInfo
    {
        /// <summary>
        /// Время возникновения ошибки
        /// </summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>
        /// Тип ошибки
        /// </summary>
        public string ErrorType { get; set; } = string.Empty;

        /// <summary>
        /// Сообщение об ошибке
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Стек вызовов
        /// </summary>
        public string? StackTrace { get; set; }

        /// <summary>
        /// Имя файла (если применимо)
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>
        /// ID пользователя (если применимо)
        /// </summary>
        public string? UserId { get; set; }
    }

}
