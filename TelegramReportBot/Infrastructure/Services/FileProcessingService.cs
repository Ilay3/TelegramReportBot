using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using TelegramReportBot.Core.Enums;
using TelegramReportBot.Core.Interfaces;
using TelegramReportBot.Core.Models.Configuration;
using TelegramReportBot.Core.Models.FileProcessing;
using TelegramReportBot.Core.Models.Statistics;
using TelegramReportBot.Core.Models.Security;
using TelegramReportBot.Core.Enum;
using TelegramReportBot.Core.Interface;

namespace TelegramReportBot.Infrastructure.Services;

/// <summary>
/// Расширенный сервис обработки и отправки PDF-файлов
/// </summary>
public class FileProcessingService : IFileProcessingService, IDisposable
{
    private readonly BotConfiguration _config;
    private readonly PerformanceSettings _performanceSettings;
    private readonly ITelegramBotService _telegramService;
    private readonly ISecurityService _securityService;
    private readonly IStatisticsService _statisticsService;
    private readonly ILogger<FileProcessingService> _logger;

    private readonly HashSet<string> _sentFiles;
    private readonly ConcurrentQueue<FileProcessingTask> _processingQueue;
    private readonly SemaphoreSlim _semaphore;
    private readonly SemaphoreSlim _fileSemaphore;
    private readonly Timer _healthCheckTimer;
    private readonly Timer _queueProcessorTimer;

    private volatile bool _isProcessing = false;
    private DateTime _lastHealthCheck = DateTime.Now;

    public FileProcessingService(
        IOptions<BotConfiguration> config,
        IOptions<PerformanceSettings> performanceSettings,
        ITelegramBotService telegramService,
        ISecurityService securityService,
        IStatisticsService statisticsService,
        ILogger<FileProcessingService> logger)
    {
        _config = config.Value;
        _performanceSettings = performanceSettings.Value;
        _telegramService = telegramService;
        _securityService = securityService;
        _statisticsService = statisticsService;
        _logger = logger;

        _sentFiles = new HashSet<string>();
        _processingQueue = new ConcurrentQueue<FileProcessingTask>();
        _semaphore = new SemaphoreSlim(1, 1);
        _fileSemaphore = new SemaphoreSlim(_performanceSettings.MaxConcurrentUploads);

        // Таймеры для фоновых задач
        _healthCheckTimer = new Timer(PerformHealthCheck, null,
            TimeSpan.FromMinutes(_performanceSettings.HealthCheckIntervalMinutes),
            TimeSpan.FromMinutes(_performanceSettings.HealthCheckIntervalMinutes));

        _queueProcessorTimer = new Timer(ProcessQueue, null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        // Загружаем список уже отправленных файлов
        LoadSentFilesAsync().Wait();
    }

    /// <summary>
    /// Обработка одного файла с расширенной функциональностью
    /// </summary>
    public async Task<FileProcessingResult> ProcessFileAsync(string filePath)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new FileProcessingResult
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            ProcessedAt = DateTime.Now
        };

