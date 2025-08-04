using TelegramReportBot.Core.Enums;
using TelegramReportBot.Core.Models.Statistics;
using TelegramReportBot.Core.Models.Security;
using TelegramReportBot.Core.Models.Configuration;
using FileInfo = TelegramReportBot.Core.Models.FileProcessing.FileInfo;
using FileProcessingResult = TelegramReportBot.Core.Models.FileProcessing.FileProcessingResult;
using TelegramReportBot.Core.Enum;

namespace TelegramReportBot.Core.Interfaces;

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

    /// <summary>
    /// Получение статистики мониторинга
    /// </summary>
    Task<FileWatcherStatistics> GetStatisticsAsync();

    /// <summary>
    /// Принудительное сканирование папки
    /// </summary>
    Task ForceScanAsync();
}

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
    Task<List<FileInfo>> GetTopFilesAsync(string criteria, int count = 10);

    /// <summary>
    /// Получение трендов активности
    /// </summary>
    Task<Dictionary<DateTime, int>> GetActivityTrendsAsync(int days = 30);
}

/// <summary>
/// Интерфейс сервиса безопасности
/// </summary>
public interface ISecurityService
{
    /// <summary>
    /// Проверка прав администратора
    /// </summary>
    Task<bool> IsAdminUserAsync(string userId);

    /// <summary>
    /// Проверка безопасности файла
    /// </summary>
    Task<bool> IsFileSafeAsync(string filePath);

    /// <summary>
    /// Валидация файла
    /// </summary>
    Task<ValidationResult> ValidateFileAsync(string filePath);

    /// <summary>
    /// Выполнение проверки безопасности системы
    /// </summary>
    Task PerformSecurityCheckAsync();

    /// <summary>
    /// Логирование события безопасности
    /// </summary>
    void LogSecurityEvent(SecurityEvent securityEvent);

    /// <summary>
    /// Получение аудит-лога
    /// </summary>
    Task<List<AuditEvent>> GetAuditLogAsync(DateTime? from = null, DateTime? to = null);

    /// <summary>
    /// Блокировка подозрительного пользователя
    /// </summary>
    Task BlockUserAsync(string userId, string reason);

    /// <summary>
    /// Разблокировка пользователя
    /// </summary>
    Task UnblockUserAsync(string userId);
}

/// <summary>
/// Интерфейс сервиса резервного копирования
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Создание резервной копии
    /// </summary>
    Task<string> CreateBackupAsync();

    /// <summary>
    /// Восстановление из резервной копии
    /// </summary>
    Task RestoreBackupAsync(string backupPath);

    /// <summary>
    /// Получение списка доступных резервных копий
    /// </summary>
    Task<List<BackupInfo>> GetAvailableBackupsAsync();

    /// <summary>
    /// Удаление старых резервных копий
    /// </summary>
    Task CleanupOldBackupsAsync();

    /// <summary>
    /// Проверка целостности резервной копии
    /// </summary>
    Task<bool> VerifyBackupAsync(string backupPath);
}

/// <summary>
/// Интерфейс сервиса конфигурации
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Получение текущей конфигурации
    /// </summary>
    Task<BotConfiguration> GetConfigurationAsync();

    /// <summary>
    /// Обновление конфигурации
    /// </summary>
    Task UpdateConfigurationAsync(BotConfiguration configuration);

    /// <summary>
    /// Перезагрузка конфигурации
    /// </summary>
    Task ReloadConfigurationAsync();

    /// <summary>
    /// Валидация конфигурации
    /// </summary>
    Task<ValidationResult> ValidateConfigurationAsync(BotConfiguration configuration);

    /// <summary>
    /// Экспорт конфигурации
    /// </summary>
    Task<string> ExportConfigurationAsync();

    /// <summary>
    /// Импорт конфигурации
    /// </summary>
    Task ImportConfigurationAsync(string configData);

    /// <summary>
    /// Получение значения конфигурации по ключу
    /// </summary>
    T GetConfigValue<T>(string key, T defaultValue = default);

    /// <summary>
    /// Установка значения конфигурации
    /// </summary>
    Task SetConfigValueAsync<T>(string key, T value);
}

/// <summary>
/// Статистика работы FileWatcherService
/// </summary>
public class FileWatcherStatistics
{
    public bool IsActive { get; set; }
    public string WatchedDirectory { get; set; } = string.Empty;
    public DateTime LastSuccessfulScan { get; set; }
    public int EventProcessingErrors { get; set; }
    public int RestartAttempts { get; set; }
    public int QueueSize { get; set; }
    public int WatchedFilesCount { get; set; }
    public int TotalFilesInDirectory { get; set; }
    public int RecentFilesCount { get; set; }
}

/// <summary>
/// Информация о резервной копии
/// </summary>
public class BackupInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long SizeBytes { get; set; }
    public bool IsCompressed { get; set; }
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
}