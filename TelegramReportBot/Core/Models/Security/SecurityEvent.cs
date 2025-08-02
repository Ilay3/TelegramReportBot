using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramReportBot.Core.Enum;
using TelegramReportBot.Core.Enums;

namespace TelegramReportBot.Core.Models.Security
{
    /// <summary>
    /// Событие безопасности
    /// </summary>
    public class SecurityEvent
    {
        /// <summary>
        /// Время события
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Тип события
        /// </summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>
        /// ID пользователя
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Описание события
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Уровень безопасности
        /// </summary>
        public SecurityLevel Level { get; set; } = SecurityLevel.Info;

        /// <summary>
        /// Дополнительные свойства
        /// </summary>
        public Dictionary<string, object> Properties { get; set; } = new();
    }

}
