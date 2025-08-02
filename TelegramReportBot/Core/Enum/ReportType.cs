namespace TelegramReportBot.Core.Enum;

/// <summary>
/// Типы отчётов для раздельной рассылки
/// </summary>
public enum ReportType
{
    /// <summary>
    /// Все файлы
    /// </summary>
    All,

    /// <summary>
    /// Только пользовательские ошибки
    /// </summary>
    UserErrors,

    /// <summary>
    /// Только серверные ошибки
    /// </summary>
    ServerErrors,

    /// <summary>
    /// Только предупреждения
    /// </summary>
    Warnings
}