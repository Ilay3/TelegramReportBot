using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TelegramReportBot.Core.Enum;
using TelegramReportBot.Core.Enums;
using TelegramReportBot.Core.Interfaces;
using TelegramReportBot.Core.Models.Configuration;
using TelegramReportBot.Core.Models.Security;
using TelegramReportBot.Core.Models.Statistics;
using FileInfo = TelegramReportBot.Core.Models.FileProcessing.FileInfo;
using FileProcessingResult = TelegramReportBot.Core.Models.FileProcessing.FileProcessingResult;

namespace TelegramReportBot.Infrastructure.Services
{
    /// <summary>
    /// Сервис для сбора и анализа статистики работы бота
    /// </summary>
    public class StatisticsService : IStatisticsService
    {
        private readonly BotConfiguration _config;
        private readonly PerformanceSettings _performanceSettings;
        private readonly ILogger<StatisticsService> _logger;

        private readonly string _statisticsFile;
        private readonly object _lockObject = new();
        private readonly Timer _saveTimer;

        private DateTime _startTime;
        private readonly List<FileProcessingResult> _processedFiles;
        private readonly List<ErrorInfo> _errors;
        private readonly Dictionary<string, int> _userActivity;
        private readonly Dictionary<ReportType, int> _distributionCounts;
        private readonly Dictionary<AdminCommand, int> _adminActions;
        private readonly List<AuditEvent> _auditEvents;

        public StatisticsService(
            IOptions<BotConfiguration> config,
            IOptions<PerformanceSettings> performanceSettings,
            ILogger<StatisticsService> logger)
        {
            _config = config.Value;
            _performanceSettings = performanceSettings.Value;
            _logger = logger;

            _statisticsFile = Path.Combine("Data", "statistics.json");
            _processedFiles = new List<FileProcessingResult>();
            _errors = new List<ErrorInfo>();
            _userActivity = new Dictionary<string, int>();
            _distributionCounts = new Dictionary<ReportType, int>();
            _adminActions = new Dictionary<AdminCommand, int>();
            _auditEvents = new List<AuditEvent>();

            // Таймер для автоматического сохранения
            _saveTimer = new Timer(
                AutoSave,
                null,
                TimeSpan.FromMinutes(_performanceSettings.StatisticsSaveIntervalMinutes),
                TimeSpan.FromMinutes(_performanceSettings.StatisticsSaveIntervalMinutes)
            );
        }

        /// <summary>
        /// Инициализация сервиса статистики
        /// </summary>
        public async Task InitializeAsync()
        {
            _startTime = DateTime.Now;

            try
            {
                await LoadExistingStatisticsAsync();
                _logger.LogInformation("📊 Сервис статистики инициализирован");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка инициализации сервиса статистики");
            }
        }

        /// <summary>
        /// Генерация полного отчёта о статистике
        /// </summary>
        public async Task<StatisticsReport> GenerateReportAsync()
        {
            return await GenerateReportForPeriodAsync(DateTime.MinValue, DateTime.MaxValue);
        }

        /// <summary>
        /// Генерация отчёта за указанный период
        /// </summary>
        public async Task<StatisticsReport> GenerateReportForPeriodAsync(DateTime from, DateTime to)
        {
            lock (_lockObject)
            {
                try
                {
                    var report = new StatisticsReport
                    {
                        GeneratedAt = DateTime.Now,
                        Uptime = DateTime.Now - _startTime
                    };

                    // Фильтруем данные по периоду
                    var filteredFiles = _processedFiles.Where(f => f.ProcessedAt >= from && f.ProcessedAt <= to).ToList();
                    var filteredErrors = _errors.Where(e => e.OccurredAt >= from && e.OccurredAt <= to).ToList();

                    // Заполняем статистику файлов
                    report.FileStats = GenerateFileStatistics(filteredFiles);

                    // Заполняем статистику производительности
                    report.Performance = GeneratePerformanceStatistics(filteredFiles);

                    // Заполняем статистику ошибок
                    report.Errors = GenerateErrorStatistics(filteredErrors);

                    // Заполняем информацию о ресурсах
                    report.Resources = GetCurrentResourceUsage();

                    // Заполняем активность пользователей
                    report.UserActivity = GenerateUserActivity();

                    // Получаем топ-файлы
                    report.LargestFiles = GetLargestFiles(filteredFiles, 10);

                    // Получаем последние ошибки
                    report.RecentErrors = filteredErrors.TakeLast(10).ToList();

                    _logger.LogInformation("📊 Сгенерирован отчёт статистики за период {From} - {To}",
                        from.ToString("dd.MM.yyyy"), to.ToString("dd.MM.yyyy"));

                    return report;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Ошибка генерации отчёта статистики");
                    throw;
                }
            }
        }

