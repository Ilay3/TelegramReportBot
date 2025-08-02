using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramReportBot.Models;
using TelegramReportBot.Models.Enum;

namespace TelegramReportBot.Services.Interface
{
    /// <summary>
    /// Интерфейс сервиса статистики
    /// </summary>
    public interface IStatisticsService
    {
        /// <summary>
        /// Инициализация сервиса статистики
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Генерация отчёта о статистике
        /// </summary>
        Task<StatisticsReport> GenerateReportAsync();

        /// <summary>
        /// Генерация отчёта за период
        /// </summary>
        Task<StatisticsReport> GenerateReportForPeriodAsync(DateTime from, DateTime to);

        /// <summary>
        /// Запись события ручной рассылки
        /// </summary>
        void RecordManualDistribution(ReportType reportType, string userId);

        /// <summary>
        /// Запись завершения рассылки
        /// </summary>
        void RecordDistributionCompleted(int filesProcessed, TimeSpan duration);

        /// <summary>
        /// Запись ошибки рассылки
        /// </summary>
        void RecordDistributionError(ReportType reportType, string errorMessage);

        /// <summary>
        /// Запись запроса статистики
        /// </summary>
        void RecordStatisticsRequest(string userId);

        /// <summary>
        /// Запись административного действия
        /// </summary>
        void RecordAdminAction(AdminCommand command, string userId);

        /// <summary>
        /// Запись события обработки файла
        /// </summary>
        void RecordFileProcessed(FileProcessingResult result);

        /// <summary>
        /// Сохранение статистики
        /// </summary>
        Task SaveStatisticsAsync();

        /// <summary>
        /// Экспорт статистики в различных форматах
        /// </summary>
        Task<byte[]> ExportStatisticsAsync(string format, DateTime? from = null, DateTime? to = null);

        /// <summary>
        /// Получение топ-файлов по различным критериям
        /// </summary>
        Task<List<Models.FileInfo>> GetTopFilesAsync(string criteria, int count = 10);

        /// <summary>
        /// Получение трендов активности
        /// </summary>
        Task<Dictionary<DateTime, int>> GetActivityTrendsAsync(int days = 30);
    }
}
