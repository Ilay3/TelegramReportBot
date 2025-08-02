using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Permissions;
using TelegramReportBot.Models;
using TelegramReportBot.Models.Enum;
using TelegramReportBot.Services.Interface;

namespace TelegramReportBot.Services
{
    /// <summary>
    /// Расширенный сервис мониторинга папки с улучшенной надежностью и производительностью
    /// </summary>
    public class FileWatcherService : IFileWatcherService, IDisposable
    {
        private readonly BotConfiguration _config;
        private readonly PerformanceSettings _performanceSettings;
        private readonly IFileProcessingService _fileProcessingService;
        private readonly INotificationService _notificationService;
        private readonly ISecurityService _securityService;
        private readonly IStatisticsService _statisticsService;
        private readonly ILogger<FileWatcherService> _logger;

        private FileSystemWatcher? _fileWatcher;
        private readonly Timer _periodicCheckTimer;
        private readonly Timer _healthCheckTimer;
        private readonly Timer _cleanupTimer;
        private readonly ConcurrentDictionary<string, FileWatchInfo> _watchedFiles;
        private readonly ConcurrentQueue<FileSystemEventArgs> _eventQueue;
        private readonly SemaphoreSlim _processingSemaphore;

        private volatile bool _isDisposed = false;
        private volatile bool _isProcessingEvents = false;
        private DateTime _lastSuccessfulScan = DateTime.MinValue;
        private int _eventProcessingErrors = 0;
        private int _restartAttempts = 0;

        private readonly string[] _supportedExtensions = { ".pdf" };
        private readonly object _lockObject = new();

