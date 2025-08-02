using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramReportBot.Models.Enum;
using TelegramReportBot.Services;

namespace TelegramReportBot.Models
{
    /// <summary>
    /// Событие безопасности
    /// </summary>
    public class SecurityEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string EventType { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public SecurityLevel Level { get; set; } = SecurityLevel.Info;
        public Dictionary<string, object> Properties { get; set; } = new();
    }
}
