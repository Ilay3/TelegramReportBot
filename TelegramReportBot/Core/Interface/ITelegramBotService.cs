namespace TelegramReportBot.Core.Interfaces;

public interface ITelegramBotService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task<bool> SendPdfFileAsync(string filePath, string caption, int? threadId = null);
    Task SendStartupNotificationAsync();
    Task SendErrorNotificationAsync(Exception error);
    Task SendReportsAsync(CancellationToken cancellationToken);
    Task SendErrorReportsAsync(CancellationToken cancellationToken);
    Task SendWeeklyStatisticsAsync(CancellationToken cancellationToken);
    Task SendLogFileAsync(CancellationToken cancellationToken);

}
