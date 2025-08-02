using TelegramReportBot.Models.Enum;

namespace TelegramReportBot.Models
{
    /// <summary>
    /// Расширенный результат обработки файла
    /// </summary>
    public class FileProcessingResult
    {
        /// <summary>
        /// Успешно ли обработан файл
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Путь к файлу
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Имя файла
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// ID топика, в который был отправлен файл
        /// </summary>
        public int TopicId { get; set; }

        /// <summary>
        /// Сообщение об ошибке (если есть)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Время обработки
        /// </summary>
        public DateTime ProcessedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Размер файла в байтах
        /// </summary>
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// Время обработки в миллисекундах
        /// </summary>
        public long ProcessingTimeMs { get; set; }

        /// <summary>
        /// ID сообщения в Telegram
        /// </summary>
        public int? TelegramMessageId { get; set; }

        /// <summary>
        /// Количество попыток отправки
        /// </summary>
        public int AttemptCount { get; set; } = 1;

        /// <summary>
        /// Приоритет обработки
        /// </summary>
        public ProcessingPriority Priority { get; set; } = ProcessingPriority.Normal;
    }
}