        /// <summary>
        /// Запись события ручной рассылки
        /// </summary>
        public void RecordManualDistribution(ReportType reportType, string userId)
        {
            lock (_lockObject)
            {
                if (!_distributionCounts.ContainsKey(reportType))
                    _distributionCounts[reportType] = 0;

                _distributionCounts[reportType]++;

                RecordUserActivity(userId);

                _auditEvents.Add(new AuditEvent
                {
                    UserId = userId,
                    Action = $"ManualDistribution_{reportType}",
                    Details = $"Запущена ручная рассылка типа {reportType}",
                    Success = true
                });

                _logger.LogDebug("📈 Записана ручная рассылка: {ReportType} от {UserId}", reportType, userId);
            }
        }

        /// <summary>
        /// Запись завершения рассылки
        /// </summary>
        public void RecordDistributionCompleted(int filesProcessed, TimeSpan duration)
        {
            lock (_lockObject)
            {
                _auditEvents.Add(new AuditEvent
                {
                    UserId = "system",
                    Action = "DistributionCompleted",
                    Details = $"Обработано файлов: {filesProcessed}, Время: {duration.TotalMilliseconds}ms",
                    Success = true
                });

                _logger.LogDebug("📈 Записано завершение рассылки: {FilesProcessed} файлов за {Duration}ms",
                    filesProcessed, duration.TotalMilliseconds);
            }
        }

        /// <summary>
        /// Запись ошибки рассылки
        /// </summary>
        public void RecordDistributionError(ReportType reportType, string errorMessage)
        {
            lock (_lockObject)
            {
                var errorInfo = new ErrorInfo
                {
                    OccurredAt = DateTime.Now,
                    ErrorType = "DistributionError",
                    Message = errorMessage
                };

                _errors.Add(errorInfo);

                _auditEvents.Add(new AuditEvent
                {
                    UserId = "system",
                    Action = $"DistributionError_{reportType}",
                    Details = errorMessage,
                    Success = false,
                    ErrorMessage = errorMessage
                });

                _logger.LogDebug("📈 Записана ошибка рассылки: {ReportType} - {ErrorMessage}", reportType, errorMessage);
            }
        }

        /// <summary>
        /// Запись запроса статистики
        /// </summary>
        public void RecordStatisticsRequest(string userId)
        {
            lock (_lockObject)
            {
                RecordUserActivity(userId);

                _auditEvents.Add(new AuditEvent
                {
                    UserId = userId,
                    Action = "StatisticsRequest",
                    Details = "Запрошена статистика",
                    Success = true
                });

                _logger.LogDebug("📈 Записан запрос статистики от {UserId}", userId);
            }
        }

        /// <summary>
        /// Запись административного действия
        /// </summary>
        public void RecordAdminAction(AdminCommand command, string userId)
        {
            lock (_lockObject)
            {
                if (!_adminActions.ContainsKey(command))
                    _adminActions[command] = 0;

                _adminActions[command]++;

                RecordUserActivity(userId);

                _auditEvents.Add(new AuditEvent
                {
                    UserId = userId,
                    Action = $"AdminCommand_{command}",
                    Details = $"Выполнена админ-команда: {command}",
                    Success = true
                });

                _logger.LogDebug("📈 Записано админ-действие: {Command} от {UserId}", command, userId);
            }
        }

        /// <summary>
        /// Запись события обработки файла
        /// </summary>
        public void RecordFileProcessed(FileProcessingResult result)
        {
            lock (_lockObject)
            {
                _processedFiles.Add(result);

                if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
                {
                    _errors.Add(new ErrorInfo
                    {
                        OccurredAt = result.ProcessedAt,
                        ErrorType = "FileProcessingError",
                        Message = result.ErrorMessage,
                        FileName = result.FileName
                    });
                }

                _auditEvents.Add(new AuditEvent
                {
                    UserId = "system",
                    Action = "FileProcessed",
                    Details = $"Обработан файл: {result.FileName}",
                    Success = result.Success,
                    ErrorMessage = result.ErrorMessage
                });

                _logger.LogDebug("📈 Записана обработка файла: {FileName} - {Success}",
                    result.FileName, result.Success ? "успешно" : "ошибка");
            }
        }