        try
        {
            _logger.LogDebug("🔄 Начало обработки файла: {FileName}", result.FileName);

            // Проверка безопасности файла
            var validationResult = await _securityService.ValidateFileAsync(filePath);
            if (!validationResult.IsValid)
            {
                result.ErrorMessage = $"Файл не прошел проверку безопасности: {string.Join(", ", validationResult.Errors)}";
                return result;
            }

            // Получаем информацию о файле
            var fileInfo = new System.IO.FileInfo(filePath);
            result.FileSizeBytes = fileInfo.Length;

            // Проверяем, не был ли файл уже отправлен
            if (IsFileAlreadySent(filePath))
            {
                result.ErrorMessage = "Файл уже был отправлен ранее";
                return result;
            }

            // Определяем приоритет и топик
            var (topicId, priority) = DetermineTopicAndPriority(result.FileName);
            if (topicId == 0)
            {
                result.ErrorMessage = "Не удалось определить тип отчёта по имени файла";
                return result;
            }

            result.TopicId = topicId;
            result.Priority = priority;

            // Ожидаем освобождения семафора для контроля нагрузки
            await _fileSemaphore.WaitAsync();

            try
            {
                // Выполняем отправку с повторными попытками
                var success = await SendFileWithRetryAsync(filePath, topicId, result.FileName, result);

                if (success)
                {
                    // Помечаем файл как отправленный
                    await MarkFileAsSentAsync(filePath);
                    result.Success = true;

                    _logger.LogInformation("✅ Файл {FileName} успешно обработан и отправлен в топик {TopicId} за {Duration}ms",
                        result.FileName, topicId, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    result.ErrorMessage = "Ошибка отправки в Telegram";
                }
            }
            finally
            {
                _fileSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "❌ Ошибка обработки файла {FileName}", result.FileName);
        }
        finally
        {
            stopwatch.Stop();
            result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;

            // Записываем статистику
            _statisticsService.RecordFileProcessed(result);
        }

        return result;
    }

    /// <summary>
    /// Обработка всех новых файлов в папке с улучшенной производительностью
    /// </summary>
    public async Task<int> ProcessAllNewFilesAsync()
    {
        return await ProcessFilesByTypeAsync(ReportType.All);
    }

