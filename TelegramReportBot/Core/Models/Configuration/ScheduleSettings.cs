namespace TelegramReportBot.Core.Models.Configuration;

/// <summary>
/// Параметры расписания автоматических задач
/// </summary>
public class ScheduleSettings
{
    /// <summary>
    /// Cron-выражение для еженедельной статистики
    /// </summary>
    public string WeeklyStatisticsCron { get; set; } = string.Empty;

    /// <summary>
    /// Cron-выражение для автоматической рассылки отчётов
    /// </summary>
    public string ReportsCron { get; set; } = string.Empty;
}
