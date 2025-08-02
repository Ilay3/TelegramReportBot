namespace TelegramReportBot.Common.Models;

/// <summary>
/// Идентификаторы топиков Telegram-группы
/// </summary>
public class TopicIds
{
    /// <summary>
    /// Топик для предупреждений
    /// </summary>
    public int Warnings { get; set; } = 11;

    /// <summary>
    /// Топик для пользовательских ошибок
    /// </summary>
    public int UserErrors { get; set; } = 9;

    /// <summary>
    /// Топик для серверных ошибок
    /// </summary>
    public int ServerErrors { get; set; } = 7;
}