        /// <summary>
        /// Сохранение статистики в файл
        /// </summary>
        public async Task SaveStatisticsAsync()
        {
            try
            {
                lock (_lockObject)
                {
                    var statisticsData = new
                    {
                        StartTime = _startTime,
                        ProcessedFiles = _processedFiles.TakeLast(1000).ToList(), // Сохраняем последние 1000 файлов
                        Errors = _errors.TakeLast(500).ToList(), // Сохраняем последние 500 ошибок
                        UserActivity = _userActivity,
                        DistributionCounts = _distributionCounts,
                        AdminActions = _adminActions,
                        AuditEvents = _auditEvents.TakeLast(1000).ToList(), // Сохраняем последние 1000 событий
                        LastSaved = DateTime.Now
                    };

                    var json = JsonSerializer.Serialize(statisticsData, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    var directory = Path.GetDirectoryName(_statisticsFile);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(_statisticsFile, json);
                }

                _logger.LogDebug("💾 Статистика сохранена в файл");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка сохранения статистики");
            }
        }

        /// <summary>
        /// Экспорт статистики в различных форматах
        /// </summary>
        public async Task<byte[]> ExportStatisticsAsync(string format, DateTime? from = null, DateTime? to = null)
        {
            var report = await GenerateReportForPeriodAsync(
                from ?? DateTime.Now.AddDays(-30),
                to ?? DateTime.Now
            );

            switch (format.ToLowerInvariant())
            {
                case "json":
                    return Encoding.UTF8.GetBytes(
                        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })
                    );

                case "csv":
                    return Encoding.UTF8.GetBytes(GenerateCsvReport(report));

                case "txt":
                    return Encoding.UTF8.GetBytes(GenerateTextReport(report));

                default:
                    throw new ArgumentException($"Неподдерживаемый формат: {format}");
            }
        }

        /// <summary>
        /// Получение топ-файлов по различным критериям
        /// </summary>
        public Task<List<FileInfo>> GetTopFilesAsync(string criteria, int count = 10)
        {
            lock (_lockObject)
            {
                var query = _processedFiles.Where(f => f.Success);

                var result = criteria.ToLowerInvariant() switch
                {
                    "size" => query.OrderByDescending(f => f.FileSizeBytes),
                    "processing_time" => query.OrderByDescending(f => f.ProcessingTimeMs),
                    "recent" => query.OrderByDescending(f => f.ProcessedAt),
                    _ => query.OrderByDescending(f => f.ProcessedAt)
                };

                var list = result.Take(count)
                    .Select(f => new FileInfo
                    {
                        FileName = f.FileName,
                        SizeBytes = f.FileSizeBytes,
                        ProcessedAt = f.ProcessedAt,
                        ProcessingTime = TimeSpan.FromMilliseconds(f.ProcessingTimeMs),
                        TopicId = f.TopicId
                    })
                    .ToList();

                return Task.FromResult(list);
            }
        }

        /// <summary>
        /// Получение трендов активности
        /// </summary>
        public async Task<Dictionary<DateTime, int>> GetActivityTrendsAsync(int days = 30)
        {
            lock (_lockObject)
            {
                var from = DateTime.Now.AddDays(-days).Date;
                var trends = new Dictionary<DateTime, int>();

                for (int i = 0; i < days; i++)
                {
                    var date = from.AddDays(i);
                    var count = _processedFiles.Count(f => f.ProcessedAt.Date == date);
                    trends[date] = count;
                }

                return trends;
            }
        }

        // Приватные методы

        private async Task LoadExistingStatisticsAsync()
        {
            if (!File.Exists(_statisticsFile))
                return;

            try
            {
                var json = await File.ReadAllTextAsync(_statisticsFile);
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                if (data.TryGetProperty("startTime", out var startTimeProperty))
                {
                    _startTime = startTimeProperty.GetDateTime();
                }

                // Загружаем другие данные...
                _logger.LogInformation("📊 Загружена существующая статистика");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Не удалось загрузить существующую статистику");
            }
        }

        private void AutoSave(object? state)
        {
            try
            {
                SaveStatisticsAsync().Wait();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка автосохранения статистики");
            }
        }

        private void RecordUserActivity(string userId)
        {
            if (!_userActivity.ContainsKey(userId))
                _userActivity[userId] = 0;

            _userActivity[userId]++;
        }

