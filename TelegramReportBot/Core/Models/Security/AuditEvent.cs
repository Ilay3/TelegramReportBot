using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Core.Models.Security
{
    /// <summary>
    /// Событие для аудита
    /// </summary>
    public class AuditEvent
    {
        /// <summary>
        /// Время события
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// ID пользователя
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Действие
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Детали действия
        /// </summary>
        public string Details { get; set; } = string.Empty;

        /// <summary>
        /// Успешно ли выполнено действие
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Сообщение об ошибке (если есть)
        /// </summary>
        public string? ErrorMessage { get; set; }
    }


}
