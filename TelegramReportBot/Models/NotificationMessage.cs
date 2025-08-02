using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramReportBot.Models.Enum;

namespace TelegramReportBot.Models
{
    /// <summary>
    /// Уведомление для отправки
    /// </summary>
    public class NotificationMessage
    {
        public NotificationType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
        public List<string> Recipients { get; set; } = new();
    }

}