        private FileStatistics GenerateFileStatistics(List<FileProcessingResult> files)
        {
            var stats = new FileStatistics();

            var now = DateTime.Now;
            var today = now.Date;
            var weekAgo = now.AddDays(-7);
            var monthAgo = now.AddDays(-30);

            stats.TotalFilesProcessed = files.Count;
            stats.FilesProcessedToday = files.Count(f => f.ProcessedAt.Date == today);
            stats.FilesProcessedThisWeek = files.Count(f => f.ProcessedAt >= weekAgo);
            stats.FilesProcessedThisMonth = files.Count(f => f.ProcessedAt >= monthAgo);

            stats.UserErrorFiles = files.Count(f => f.TopicId == _config.TopicIds.UserErrors);
            stats.ServerErrorFiles = files.Count(f => f.TopicId == _config.TopicIds.ServerErrors);
            stats.WarningFiles = files.Count(f => f.TopicId == _config.TopicIds.Warnings);

            stats.TotalSizeBytes = files.Sum(f => f.FileSizeBytes);
            stats.AverageFileSizeBytes = files.Count > 0 ? (long)files.Average(f => f.FileSizeBytes) : 0;

            stats.FailedFiles = files.Count(f => !f.Success);

            var lastProcessed = files.OrderByDescending(f => f.ProcessedAt).FirstOrDefault();
            if (lastProcessed != null)
            {
                stats.LastProcessedFile = lastProcessed.ProcessedAt;
                stats.LastProcessedFileName = lastProcessed.FileName;
            }

            return stats;
        }

        private PerformanceStatistics GeneratePerformanceStatistics(List<FileProcessingResult> files)
        {
            var stats = new PerformanceStatistics();

            if (files.Count == 0)
                return stats;

            var processingTimes = files.Select(f => (double)f.ProcessingTimeMs).ToList();

            stats.AverageProcessingTimeMs = processingTimes.Average();
            stats.MinProcessingTimeMs = processingTimes.Min();
            stats.MaxProcessingTimeMs = processingTimes.Max();

            var successfulFiles = files.Count(f => f.Success);
            stats.SuccessRate = (double)successfulFiles / files.Count;
            stats.ErrorRate = 1.0 - stats.SuccessRate;

            var uptime = DateTime.Now - _startTime;
            if (uptime.TotalHours > 0)
            {
                stats.FilesPerHour = (int)(files.Count / uptime.TotalHours);
            }

            if (uptime.TotalDays > 0)
            {
                stats.FilesPerDay = (int)(files.Count / uptime.TotalDays);
            }

            stats.TotalProcessingTime = TimeSpan.FromMilliseconds(processingTimes.Sum());

            return stats;
        }

        private ErrorStatistics GenerateErrorStatistics(List<ErrorInfo> errors)
        {
            var stats = new ErrorStatistics();

            var now = DateTime.Now;
            var today = now.Date;
            var weekAgo = now.AddDays(-7);

            stats.TotalErrors = errors.Count;
            stats.ErrorsToday = errors.Count(e => e.OccurredAt.Date == today);
            stats.ErrorsThisWeek = errors.Count(e => e.OccurredAt >= weekAgo);

            stats.NetworkErrors = errors.Count(e => e.ErrorType.Contains("Network") || e.Message.Contains("network"));
            stats.FileSystemErrors = errors.Count(e => e.ErrorType.Contains("FileSystem") || e.Message.Contains("file"));
            stats.TelegramApiErrors = errors.Count(e => e.ErrorType.Contains("Telegram") || e.Message.Contains("telegram"));
            stats.ValidationErrors = errors.Count(e => e.ErrorType.Contains("Validation") || e.Message.Contains("validation"));

            var lastError = errors.OrderByDescending(e => e.OccurredAt).FirstOrDefault();
            if (lastError != null)
            {
                stats.LastError = lastError.OccurredAt;
                stats.LastErrorMessage = lastError.Message;
            }

            return stats;
        }

