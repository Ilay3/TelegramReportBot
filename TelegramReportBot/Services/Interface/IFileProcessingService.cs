using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramReportBot.Models.Enum;
using TelegramReportBot.Models;

namespace TelegramReportBot.Services.Interface
{
    /// <summary>
    /// Расширенный интерфейс сервиса обработки файлов
    /// </summary>
    public interface IFileProcessingService
    {
        /// <summary>
        /// Обработка нового файла (определение типа и отправка)
        /// </summary>
        Task<FileProcessingResult> ProcessFileAsync(string filePath);

        /// <summary>
        /// Обработка всех новых файлов в папке
        /// </summary>
        Task<int> ProcessAllNewFilesAsync();

        /// <summary>
        /// Обработка файлов определенного типа
        /// </summary>
        Task<int> ProcessFilesByTypeAsync(ReportType reportType);

        /// <summary>
        /// Проверка, был ли файл уже отправлен
        /// </summary>
        bool IsFileAlreadySent(string filePath);

        /// <summary>
        /// Добавление файла в список отправленных
        /// </summary>
        Task MarkFileAsSentAsync(string filePath);

        /// <summary>
        /// Получение статистики файлов по типам
        /// </summary>
        Task<FileStatistics> GetFileStatisticsAsync();

        /// <summary>
        /// Получение состояния здоровья системы
        /// </summary>
        Task<HealthStatus> GetHealthStatusAsync();

        /// <summary>
        /// Очистка списка отправленных файлов
        /// </summary>
        Task ClearSentFilesAsync();

        /// <summary>
        /// Получение детальной информации о файле
        /// </summary>
        Task<FileProcessingResult> GetFileDetailsAsync(string filePath);

        /// <summary>
        /// Принудительная обработка файла (игнорируя состояние "уже отправлен")
        /// </summary>
        Task<FileProcessingResult> ForceProcessFileAsync(string filePath);
    }

}
