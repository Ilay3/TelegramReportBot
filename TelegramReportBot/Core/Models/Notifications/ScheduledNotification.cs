using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Core.Models.Notifications
{
    /// <summary>
    /// Запланированное уведомление
    /// </summary>
    public class ScheduledNotification
    {
        /// <summary>
        /// Уникальный идентификатор
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Уведомление для отправки
        /// </summary>
        public NotificationMessage Notification { get; set; } = new();

        /// <summary>
        /// Запланированное время отправки
        /// </summary>
        public DateTime ScheduledTime { get; set; }

        /// <summary>
        /// Время создания
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Отправлено ли уведомление
        /// </summary>
        public bool IsSent { get; set; } = false;

        /// <summary>
        /// Время отправки
        /// </summary>
        public DateTime? SentAt { get; set; }
    }

}