        public FileWatcherService(
            IOptions<BotConfiguration> config,
            IOptions<PerformanceSettings> performanceSettings,
            IFileProcessingService fileProcessingService,
            INotificationService notificationService,
            ISecurityService securityService,
            IStatisticsService statisticsService,
            ILogger<FileWatcherService> logger)
        {
            _config = config.Value;
            _performanceSettings = performanceSettings.Value;
            _fileProcessingService = fileProcessingService;
            _notificationService = notificationService;
            _securityService = securityService;
            _statisticsService = statisticsService;
            _logger = logger;

            _watchedFiles = new ConcurrentDictionary<string, FileWatchInfo>();
            _eventQueue = new ConcurrentQueue<FileSystemEventArgs>();
            _processingSemaphore = new SemaphoreSlim(1, 1);

            // Таймеры для различных фоновых задач
            _periodicCheckTimer = new Timer(PeriodicDirectoryScan, null,
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

            _healthCheckTimer = new Timer(PerformHealthCheck, null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

            _cleanupTimer = new Timer(CleanupWatchedFiles, null,
                TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        }

        /// <summary>
        /// Запуск расширенного мониторинга папки
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("🔍 Запуск расширенного мониторинга папки {ReportsFolder}", _config.ReportsFolder);

                // Проверяем и создаём папку
                await EnsureDirectoryExistsAsync();

                // Проверяем права доступа
                await ValidateDirectoryPermissionsAsync();

                // Настраиваем и запускаем FileSystemWatcher
                await StartFileSystemWatcherAsync();

                // Выполняем первоначальное сканирование
                await InitialDirectoryScanAsync();

                // Запускаем обработчик очереди событий
                _ = Task.Run(ProcessEventQueueAsync, cancellationToken);

                _logger.LogInformation("✅ Расширенный мониторинг папки запущен успешно");

                // Уведомляем о запуске
                await _notificationService.SendSystemNotificationAsync(
                    "Мониторинг файлов запущен",
                    $"Начат мониторинг папки: {_config.ReportsFolder}",
                    NotificationPriority.Low
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Критическая ошибка запуска мониторинга папки");

                await _notificationService.SendErrorNotificationAsync(
                    "Ошибка запуска мониторинга",
                    "Не удалось запустить мониторинг файлов",
                    ex
                );

                throw;
            }
        }

        /// <summary>
        /// Остановка мониторинга
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("⏹️ Остановка мониторинга папки...");

                // Останавливаем FileSystemWatcher
                if (_fileWatcher != null)
                {
                    _fileWatcher.EnableRaisingEvents = false;
                    _fileWatcher.Dispose();
                    _fileWatcher = null;
                }

                // Ожидаем завершения обработки текущих событий
                await _processingSemaphore.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                // Обрабатываем оставшиеся события в очереди
                await ProcessRemainingEventsAsync();

                _logger.LogInformation("✅ Мониторинг папки остановлен");

                // Уведомляем об остановке
                await _notificationService.SendSystemNotificationAsync(
                    "Мониторинг файлов остановлен",
                    "Мониторинг файлов корректно завершен",
                    NotificationPriority.Low
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка остановки мониторинга папки");
            }
            finally
            {
                _processingSemaphore.Release();
            }
        }

        /// <summary>
        /// Получение статистики мониторинга
        /// </summary>
        public async Task<FileWatcherStatistics> GetStatisticsAsync()
        {
            var stats = new FileWatcherStatistics
            {
                IsActive = _fileWatcher?.EnableRaisingEvents == true,
                WatchedDirectory = _config.ReportsFolder,
                LastSuccessfulScan = _lastSuccessfulScan,
                EventProcessingErrors = _eventProcessingErrors,
                RestartAttempts = _restartAttempts,
                QueueSize = _eventQueue.Count,
                WatchedFilesCount = _watchedFiles.Count
            };

            // Получаем информацию о папке
            try
            {
                if (Directory.Exists(_config.ReportsFolder))
                {
                    var files = Directory.GetFiles(_config.ReportsFolder, "*.pdf", SearchOption.TopDirectoryOnly);
                    stats.TotalFilesInDirectory = files.Length;

                    var recentFiles = files.Where(f => File.GetCreationTime(f) > DateTime.Now.AddHours(-24)).ToArray();
                    stats.RecentFilesCount = recentFiles.Length;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ошибка получения статистики папки");
            }

            return stats;
        }

        /// <summary>
        /// Принудительное сканирование папки
        /// </summary>
        public async Task ForceScanAsync()
        {
            try
            {
                _logger.LogInformation("🔄 Запуск принудительного сканирования папки");
                await PeriodicDirectoryScan(null);
                _logger.LogInformation("✅ Принудительное сканирование завершено");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка принудительного сканирования");
                throw;
            }
        }

        // Приватные методы

        private async Task EnsureDirectoryExistsAsync()
        {
            if (!Directory.Exists(_config.ReportsFolder))
            {
                try
                {
                    Directory.CreateDirectory(_config.ReportsFolder);
                    _logger.LogInformation("📁 Создана папка для мониторинга: {ReportsFolder}", _config.ReportsFolder);

                    await _notificationService.SendSystemNotificationAsync(
                        "Папка мониторинга создана",
                        $"Создана папка: {_config.ReportsFolder}",
                        NotificationPriority.Low
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Не удалось создать папку мониторинга: {ReportsFolder}", _config.ReportsFolder);
                    throw;
                }
            }
        }

        private async Task ValidateDirectoryPermissionsAsync()
        {
            try
            {
                // Проверяем права на чтение
                var files = Directory.GetFiles(_config.ReportsFolder, "*.pdf", SearchOption.TopDirectoryOnly);

                // Проверяем права на создание файлов
                var testFile = Path.Combine(_config.ReportsFolder, $"permission_test_{Guid.NewGuid():N}.tmp");
                await File.WriteAllTextAsync(testFile, "test");
                File.Delete(testFile);

                _logger.LogDebug("✅ Права доступа к папке проверены успешно");
            }
            catch (Exception ex)
            {
                var error = $"Недостаточно прав доступа к папке {_config.ReportsFolder}: {ex.Message}";
                _logger.LogError(ex, "🚫 {Error}", error);

                await _notificationService.SendErrorNotificationAsync(
                    "Ошибка прав доступа",
                    error,
                    ex
                );

                throw;
            }
        }

        private async Task StartFileSystemWatcherAsync()
        {
            try
            {
                _fileWatcher = new FileSystemWatcher(_config.ReportsFolder)
                {
                    // Настройки мониторинга
                    Filter = "*.*", // Мониторим все файлы, фильтруем в обработчике
                    NotifyFilter = NotifyFilters.FileName |
                                  NotifyFilters.CreationTime |
                                  NotifyFilters.LastWrite |
                                  NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    InternalBufferSize = _performanceSettings.FileWatcherBufferSize
                };

                // Подписываемся на события
                _fileWatcher.Created += OnFileEvent;
                _fileWatcher.Changed += OnFileEvent;
                _fileWatcher.Renamed += OnFileRenamed;
                _fileWatcher.Deleted += OnFileDeleted;
                _fileWatcher.Error += OnWatcherError;

                // Запускаем мониторинг
                _fileWatcher.EnableRaisingEvents = true;

                _logger.LogInformation("🔍 FileSystemWatcher настроен и запущен");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка настройки FileSystemWatcher");
                throw;
            }
        }

        private async Task InitialDirectoryScanAsync()
        {
            try
            {
                _logger.LogInformation("🔍 Выполняю первоначальное сканирование папки...");

                var processedCount = await _fileProcessingService.ProcessAllNewFilesAsync();

                _lastSuccessfulScan = DateTime.Now;

                _logger.LogInformation("✅ Первоначальное сканирование завершено. Обработано файлов: {ProcessedCount}", processedCount);

                if (processedCount > 0)
                {
                    await _notificationService.SendSuccessNotificationAsync(
                        "Первоначальное сканирование завершено",
                        $"Обработано файлов: {processedCount}"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка первоначального сканирования");

                await _notificationService.SendErrorNotificationAsync(
                    "Ошибка первоначального сканирования",
                    "Не удалось выполнить первоначальное сканирование папки",
                    ex
                );
            }
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Фильтруем по расширению
                if (!IsSupportedFile(e.FullPath))
                    return;

                _logger.LogDebug("📄 Событие файла: {EventType} - {FileName}", e.ChangeType, e.Name);

                // Добавляем событие в очередь для асинхронной обработки
                _eventQueue.Enqueue(e);

                // Обновляем информацию о файле
                UpdateFileWatchInfo(e.FullPath, e.ChangeType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка обработки события файла: {FileName}", e.Name);
                Interlocked.Increment(ref _eventProcessingErrors);
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                _logger.LogDebug("📝 Файл переименован: {OldName} → {NewName}", e.OldName, e.Name);

                // Удаляем информацию о старом имени
                _watchedFiles.TryRemove(e.OldFullPath, out _);

                // Если новое имя поддерживается, добавляем в очередь
                if (IsSupportedFile(e.FullPath))
                {
                    var createEvent = new FileSystemEventArgs(WatcherChangeTypes.Created, Path.GetDirectoryName(e.FullPath)!, e.Name!);
                    _eventQueue.Enqueue(createEvent);
                    UpdateFileWatchInfo(e.FullPath, WatcherChangeTypes.Created);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка обработки переименования файла: {OldName} → {NewName}", e.OldName, e.Name);
                Interlocked.Increment(ref _eventProcessingErrors);
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            try
            {
                _logger.LogDebug("🗑️ Файл удалён: {FileName}", e.Name);

                // Удаляем из отслеживаемых файлов
                _watchedFiles.TryRemove(e.FullPath, out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка обработки удаления файла: {FileName}", e.Name);
            }
        }

        private async void OnWatcherError(object sender, ErrorEventArgs e)
        {
            _logger.LogError(e.GetException(), "💥 Критическая ошибка FileSystemWatcher");

            await _notificationService.SendErrorNotificationAsync(
                "Ошибка мониторинга файлов",
                "Произошла ошибка в системе мониторинга файлов. Пытаюсь перезапустить...",
                e.GetException()
            );

            // Пытаемся перезапустить мониторинг
            _ = Task.Run(async () => await RestartFileWatcherAsync());
        }

        private async Task RestartFileWatcherAsync()
        {
            try
            {
                Interlocked.Increment(ref _restartAttempts);

                _logger.LogWarning("🔄 Попытка перезапуска FileSystemWatcher (попытка #{RestartAttempts})", _restartAttempts);

                // Останавливаем текущий watcher
                if (_fileWatcher != null)
                {
                    _fileWatcher.EnableRaisingEvents = false;
                    _fileWatcher.Dispose();
                    _fileWatcher = null;
                }

                // Ждём немного перед перезапуском
                await Task.Delay(TimeSpan.FromSeconds(5));

                // Запускаем заново
                await StartFileSystemWatcherAsync();

                _logger.LogInformation("✅ FileSystemWatcher успешно перезапущен");

                await _notificationService.SendSuccessNotificationAsync(
                    "Мониторинг восстановлен",
                    $"FileSystemWatcher успешно перезапущен (попытка #{_restartAttempts})"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Не удалось перезапустить FileSystemWatcher");

                await _notificationService.SendErrorNotificationAsync(
                    "Критическая ошибка мониторинга",
                    "Не удалось восстановить мониторинг файлов",
                    ex
                );
            }
        }

        private async Task ProcessEventQueueAsync()
        {
            _logger.LogDebug("🔄 Запуск обработчика очереди событий");

            while (!_isDisposed)
            {
                try
                {
                    if (_eventQueue.IsEmpty)
                    {
                        await Task.Delay(1000); // Ждём секунду если очередь пуста
                        continue;
                    }

                    if (!await _processingSemaphore.WaitAsync(100))
                    {
                        continue; // Пропускаем если уже идет обработка
                    }

                    _isProcessingEvents = true;

                    try
                    {
                        var processedCount = 0;
                        var maxBatch = 10; // Обрабатываем максимум 10 событий за раз

                        while (processedCount < maxBatch && _eventQueue.TryDequeue(out var fileEvent))
                        {
                            try
                            {
                                await ProcessSingleFileEventAsync(fileEvent);
                                processedCount++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "❌ Ошибка обработки события файла: {FileName}", fileEvent.Name);
                                Interlocked.Increment(ref _eventProcessingErrors);
                            }
                        }

                        if (processedCount > 0)
                        {
                            _logger.LogDebug("📤 Обработано событий файлов: {ProcessedCount}", processedCount);
                        }
                    }
                    finally
                    {
                        _isProcessingEvents = false;
                        _processingSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "💥 Критическая ошибка в обработчике очереди событий");
                    await Task.Delay(5000); // Ждём 5 секунд при критической ошибке
                }
            }

            _logger.LogDebug("⏹️ Обработчик очереди событий остановлен");
        }

        private async Task ProcessSingleFileEventAsync(FileSystemEventArgs fileEvent)
        {
            var filePath = fileEvent.FullPath;
            var fileName = fileEvent.Name ?? Path.GetFileName(filePath);

            _logger.LogDebug("🔄 Обработка события: {EventType} для файла {FileName}", fileEvent.ChangeType, fileName);

            // Ждём немного, чтобы файл был полностью записан
            await Task.Delay(3000);

            // Проверяем, что файл всё ещё существует
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("⚠️ Файл {FileName} был удалён до завершения обработки", fileName);
                return;
            }

            // Проверяем, не обрабатывается ли файл уже
            var watchInfo = _watchedFiles.GetOrAdd(filePath, _ => new FileWatchInfo
            {
                FilePath = filePath,
                FirstSeen = DateTime.Now,
                LastEvent = fileEvent.ChangeType
            });

            if (watchInfo.IsProcessing)
            {
                _logger.LogDebug("⏳ Файл {FileName} уже обрабатывается, пропускаем", fileName);
                return;
            }

            try
            {
                watchInfo.IsProcessing = true;
                watchInfo.LastProcessingAttempt = DateTime.Now;

                // Проверяем безопасность файла
                var isSafe = await _securityService.IsFileSafeAsync(filePath);
                if (!isSafe)
                {
                    _logger.LogWarning("🚫 Файл {FileName} не прошёл проверку безопасности", fileName);

                    await _notificationService.SendWarningNotificationAsync(
                        "Подозрительный файл обнаружен",
                        $"Файл {fileName} не прошёл проверку безопасности и не будет обработан"
                    );

                    return;
                }

                // Обрабатываем файл
                var result = await _fileProcessingService.ProcessFileAsync(filePath);

                watchInfo.LastProcessingResult = result.Success;
                watchInfo.LastError = result.ErrorMessage;

                if (result.Success)
                {
                    _logger.LogInformation("✅ Файл {FileName} успешно обработан через мониторинг", fileName);
                    watchInfo.ProcessedSuccessfully = true;
                }
                else
                {
                    _logger.LogWarning("⚠️ Не удалось обработать файл {FileName} через мониторинг: {ErrorMessage}",
                        fileName, result.ErrorMessage);

                    // Если это не ошибка "уже отправлен", уведомляем
                    if (!string.IsNullOrEmpty(result.ErrorMessage) &&
                        !result.ErrorMessage.Contains("уже был отправлен"))
                    {
                        await _notificationService.SendWarningNotificationAsync(
                            "Ошибка обработки файла",
                            $"Не удалось обработать файл {fileName}: {result.ErrorMessage}"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Критическая ошибка обработки файла {FileName}", fileName);

                watchInfo.LastError = ex.Message;
                watchInfo.LastProcessingResult = false;

                await _notificationService.SendErrorNotificationAsync(
                    "Критическая ошибка обработки файла",
                    $"Произошла критическая ошибка при обработке файла {fileName}",
                    ex
                );
            }
            finally
            {
                watchInfo.IsProcessing = false;
                watchInfo.ProcessingCount++;
            }
        }

        private async Task ProcessRemainingEventsAsync()
        {
            try
            {
                _logger.LogInformation("🔄 Обработка оставшихся событий в очереди...");

                var remainingCount = _eventQueue.Count;
                var processedCount = 0;

                while (_eventQueue.TryDequeue(out var fileEvent) && processedCount < 50) // Лимит на случай большой очереди
                {
                    try
                    {
                        await ProcessSingleFileEventAsync(fileEvent);
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Ошибка обработки оставшегося события: {FileName}", fileEvent.Name);
                    }
                }

                _logger.LogInformation("✅ Обработано оставшихся событий: {ProcessedCount} из {RemainingCount}",
                    processedCount, remainingCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка обработки оставшихся событий");
            }
        }

        private bool IsSupportedFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return _supportedExtensions.Contains(extension);
        }

        private void UpdateFileWatchInfo(string filePath, WatcherChangeTypes changeType)
        {
            _watchedFiles.AddOrUpdate(filePath,
                _ => new FileWatchInfo
                {
                    FilePath = filePath,
                    FirstSeen = DateTime.Now,
                    LastEvent = changeType,
                    EventCount = 1
                },
                (_, existing) =>
                {
                    existing.LastEvent = changeType;
                    existing.LastSeen = DateTime.Now;
                    existing.EventCount++;
                    return existing;
                });
        }

        private async void PeriodicDirectoryScan(object? state)
        {
            if (_isProcessingEvents)
            {
                _logger.LogDebug("⏳ Пропускаю периодическое сканирование - идёт обработка событий");
                return;
            }

            try
            {
                _logger.LogDebug("🔍 Выполняю периодическое сканирование папки");

                var processedCount = await _fileProcessingService.ProcessAllNewFilesAsync();
                _lastSuccessfulScan = DateTime.Now;

                if (processedCount > 0)
                {
                    _logger.LogInformation("📊 Периодическое сканирование: обработано файлов {ProcessedCount}", processedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка периодического сканирования");

                await _notificationService.SendWarningNotificationAsync(
                    "Ошибка периодического сканирования",
                    "Произошла ошибка при периодическом сканировании папки",
                    new List<string>() // Отправляем только админам
                );
            }
        }

        private async void PerformHealthCheck(object? state)
        {
            try
            {
                var issues = new List<string>();

                // Проверяем состояние FileSystemWatcher
                if (_fileWatcher == null || !_fileWatcher.EnableRaisingEvents)
                {
                    issues.Add("FileSystemWatcher не активен");
                }

                // Проверяем доступность папки
                if (!Directory.Exists(_config.ReportsFolder))
                {
                    issues.Add($"Папка мониторинга недоступна: {_config.ReportsFolder}");
                }

                // Проверяем количество ошибок
                if (_eventProcessingErrors > 10)
                {
                    issues.Add($"Много ошибок обработки событий: {_eventProcessingErrors}");
                }

                // Проверяем время последнего успешного сканирования
                var timeSinceLastScan = DateTime.Now - _lastSuccessfulScan;
                if (timeSinceLastScan > TimeSpan.FromHours(2))
                {
                    issues.Add($"Долго не было успешного сканирования: {timeSinceLastScan.TotalHours:F1} часов");
                }

                // Проверяем размер очереди
                var queueSize = _eventQueue.Count;
                if (queueSize > 100)
                {
                    issues.Add($"Большая очередь событий: {queueSize}");
                }

                if (issues.Any())
                {
                    _logger.LogWarning("⚠️ Обнаружены проблемы мониторинга: {Issues}", string.Join("; ", issues));

                    await _notificationService.SendWarningNotificationAsync(
                        "Проблемы мониторинга файлов",
                        $"Обнаружено проблем: {issues.Count}\n\n• " + string.Join("\n• ", issues)
                    );
                }
                else
                {
                    _logger.LogDebug("✅ Проверка здоровья мониторинга: всё в порядке");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка проверки здоровья мониторинга");
            }
        }

        private void CleanupWatchedFiles(object? state)
        {
            try
            {
                var cutoffTime = DateTime.Now.AddHours(-24);
                var filesToRemove = new List<string>();

                foreach (var kvp in _watchedFiles)
                {
                    var watchInfo = kvp.Value;

                    // Удаляем информацию о файлах, которые не видели больше суток
                    if (watchInfo.LastSeen < cutoffTime ||
                        (watchInfo.ProcessedSuccessfully && watchInfo.LastProcessingAttempt < cutoffTime))
                    {
                        filesToRemove.Add(kvp.Key);
                    }
                }

                foreach (var filePath in filesToRemove)
                {
                    _watchedFiles.TryRemove(filePath, out _);
                }

                if (filesToRemove.Count > 0)
                {
                    _logger.LogDebug("🧹 Очищена информация о {Count} старых файлах", filesToRemove.Count);
                }

                // Сбрасываем счётчик ошибок если прошло много времени
                if (_eventProcessingErrors > 0 && DateTime.Now.Hour == 0) // В полночь
                {
                    Interlocked.Exchange(ref _eventProcessingErrors, 0);
                    _logger.LogDebug("🔄 Сброшен счётчик ошибок обработки событий");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка очистки информации о наблюдаемых файлах");
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            try
            {
                _fileWatcher?.Dispose();
                _periodicCheckTimer?.Dispose();
                _healthCheckTimer?.Dispose();
                _cleanupTimer?.Dispose();
                _processingSemaphore?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка освобождения ресурсов FileWatcherService");
            }
        }
    }

    /// <summary>
    /// Информация об отслеживаемом файле
    /// </summary>
    public class FileWatchInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public WatcherChangeTypes LastEvent { get; set; }
        public int EventCount { get; set; }
        public bool IsProcessing { get; set; }
        public DateTime? LastProcessingAttempt { get; set; }
        public bool? LastProcessingResult { get; set; }
        public string? LastError { get; set; }
        public bool ProcessedSuccessfully { get; set; }
        public int ProcessingCount { get; set; }
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
}