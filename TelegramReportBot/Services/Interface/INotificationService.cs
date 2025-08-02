using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramReportBot.Models;
using TelegramReportBot.Models.Enum;

namespace TelegramReportBot.Services.Interface
{
    /// <summary>
    /// Интерфейс сервиса уведомлений
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Отправка уведомления
        /// </summary>
        Task SendNotificationAsync(NotificationMessage notification);

        /// <summary>
        /// Отправка группового уведомления
        /// </summary>
        Task SendBulkNotificationAsync(List<string> recipients, string message);

        /// <summary>
        /// Планирование отложенного уведомления
        /// </summary>
        Task ScheduleNotificationAsync(NotificationMessage notification, DateTime when);

        /// <summary>
        /// Получение истории уведомлений
        /// </summary>
        Task<List<NotificationMessage>> GetNotificationHistoryAsync(int count = 100);

        /// <summary>
        /// Настройка подписок пользователей
        /// </summary>
        Task ManageUserSubscriptionAsync(string userId, NotificationType type, bool subscribe);
    }
}
