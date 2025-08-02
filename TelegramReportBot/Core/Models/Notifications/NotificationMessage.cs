using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramReportBot.Core.Enums;

namespace TelegramReportBot.Core.Models.Notifications
{
    /// <summary>
    /// Уведомление для отправки
    /// </summary>
    public class NotificationMessage
    {
        /// <summary>
        /// Тип уведомления
        /// </summary>
        public NotificationType Type { get; set; }

        /// <summary>
        /// Заголовок уведомления
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Сообщение уведомления
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Время создания
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Приоритет уведомления
        /// </summary>
        public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

        /// <summary>
        /// Список получателей
        /// </summary>
        public List<string> Recipients { get; set; } = new();
    }

}
