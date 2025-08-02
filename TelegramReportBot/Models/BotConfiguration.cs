namespace TelegramReportBot.Models
{
    /// <summary>
    /// Расширенная конфигурация бота с дополнительными настройками
    /// </summary>
    public class BotConfiguration
    {
        /// <summary>
        /// Токен Telegram-бота
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// ID чата группы
        /// </summary>
        public string ChatId { get; set; } = string.Empty;

        /// <summary>
        /// ID топиков для разных типов отчётов
        /// </summary>
        public TopicIds TopicIds { get; set; } = new();

        /// <summary>
        /// Папка для мониторинга PDF-файлов
        /// </summary>
        public string ReportsFolder { get; set; } = string.Empty;

        /// <summary>
        /// Файл для хранения списка отправленных файлов
        /// </summary>
        public string SentFilesDatabase { get; set; } = string.Empty;

        /// <summary>
        /// Список администраторов бота
        /// </summary>
        public List<string> AdminUsers { get; set; } = new();

        /// <summary>
        /// Настройки уведомлений
        /// </summary>
        public NotificationSettings NotificationSettings { get; set; } = new();

        /// <summary>
        /// Настройки ограничения скорости
        /// </summary>
        public RateLimitingSettings RateLimiting { get; set; } = new();
    }
}