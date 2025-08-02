using TelegramReportBot.Models;
using TelegramReportBot.Models.Enum;

namespace TelegramReportBot.Services
{
    /// <summary>
    /// Расширенный интерфейс для работы с Telegram Bot API
    /// </summary>
    public interface ITelegramBotService
    {
        /// <summary>
        /// Запуск бота (начало прослушивания команд)
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Остановка бота
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Отправка PDF-файла в указанный топик
        /// </summary>
        /// <param name="filePath">Путь к файлу</param>
        /// <param name="topicId">ID топика</param>
        /// <param name="caption">Подпись к файлу</param>
        Task<bool> SendPdfFileAsync(string filePath, int topicId, string caption);

        /// <summary>
        /// Событие для ручного запуска рассылки по типу
        /// </summary>
        event Func<ReportType, string, Task>? ManualDistributionRequested;

        /// <summary>
        /// Событие для запроса статистики
        /// </summary>
        event Func<string, Task<StatisticsReport>>? StatisticsRequested;

        /// <summary>
        /// Событие для административных команд
        /// </summary>
        event Func<AdminCommand, string, Task>? AdminCommandReceived;

        /// <summary>
        /// Отправка уведомления о запуске
        /// </summary>
        Task SendStartupNotificationAsync();

        /// <summary>
        /// Отправка уведомления о завершении работы
        /// </summary>
        Task SendShutdownNotificationAsync();

        /// <summary>
        /// Отправка уведомления об ошибке
        /// </summary>
        Task SendErrorNotificationAsync(Exception error);
    }
}