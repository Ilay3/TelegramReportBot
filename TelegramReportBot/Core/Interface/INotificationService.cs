using TelegramReportBot.Core.Enums;
using TelegramReportBot.Core.Models.Notifications;

namespace TelegramReportBot.Core.Interfaces;

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

    /// <summary>
    /// Отправка уведомления о системных событиях
    /// </summary>
    Task SendSystemNotificationAsync(string title, string message, NotificationPriority priority = NotificationPriority.Normal);

    /// <summary>
    /// Отправка уведомления об ошибке
    /// </summary>
    Task SendErrorNotificationAsync(string title, string errorMessage, Exception? exception = null);

    /// <summary>
    /// Отправка уведомления об успехе
    /// </summary>
    Task SendSuccessNotificationAsync(string title, string message, List<string>? recipients = null);

    /// <summary>
    /// Отправка предупреждения
    /// </summary>
    Task SendWarningNotificationAsync(string title, string message, List<string>? recipients = null);

    /// <summary>
    /// Получение статистики уведомлений
    /// </summary>
    Task<NotificationStatistics> GetNotificationStatisticsAsync();
}