    /// <summary>
    /// Обработка файлов определенного типа с параллельной обработкой
    /// </summary>
    public async Task<int> ProcessFilesByTypeAsync(ReportType reportType)
    {
        var stopwatch = Stopwatch.StartNew();
        var processedCount = 0;

        try
        {
            var typeName = GetReportTypeName(reportType);
            _logger.LogInformation("🚀 Начинаю обработку файлов типа '{TypeName}' в папке {ReportsFolder}",
                typeName, _config.ReportsFolder);

            // Проверяем, существует ли папка
            if (!Directory.Exists(_config.ReportsFolder))
            {
                _logger.LogWarning("📁 Папка {ReportsFolder} не существует", _config.ReportsFolder);
                return 0;
            }

            // Получаем все PDF-файлы в папке
            var pdfFiles = Directory.GetFiles(_config.ReportsFolder, "*.pdf", SearchOption.TopDirectoryOnly);

            if (pdfFiles.Length == 0)
            {
                _logger.LogInformation("📄 В папке {ReportsFolder} не найдено PDF-файлов", _config.ReportsFolder);
                return 0;
            }

            // Фильтруем файлы по типу
            var filteredFiles = FilterFilesByType(pdfFiles, reportType);

            if (filteredFiles.Count == 0)
            {
                _logger.LogInformation("🔍 Не найдено файлов типа '{TypeName}' для обработки", typeName);
                return 0;
            }

            _logger.LogInformation("📋 Найдено {FileCount} файлов типа '{TypeName}' для обработки",
                filteredFiles.Count, typeName);

            // Сортируем файлы по приоритету
            var prioritizedFiles = filteredFiles
                .Select(f => new { FilePath = f, Priority = DetermineTopicAndPriority(Path.GetFileName(f)).priority })
                .OrderByDescending(f => f.Priority)
                .ThenBy(f => File.GetCreationTime(f.FilePath))
                .Select(f => f.FilePath)
                .ToList();

            // Обрабатываем файлы с контролем параллелизма
            var semaphore = new SemaphoreSlim(_performanceSettings.MaxConcurrentUploads);
            var tasks = new List<Task<FileProcessingResult>>();

            foreach (var filePath in prioritizedFiles)
            {
                tasks.Add(ProcessFileWithSemaphoreAsync(filePath, semaphore));
            }

            // Ожидаем завершения всех задач
            var results = await Task.WhenAll(tasks);

            // Подсчитываем результаты
            var successfulResults = results.Where(r => r.Success).ToList();
            var errorResults = results.Where(r => !r.Success &&
                !string.IsNullOrEmpty(r.ErrorMessage) &&
                !r.ErrorMessage.Contains("уже был отправлен")).ToList();

            processedCount = successfulResults.Count;

            // Логируем ошибки
            foreach (var errorResult in errorResults)
            {
                _logger.LogWarning("⚠️ Не удалось обработать файл {FileName}: {ErrorMessage}",
                    errorResult.FileName, errorResult.ErrorMessage);
            }

            stopwatch.Stop();

            _logger.LogInformation("🎯 Обработка файлов типа '{TypeName}' завершена. " +
                "Успешно: {ProcessedCount}, Ошибок: {ErrorCount}, Время: {Duration}ms",
                typeName, processedCount, errorResults.Count, stopwatch.ElapsedMilliseconds);

            // Записываем статистику завершения
            _statisticsService.RecordDistributionCompleted(processedCount, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Ошибка при обработке файлов типа {ReportType}", reportType);
            _statisticsService.RecordDistributionError(reportType, ex.Message);
        }

        return processedCount;
    }

    /// <summary>
    /// Получение состояния здоровья системы
    /// </summary>
    public async Task<HealthStatus> GetHealthStatusAsync()
    {
        var healthStatus = new HealthStatus();
        var issues = new List<string>();

        try
        {
            // Проверка доступности папки
            if (!Directory.Exists(_config.ReportsFolder))
            {
                issues.Add($"Папка мониторинга недоступна: {_config.ReportsFolder}");
                healthStatus.IsHealthy = false;
            }

            // Проверка доступного места на диске
            if (Directory.Exists(_config.ReportsFolder))
            {
                var drive = new DriveInfo(Path.GetPathRoot(_config.ReportsFolder)!);
                var freeSpaceGB = drive.AvailableFreeSpace / (1024L * 1024L * 1024L);

                healthStatus.Metrics["DiskFreeSpaceGB"] = freeSpaceGB;

                if (freeSpaceGB < 1)
                {
                    issues.Add($"Мало свободного места на диске: {freeSpaceGB} ГБ");
                    healthStatus.IsHealthy = false;
                }
            }

            // Проверка использования памяти
            var process = Process.GetCurrentProcess();
            var memoryMB = process.WorkingSet64 / (1024 * 1024);
            healthStatus.Metrics["MemoryUsageMB"] = memoryMB;

            if (memoryMB > _performanceSettings.MemoryThresholdMB)
            {
                issues.Add($"Высокое использование памяти: {memoryMB} МБ");
                healthStatus.IsHealthy = false;
            }

            // Проверка размера очереди обработки
            var queueSize = _processingQueue.Count;
            healthStatus.Metrics["QueueSize"] = queueSize;

            if (queueSize > 100)
            {
                issues.Add($"Большая очередь обработки: {queueSize} файлов");
            }

            // Проверка базы данных отправленных файлов
            try
            {
                var dbSize = new System.IO.FileInfo(_config.SentFilesDatabase).Length;
                healthStatus.Metrics["DatabaseSizeBytes"] = dbSize;

                if (dbSize > 10 * 1024 * 1024) // 10 MB
                {
                    issues.Add($"База данных отправленных файлов слишком большая: {dbSize / (1024 * 1024)} МБ");
                }
            }
            catch
            {
                // База данных может не существовать, это нормально
            }

            // Проверка времени последней обработки
            var timeSinceLastCheck = DateTime.Now - _lastHealthCheck;
            if (timeSinceLastCheck > TimeSpan.FromMinutes(30))
            {
                issues.Add($"Долго не было проверок здоровья: {timeSinceLastCheck.TotalMinutes:F1} минут");
            }

            healthStatus.Issues = issues;
            healthStatus.Metrics["LastHealthCheckMinutesAgo"] = timeSinceLastCheck.TotalMinutes;
            healthStatus.Metrics["SentFilesCount"] = _sentFiles.Count;
            healthStatus.Metrics["IsProcessing"] = _isProcessing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка проверки здоровья системы");
            healthStatus.IsHealthy = false;
            healthStatus.Issues.Add($"Ошибка проверки: {ex.Message}");
        }

        return healthStatus;
    }

    /// <summary>
    /// Очистка списка отправленных файлов
    /// </summary>
    public async Task ClearSentFilesAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            _sentFiles.Clear();

            // Создаём резервную копию перед очисткой
            var backupPath = $"{_config.SentFilesDatabase}.backup.{DateTime.Now:yyyyMMdd_HHmmss}";
            if (File.Exists(_config.SentFilesDatabase))
            {
                File.Copy(_config.SentFilesDatabase, backupPath);
            }

            // Очищаем файл
            await File.WriteAllTextAsync(_config.SentFilesDatabase, string.Empty);

            _logger.LogInformation("🗑️ Список отправленных файлов очищен. Резервная копия: {BackupPath}", backupPath);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Получение детальной информации о файле
    /// </summary>
    public async Task<FileProcessingResult> GetFileDetailsAsync(string filePath)
    {
        var result = new FileProcessingResult
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath)
        };

