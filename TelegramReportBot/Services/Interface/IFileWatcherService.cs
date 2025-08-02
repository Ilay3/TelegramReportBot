namespace TelegramReportBot.Services
{
    /// <summary>
    /// Интерфейс сервиса мониторинга файлов
    /// </summary>
    public interface IFileWatcherService
    {
        /// <summary>
        /// Запуск мониторинга папки
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Остановка мониторинга
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken);
    }
}