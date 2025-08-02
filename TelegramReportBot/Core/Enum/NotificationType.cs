namespace TelegramReportBot.Core.Enums;

/// <summary>
/// Тип уведомления
/// </summary>
public enum NotificationType
{
    /// <summary>
    /// Информационное уведомление
    /// </summary>
    Info,

    /// <summary>
    /// Предупреждение
    /// </summary>
    Warning,

    /// <summary>
    /// Ошибка
    /// </summary>
    Error,

    /// <summary>
    /// Успешное выполнение
    /// </summary>
    Success,

    /// <summary>
    /// Системное уведомление
    /// </summary>
    System
}

/// <summary>
/// Приоритет уведомления
/// </summary>
public enum NotificationPriority
{
    /// <summary>
    /// Низкий приоритет
    /// </summary>
    Low,

    /// <summary>
    /// Обычный приоритет
    /// </summary>
    Normal,

    /// <summary>
    /// Высокий приоритет
    /// </summary>
    High,

    /// <summary>
    /// Критический приоритет
    /// </summary>
    Critical
}