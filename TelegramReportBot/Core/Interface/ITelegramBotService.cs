namespace TelegramReportBot.Core.Interfaces;

public interface ITelegramBotService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task<bool> SendPdfFileAsync(string filePath, string caption);
    Task SendStartupNotificationAsync();
    Task SendErrorNotificationAsync(Exception error);
}