        try
        {
            if (!File.Exists(filePath))
            {
                result.ErrorMessage = "Файл не найден";
                return result;
            }

            var fileInfo = new System.IO.FileInfo(filePath);
            result.FileSizeBytes = fileInfo.Length;
            result.ProcessedAt = fileInfo.LastWriteTime;

            // Проверяем статус отправки
            var wasAlreadySent = IsFileAlreadySent(filePath);
            if (wasAlreadySent)
            {
                result.Success = true;
                result.ErrorMessage = "Файл уже был отправлен";
            }

            // Определяем топик и приоритет
            var (topicId, priority) = DetermineTopicAndPriority(result.FileName);
            result.TopicId = topicId;
            result.Priority = priority;

            // Выполняем проверку безопасности
            var validationResult = await _securityService.ValidateFileAsync(filePath);
            if (!validationResult.IsValid)
            {
                result.ErrorMessage = $"Проблемы безопасности: {string.Join(", ", validationResult.Errors)}";
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Принудительная обработка файла (игнорируя состояние "уже отправлен")
    /// </summary>
    public async Task<FileProcessingResult> ForceProcessFileAsync(string filePath)
    {
        // Временно удаляем файл из списка отправленных
        var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();
        var wasInSentList = false;

        await _semaphore.WaitAsync();
        try
        {
            wasInSentList = _sentFiles.Remove(normalizedPath);
        }
        finally
        {
            _semaphore.Release();
        }

        try
        {
            // Обрабатываем файл как обычно
            var result = await ProcessFileAsync(filePath);

            if (!result.Success && wasInSentList)
            {
                // Если обработка не удалась, возвращаем файл в список отправленных
                await _semaphore.WaitAsync();
                try
                {
                    _sentFiles.Add(normalizedPath);
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            // При ошибке также возвращаем файл в список
            if (wasInSentList)
            {
                await _semaphore.WaitAsync();
                try
                {
                    _sentFiles.Add(normalizedPath);
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Получение статистики файлов
    /// </summary>
    public async Task<FileStatistics> GetFileStatisticsAsync()
    {
        var stats = new FileStatistics();

        try
        {
            if (!Directory.Exists(_config.ReportsFolder))
            {
                return stats;
            }

            var pdfFiles = Directory.GetFiles(_config.ReportsFolder, "*.pdf", SearchOption.TopDirectoryOnly);
            var fileInfos = pdfFiles.Select(f => new { Path = f, Info = new System.IO.FileInfo(f) }).ToList();

            stats.TotalFilesProcessed = _sentFiles.Count;
            stats.PendingFiles = fileInfos.Count(f => !IsFileAlreadySent(f.Path));

            // Статистика по типам
            foreach (var file in fileInfos)
            {
                var fileName = file.Info.Name.ToLowerInvariant();

                if (fileName.Contains("user"))
                    stats.UserErrorFiles++;
                else if (fileName.Contains("server"))
                    stats.ServerErrorFiles++;
                else if (fileName.Contains("warn"))
                    stats.WarningFiles++;
                else
                    stats.UnknownTypeFiles++;
            }

            // Размеры файлов
            if (fileInfos.Count > 0)
            {
                stats.TotalSizeBytes = fileInfos.Sum(f => f.Info.Length);
                stats.AverageFileSizeBytes = (long)fileInfos.Average(f => f.Info.Length);
            }

            // Последний обработанный файл
            var lastProcessedFile = fileInfos
                .Where(f => IsFileAlreadySent(f.Path))
                .OrderByDescending(f => f.Info.LastWriteTime)
                .FirstOrDefault();

            if (lastProcessedFile != null)
            {
                stats.LastProcessedFile = lastProcessedFile.Info.LastWriteTime;
                stats.LastProcessedFileName = lastProcessedFile.Info.Name;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка получения статистики файлов");
        }

        return stats;
    }

    /// <summary>
    /// Проверка, был ли файл уже отправлен
    /// </summary>
    public bool IsFileAlreadySent(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();
        return _sentFiles.Contains(normalizedPath);
    }

    /// <summary>
    /// Добавление файла в список отправленных
    /// </summary>
    public async Task MarkFileAsSentAsync(string filePath)
    {
        await _semaphore.WaitAsync();
        try
        {
            var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();

            if (_sentFiles.Add(normalizedPath))
            {
                // Сохраняем в файл
                var databasePath = _config.SentFilesDatabase;
                var directoryPath = Path.GetDirectoryName(databasePath);

                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                await File.AppendAllTextAsync(databasePath, normalizedPath + Environment.NewLine);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // Приватные методы

    private async Task<FileProcessingResult> ProcessFileWithSemaphoreAsync(string filePath, SemaphoreSlim semaphore)
    {
        await semaphore.WaitAsync();
        try
        {
            return await ProcessFileAsync(filePath);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<bool> SendFileWithRetryAsync(string filePath, int topicId, string caption, FileProcessingResult result)
    {
        var attempts = 0;
        var maxAttempts = _config.RateLimiting.MaxRetries;

        while (attempts < maxAttempts)
        {
            attempts++;
            result.AttemptCount = attempts;

            try
            {
                var success = await _telegramService.SendPdfFileAsync(filePath, topicId, caption);
                if (success)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Попытка {Attempt}/{MaxAttempts} отправки файла {FileName} не удалась",
                    attempts, maxAttempts, Path.GetFileName(filePath));

                if (attempts >= maxAttempts)
                {
                    result.ErrorMessage = $"Превышено максимальное количество попыток отправки: {ex.Message}";
                    break;
                }
            }

            // Экспоненциальная задержка между попытками
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempts) * _config.RateLimiting.CooldownBetweenUploads / 1000.0);
            await Task.Delay(delay);
        }

        return false;
    }

    private (int topicId, ProcessingPriority priority) DetermineTopicAndPriority(string fileName)
    {
        var lowerFileName = fileName.ToLowerInvariant();

        // Определяем приоритет по ключевым словам
        var priority = ProcessingPriority.Normal;

        if (lowerFileName.Contains("critical") || lowerFileName.Contains("urgent"))
            priority = ProcessingPriority.Critical;
        else if (lowerFileName.Contains("high") || lowerFileName.Contains("important"))
            priority = ProcessingPriority.High;
        else if (lowerFileName.Contains("low"))
            priority = ProcessingPriority.Low;

        // Определяем топик
        if (lowerFileName.Contains("user"))
        {
            return (_config.TopicIds.UserErrors, priority);
        }

        if (lowerFileName.Contains("server"))
        {
            return (_config.TopicIds.ServerErrors, priority);
        }

        if (lowerFileName.Contains("warn"))
        {
            return (_config.TopicIds.Warnings, priority);
        }

        return (0, priority);
    }

    private List<string> FilterFilesByType(string[] allFiles, ReportType reportType)
    {
        var filteredFiles = new List<string>();

        foreach (var filePath in allFiles)
        {
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();

            switch (reportType)
            {
                case ReportType.All:
                    filteredFiles.Add(filePath);
                    break;
                case ReportType.UserErrors:
                    if (fileName.Contains("user"))
                        filteredFiles.Add(filePath);
                    break;
                case ReportType.ServerErrors:
                    if (fileName.Contains("server"))
                        filteredFiles.Add(filePath);
                    break;
                case ReportType.Warnings:
                    if (fileName.Contains("warn"))
                        filteredFiles.Add(filePath);
                    break;
            }
        }

        return filteredFiles;
    }

    private string GetReportTypeName(ReportType reportType)
    {
        return reportType switch
        {
            ReportType.All => "Все отчёты",
            ReportType.UserErrors => "Пользовательские ошибки",
            ReportType.ServerErrors => "Серверные ошибки",
            ReportType.Warnings => "Предупреждения",
            _ => "Неизвестный тип"
        };
    }

    private async Task LoadSentFilesAsync()
    {
        try
        {
            var databasePath = _config.SentFilesDatabase;

            if (File.Exists(databasePath))
            {
                var lines = await File.ReadAllLinesAsync(databasePath);
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _sentFiles.Add(line.Trim());
                    }
                }

                _logger.LogInformation("📊 Загружено {Count} записей о ранее отправленных файлах", _sentFiles.Count);
            }
            else
            {
                _logger.LogInformation("📝 Файл базы данных отправленных файлов не найден, начинаем с пустого списка");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка загрузки списка отправленных файлов");
        }
    }

    private void PerformHealthCheck(object? state)
    {
        try
        {
            _lastHealthCheck = DateTime.Now;

            // Выполняем быструю проверку здоровья
            var healthTask = GetHealthStatusAsync();
            healthTask.Wait(TimeSpan.FromSeconds(10)); // Таймаут 10 секунд

            var health = healthTask.Result;
            if (!health.IsHealthy)
            {
                _logger.LogWarning("⚠️ Обнаружены проблемы здоровья системы: {Issues}",
                    string.Join("; ", health.Issues));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка периодической проверки здоровья");
        }
    }

    private void ProcessQueue(object? state)
    {
        if (_isProcessing || _processingQueue.IsEmpty)
            return;

        _isProcessing = true;

        try
        {
            while (_processingQueue.TryDequeue(out var task))
            {
                // Здесь можно добавить логику обработки задач из очереди
                // Пока что очередь не используется активно
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка обработки очереди файлов");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        _queueProcessorTimer?.Dispose();
        _semaphore?.Dispose();
        _fileSemaphore?.Dispose();
    }
}

/// <summary>
/// Настройки производительности
/// </summary>
public class PerformanceSettings
{
    /// <summary>
    /// Максимум одновременных загрузок
    /// </summary>
    public int MaxConcurrentUploads { get; set; } = 3;

    /// <summary>
    /// Размер буфера FileWatcher
    /// </summary>
    public int FileWatcherBufferSize { get; set; } = 8192;

    /// <summary>
    /// Порог памяти в МБ
    /// </summary>
    public int MemoryThresholdMB { get; set; } = 500;

    /// <summary>
    /// Максимальный размер лог-файла в МБ
    /// </summary>
    public int MaxLogFileSizeMB { get; set; } = 100;

    /// <summary>
    /// Интервал сохранения статистики в минутах
    /// </summary>
    public int StatisticsSaveIntervalMinutes { get; set; } = 10;

    /// <summary>
    /// Интервал проверки здоровья в минутах
    /// </summary>
    public int HealthCheckIntervalMinutes { get; set; } = 5;
}