        private ResourceUsage GetCurrentResourceUsage()
        {
            var resources = new ResourceUsage();

            try
            {
                var process = Process.GetCurrentProcess();
                resources.MemoryUsageMB = process.WorkingSet64 / (1024 * 1024);
                resources.ActiveThreads = process.Threads.Count;

                // CPU usage требует более сложной реализации
                resources.CpuUsagePercent = 0; // Заглушка

                // Информация о диске
                if (Directory.Exists(_config.ReportsFolder))
                {
                    var drive = new DriveInfo(Path.GetPathRoot(_config.ReportsFolder)!);
                    resources.DiskFreeMB = drive.AvailableFreeSpace / (1024 * 1024);
                    resources.DiskUsageMB = (drive.TotalSize - drive.AvailableFreeSpace) / (1024 * 1024);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("❌ Ошибка получения информации о ресурсах: {Error}", ex.Message);
            }

            return resources;
        }

        private UserActivity GenerateUserActivity()
        {
            var activity = new UserActivity();

            activity.TotalUsers = _userActivity.Count;

            var today = DateTime.Now.Date;
            var weekAgo = DateTime.Now.AddDays(-7);

            // Эта информация требует дополнительного отслеживания активности по времени
            activity.ActiveUsersToday = _userActivity.Count; // Упрощенная версия
            activity.ActiveUsersThisWeek = _userActivity.Count;

            activity.UserActions = new Dictionary<string, int>(_userActivity);

            if (_userActivity.Count > 0)
            {
                activity.MostActiveUser = _userActivity.OrderByDescending(kv => kv.Value).First().Key;
                activity.LastUserActivity = DateTime.Now; // Упрощенная версия
            }

            return activity;
        }

        private List<Core.Models.FileProcessing.FileInfo> GetLargestFiles(List<FileProcessingResult> files, int count)
        {
            return files.Where(f => f.Success)
                .OrderByDescending(f => f.FileSizeBytes)
                .Take(count)
                .Select(f => new Core.Models.FileProcessing.FileInfo
                {
                    FileName = f.FileName,
                    SizeBytes = f.FileSizeBytes,
                    ProcessedAt = f.ProcessedAt,
                    ProcessingTime = TimeSpan.FromMilliseconds(f.ProcessingTimeMs),
                    TopicId = f.TopicId
                })
                .ToList();
        }

        private string GenerateCsvReport(StatisticsReport report)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Metric,Value");
            csv.AppendLine($"Generated At,{report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            csv.AppendLine($"Uptime,{report.Uptime}");
            csv.AppendLine($"Total Files Processed,{report.FileStats.TotalFilesProcessed}");
            csv.AppendLine($"Success Rate,{report.Performance.SuccessRate:P2}");
            csv.AppendLine($"Average Processing Time,{report.Performance.AverageProcessingTimeMs:F1}ms");
            csv.AppendLine($"Memory Usage,{report.Resources.MemoryUsageMB}MB");
            csv.AppendLine($"Total Errors,{report.Errors.TotalErrors}");

            return csv.ToString();
        }

        private string GenerateTextReport(StatisticsReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== ОТЧЁТ СТАТИСТИКИ TELEGRAM REPORTS BOT ===");
            sb.AppendLine($"Сгенерирован: {report.GeneratedAt:dd.MM.yyyy HH:mm:ss}");
            sb.AppendLine($"Время работы: {report.Uptime}");
            sb.AppendLine();

            sb.AppendLine("ФАЙЛЫ:");
            sb.AppendLine($"  Всего обработано: {report.FileStats.TotalFilesProcessed}");
            sb.AppendLine($"  Сегодня: {report.FileStats.FilesProcessedToday}");
            sb.AppendLine($"  На этой неделе: {report.FileStats.FilesProcessedThisWeek}");
            sb.AppendLine($"  В этом месяце: {report.FileStats.FilesProcessedThisMonth}");
            sb.AppendLine();

            sb.AppendLine("ПРОИЗВОДИТЕЛЬНОСТЬ:");
            sb.AppendLine($"  Успешность: {report.Performance.SuccessRate:P1}");
            sb.AppendLine($"  Среднее время обработки: {report.Performance.AverageProcessingTimeMs:F1}ms");
            sb.AppendLine($"  Файлов в час: {report.Performance.FilesPerHour}");
            sb.AppendLine();

            sb.AppendLine("РЕСУРСЫ:");
            sb.AppendLine($"  Память: {report.Resources.MemoryUsageMB} МБ");
            sb.AppendLine($"  Свободно на диске: {report.Resources.DiskFreeMB} МБ");
            sb.AppendLine();

            sb.AppendLine("ОШИБКИ:");
            sb.AppendLine($"  Всего: {report.Errors.TotalErrors}");
            sb.AppendLine($"  Сегодня: {report.Errors.ErrorsToday}");
            sb.AppendLine($"  Последняя: {report.Errors.LastError?.ToString("dd.MM.yyyy HH:mm:ss") ?? "Нет"}");

            return sb.ToString();
        }

        public void Dispose()
        {
            _saveTimer?.Dispose();
        }
    }
}