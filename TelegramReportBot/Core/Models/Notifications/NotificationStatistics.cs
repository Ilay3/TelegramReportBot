using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramReportBot.Core.Enums;

namespace TelegramReportBot.Core.Models.Notifications
{
    /// <summary>
    /// Статистика уведомлений
    /// </summary>
    public class NotificationStatistics
    {
        /// <summary>
        /// Общее количество уведомлений
        /// </summary>
        public int TotalNotifications { get; set; }

        /// <summary>
        /// Уведомлений сегодня
        /// </summary>
        public int NotificationsToday { get; set; }

        /// <summary>
        /// Уведомлений на этой неделе
        /// </summary>
        public int NotificationsThisWeek { get; set; }

        /// <summary>
        /// Уведомлений в этом месяце
        /// </summary>
        public int NotificationsThisMonth { get; set; }

        /// <summary>
        /// Уведомления по типам
        /// </summary>
        public Dictionary<NotificationType, int> NotificationsByType { get; set; } = new();

        /// <summary>
        /// Уведомления по приоритетам
        /// </summary>
        public Dictionary<NotificationPriority, int> NotificationsByPriority { get; set; } = new();

        /// <summary>
        /// Время последнего уведомления
        /// </summary>
        public DateTime? LastNotification { get; set; }

        /// <summary>
        /// Заголовок последнего уведомления
        /// </summary>
        public string? LastNotificationTitle { get; set; }

        /// <summary>
        /// Количество подписчиков
        /// </summary>
        public int SubscribersCount { get; set; }

        /// <summary>
        /// Размер очереди
        /// </summary>
        public int QueueSize { get; set; }

        /// <summary>
        /// Количество запланированных уведомлений
        /// </summary>
        public int ScheduledNotificationsCount { get; set; }
    }
}
