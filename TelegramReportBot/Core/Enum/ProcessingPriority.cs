namespace TelegramReportBot.Core.Enums;

/// <summary>
/// Приоритет обработки файла
/// </summary>
public enum ProcessingPriority
{
    /// <summary>
    /// Низкий приоритет
    /// </summary>
    Low = 1,

    /// <summary>
    /// Обычный приоритет
    /// </summary>
    Normal = 2,

    /// <summary>
    /// Высокий приоритет
    /// </summary>
    High = 3,

    /// <summary>
    /// Критический приоритет
    /// </summary>
    Critical = 4
}