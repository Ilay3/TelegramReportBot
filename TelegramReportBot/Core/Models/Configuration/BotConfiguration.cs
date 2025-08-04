using System.Collections.Generic;
using TelegramReportBot.Core.Models;

namespace TelegramReportBot.Core.Models.Configuration
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
        public TopicIds TopicIds { get; set; } = new TopicIds();

        /// <summary>
        /// Папка для мониторинга PDF-файлов
        /// </summary>
        public string ReportsFolder { get; set; } = string.Empty;

        /// <summary>
        /// Файл для хранения списка отправленных файлов
        /// </summary>
        public string SentFilesDatabase { get; set; } = string.Empty;

        /// <summary>
        /// Фильтры для определения типа файла по имени
        /// </summary>
        public Dictionary<string, string> FileFilters { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Настройки расписания
        /// </summary>
        public ScheduleSettings Schedule { get; set; } = new ScheduleSettings();
    }
}
