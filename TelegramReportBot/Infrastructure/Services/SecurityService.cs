using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using TelegramReportBot.Core.Enum;
using TelegramReportBot.Core.Enums;
using TelegramReportBot.Core.Interface;
using TelegramReportBot.Core.Interfaces;
using TelegramReportBot.Core.Models.Configuration;
using TelegramReportBot.Core.Models.Security;

namespace TelegramReportBot.Infrastructure.Services
{
    /// <summary>
    /// Сервис безопасности для защиты бота и валидации данных
    /// </summary>
    public class SecurityService : ISecurityService
    {
        private readonly BotConfiguration _config;
        private readonly ILogger<SecurityService> _logger;
        private readonly HashSet<string> _adminUsers;
        private readonly HashSet<string> _blockedUsers;
        private readonly List<SecurityEvent> _securityEvents;
        private readonly List<AuditEvent> _auditLog;
        private readonly object _lockObject = new();
        private readonly Timer _cleanupTimer;

        // Настройки безопасности (можно вынести в конфигурацию)
        private static readonly string[] DangerousExtensions = { ".exe", ".bat", ".cmd", ".scr", ".com", ".pif" };
        private static readonly string[] AllowedFileExtensions = { ".pdf" };
        private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB
        private const int MaxFileNameLength = 255;
        private const int MaxAuditLogSize = 10000;

        public SecurityService(
            IOptions<BotConfiguration> config,
            ILogger<SecurityService> logger)
        {
            _config = config.Value;
            _logger = logger;

            _adminUsers = new HashSet<string>(_config.AdminUsers);
            _blockedUsers = new HashSet<string>();
            _securityEvents = new List<SecurityEvent>();
            _auditLog = new List<AuditEvent>();

            // Таймер для периодической очистки логов
            _cleanupTimer = new Timer(CleanupSecurityLogs, null,
                TimeSpan.FromHours(24), TimeSpan.FromHours(24));

            LoadSecurityConfiguration();
            _logger.LogInformation("🔒 Сервис безопасности инициализирован");
        }

        /// <summary>
        /// Проверка прав администратора
        /// </summary>
        public async Task<bool> IsAdminUserAsync(string userId)
        {
            try
            {
                lock (_lockObject)
                {
                    var isAdmin = _adminUsers.Contains(userId) || _adminUsers.Contains($"@{userId}");

                    if (!isAdmin)
                    {
                        LogSecurityEvent(new SecurityEvent
                        {
                            EventType = "UnauthorizedAdminAccess",
                            UserId = userId,
                            Description = "Попытка доступа к админ-функциям неавторизованным пользователем",
                            Level = SecurityLevel.Warning
                        });
                    }

                    return isAdmin;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка проверки прав администратора для пользователя {UserId}", userId);
                return false;
            }
        }

        /// <summary>
        /// Проверка безопасности файла
        /// </summary>
        public async Task<bool> IsFileSafeAsync(string filePath)
        {
            try
            {
                var validationResult = await ValidateFileAsync(filePath);
                return validationResult.IsValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка проверки безопасности файла {FilePath}", filePath);

                LogSecurityEvent(new SecurityEvent
                {
                    EventType = "FileSecurityCheckError",
                    Description = $"Ошибка проверки безопасности файла: {filePath}",
                    Level = SecurityLevel.Critical,
                    Properties = { ["Error"] = ex.Message }
                });

                return false;
            }
        }

        /// <summary>
        /// Валидация файла по различным критериям безопасности
        /// </summary>
        public async Task<ValidationResult> ValidateFileAsync(string filePath)
        {
            var result = new ValidationResult();

            try
            {
                _logger.LogDebug("🔍 Валидация файла: {FilePath}", filePath);

                // Проверка существования файла
                if (!File.Exists(filePath))
                {
                    result.IsValid = false;
                    result.Errors.Add("Файл не найден");
                    return result;
                }

                var fileInfo = new System.IO.FileInfo(filePath);
                var fileName = fileInfo.Name;
                var fileExtension = fileInfo.Extension.ToLowerInvariant();

                // Проверка расширения файла
                if (!AllowedFileExtensions.Contains(fileExtension))
                {
                    result.IsValid = false;
                    result.Errors.Add($"Недопустимое расширение файла: {fileExtension}");
                }

                // Проверка на опасные расширения
                if (DangerousExtensions.Contains(fileExtension))
                {
                    result.IsValid = false;
                    result.Errors.Add($"Опасное расширение файла: {fileExtension}");

                    LogSecurityEvent(new SecurityEvent
                    {
                        EventType = "DangerousFileDetected",
                        Description = $"Обнаружен файл с опасным расширением: {fileName}",
                        Level = SecurityLevel.Critical,
                        Properties = { ["FilePath"] = filePath, ["Extension"] = fileExtension }
                    });
                }

                // Проверка размера файла
                if (fileInfo.Length > MaxFileSizeBytes)
                {
                    result.IsValid = false;
                    result.Errors.Add($"Файл слишком большой: {fileInfo.Length} байт (максимум {MaxFileSizeBytes})");
                }

                if (fileInfo.Length == 0)
                {
                    result.IsValid = false;
                    result.Errors.Add("Файл пустой");
                }

                // Проверка имени файла
                if (fileName.Length > MaxFileNameLength)
                {
                    result.Warnings.Add($"Слишком длинное имя файла: {fileName.Length} символов");
                }

                // Проверка на подозрительные символы в имени файла
                if (ContainsSuspiciousCharacters(fileName))
                {
                    result.Warnings.Add("Имя файла содержит подозрительные символы");
                }

                // Проверка пути на path traversal атаки
                if (ContainsPathTraversal(filePath))
                {
                    result.IsValid = false;
                    result.Errors.Add("Обнаружена попытка path traversal атаки");

                    LogSecurityEvent(new SecurityEvent
                    {
                        EventType = "PathTraversalAttempt",
                        Description = $"Попытка path traversal атаки: {filePath}",
                        Level = SecurityLevel.Critical,
                        Properties = { ["FilePath"] = filePath }
                    });
                }

                // Проверка содержимого файла (базовая)
                await ValidateFileContentAsync(filePath, result);

                // Добавляем метаданные
                result.Metadata["FileSize"] = fileInfo.Length;
                result.Metadata["Extension"] = fileExtension;
                result.Metadata["CreatedAt"] = fileInfo.CreationTime;
                result.Metadata["LastModified"] = fileInfo.LastWriteTime;

                if (result.IsValid)
                {
                    _logger.LogDebug("✅ Файл прошел валидацию: {FileName}", fileName);
                }
                else
                {
                    _logger.LogWarning("❌ Файл не прошел валидацию: {FileName}. Ошибки: {Errors}",
                        fileName, string.Join(", ", result.Errors));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка валидации файла {FilePath}", filePath);
                result.IsValid = false;
                result.Errors.Add($"Ошибка валидации: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Выполнение комплексной проверки безопасности системы
        /// </summary>
        public async Task PerformSecurityCheckAsync()
        {
            try
            {
                _logger.LogInformation("🔍 Выполнение проверки безопасности системы...");

                var issues = new List<string>();

                // Проверка прав доступа к папкам
                await CheckDirectoryPermissionsAsync(issues);

                // Проверка конфигурации
                await CheckConfigurationSecurityAsync(issues);

                // Проверка подозрительных файлов
                await CheckForSuspiciousFilesAsync(issues);

                // Проверка системных ресурсов
                await CheckSystemResourcesAsync(issues);

                // Проверка аудит-лога на подозрительную активность
                await CheckAuditLogForSuspiciousActivityAsync(issues);

                if (issues.Count > 0)
                {
                    _logger.LogWarning("⚠️ Обнаружены проблемы безопасности: {Issues}", string.Join("; ", issues));

                    LogSecurityEvent(new SecurityEvent
                    {
                        EventType = "SecurityIssuesDetected",
                        Description = $"Обнаружено проблем безопасности: {issues.Count}",
                        Level = SecurityLevel.Warning,
                        Properties = { ["Issues"] = issues }
                    });
                }
                else
                {
                    _logger.LogInformation("✅ Проверка безопасности прошла успешно");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка выполнения проверки безопасности");
            }
        }

        /// <summary>
        /// Логирование события безопасности
        /// </summary>
        public void LogSecurityEvent(SecurityEvent securityEvent)
        {
            lock (_lockObject)
            {
                _securityEvents.Add(securityEvent);

                // Ограничиваем размер лога
                if (_securityEvents.Count > 1000)
                {
                    _securityEvents.RemoveRange(0, 100);
                }

                var logLevel = securityEvent.Level switch
                {
                    SecurityLevel.Info => LogLevel.Information,
                    SecurityLevel.Warning => LogLevel.Warning,
                    SecurityLevel.Critical => LogLevel.Error,
                    _ => LogLevel.Information
                };

                _logger.Log(logLevel, "🔒 Событие безопасности: {EventType} - {Description} (Пользователь: {UserId})",
                    securityEvent.EventType, securityEvent.Description, securityEvent.UserId);
            }
        }

        /// <summary>
        /// Получение аудит-лога
        /// </summary>
        public async Task<List<AuditEvent>> GetAuditLogAsync(DateTime? from = null, DateTime? to = null)
        {
            lock (_lockObject)
            {
                var query = _auditLog.AsEnumerable();

                if (from.HasValue)
                    query = query.Where(e => e.Timestamp >= from.Value);

                if (to.HasValue)
                    query = query.Where(e => e.Timestamp <= to.Value);

                return query.OrderByDescending(e => e.Timestamp).ToList();
            }
        }

        /// <summary>
        /// Блокировка подозрительного пользователя
        /// </summary>
        public async Task BlockUserAsync(string userId, string reason)
        {
            lock (_lockObject)
            {
                _blockedUsers.Add(userId);

                LogSecurityEvent(new SecurityEvent
                {
                    EventType = "UserBlocked",
                    UserId = userId,
                    Description = $"Пользователь заблокирован. Причина: {reason}",
                    Level = SecurityLevel.Warning,
                    Properties = { ["Reason"] = reason }
                });

                _logger.LogWarning("🚫 Пользователь {UserId} заблокирован. Причина: {Reason}", userId, reason);
            }
        }

        /// <summary>
        /// Разблокировка пользователя
        /// </summary>
        public async Task UnblockUserAsync(string userId)
        {
            lock (_lockObject)
            {
                if (_blockedUsers.Remove(userId))
                {
                    LogSecurityEvent(new SecurityEvent
                    {
                        EventType = "UserUnblocked",
                        UserId = userId,
                        Description = "Пользователь разблокирован",
                        Level = SecurityLevel.Info
                    });

                    _logger.LogInformation("✅ Пользователь {UserId} разблокирован", userId);
                }
            }
        }

        // Приватные методы

        private void LoadSecurityConfiguration()
        {
            try
            {
                // Здесь можно загрузить дополнительную конфигурацию безопасности
                // Например, из файла security-config.json
                _logger.LogDebug("📋 Конфигурация безопасности загружена");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Не удалось загрузить конфигурацию безопасности");
            }
        }

        private void CleanupSecurityLogs(object? state)
        {
            try
            {
                lock (_lockObject)
                {
                    var cutoffDate = DateTime.Now.AddDays(-30);

                    // Очищаем старые события безопасности
                    _securityEvents.RemoveAll(e => e.Timestamp < cutoffDate);

                    // Очищаем старые записи аудит-лога
                    _auditLog.RemoveAll(e => e.Timestamp < cutoffDate);

                    // Ограничиваем размер аудит-лога
                    if (_auditLog.Count > MaxAuditLogSize)
                    {
                        _auditLog.RemoveRange(0, _auditLog.Count - MaxAuditLogSize);
                    }
                }

                _logger.LogDebug("🧹 Очистка логов безопасности выполнена");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка очистки логов безопасности");
            }
        }

        private bool ContainsSuspiciousCharacters(string fileName)
        {
            // Проверяем на подозрительные символы и паттерны
            var suspiciousPatterns = new[]
            {
                @"[<>:""|?*]", // Недопустимые символы в Windows
                @"\.\.", // Попытки path traversal
                @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", // Зарезервированные имена Windows
                @"^\s+|\s+$", // Пробелы в начале или конце
                @"[^\x20-\x7E\u00A0-\uFFFF]" // Непечатаемые символы
            };

            return suspiciousPatterns.Any(pattern => Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase));
        }

        private bool ContainsPathTraversal(string filePath)
        {
            var normalizedPath = Path.GetFullPath(filePath);
            var allowedBasePath = Path.GetFullPath(_config.ReportsFolder);

            return !normalizedPath.StartsWith(allowedBasePath, StringComparison.OrdinalIgnoreCase);
        }

        private async Task ValidateFileContentAsync(string filePath, ValidationResult result)
        {
            try
            {
                // Простая проверка содержимого файла
                using var stream = File.OpenRead(filePath);
                var buffer = new byte[1024];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                if (bytesRead > 0)
                {
                    // Проверяем PDF заголовок
                    var header = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Min(8, bytesRead));
                    if (!header.StartsWith("%PDF"))
                    {
                        result.Warnings.Add("Файл не содержит корректного PDF заголовка");
                    }

                    // Проверяем на подозрительные бинарные паттерны
                    if (ContainsSuspiciousBinaryPatterns(buffer, bytesRead))
                    {
                        result.Warnings.Add("Обнаружены подозрительные бинарные данные");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Ошибка проверки содержимого файла: {ex.Message}");
            }
        }

        private bool ContainsSuspiciousBinaryPatterns(byte[] buffer, int length)
        {
            // Простая проверка на подозрительные бинарные паттерны
            // Например, исполняемые файлы, скрипты и т.д.

            var suspiciousPatterns = new byte[][]
            {
                new byte[] { 0x4D, 0x5A }, // MZ header (executable)
                new byte[] { 0x50, 0x4B }, // PK header (ZIP/Office documents с макросами)
            };

            for (int i = 0; i < length - 1; i++)
            {
                foreach (var pattern in suspiciousPatterns)
                {
                    if (i + pattern.Length <= length)
                    {
                        bool matches = true;
                        for (int j = 0; j < pattern.Length; j++)
                        {
                            if (buffer[i + j] != pattern[j])
                            {
                                matches = false;
                                break;
                            }
                        }
                        if (matches)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private async Task CheckDirectoryPermissionsAsync(List<string> issues)
        {
            try
            {
                // Проверяем доступ к основным папкам
                var directories = new[]
                {
                    _config.ReportsFolder,
                    "Data",
                    "Logs",
                    "Backups"
                };

                foreach (var dir in directories)
                {
                    if (!Directory.Exists(dir))
                    {
                        try
                        {
                            Directory.CreateDirectory(dir);
                        }
                        catch
                        {
                            issues.Add($"Не удается создать папку: {dir}");
                        }
                    }
                    else
                    {
                        // Проверяем права на запись
                        try
                        {
                            var testFile = Path.Combine(dir, $"test_{Guid.NewGuid():N}.tmp");
                            await File.WriteAllTextAsync(testFile, "test");
                            File.Delete(testFile);
                        }
                        catch
                        {
                            issues.Add($"Нет прав на запись в папку: {dir}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"Ошибка проверки прав доступа: {ex.Message}");
            }
        }

        private async Task CheckConfigurationSecurityAsync(List<string> issues)
        {
            try
            {
                // Проверяем безопасность конфигурации
                if (string.IsNullOrEmpty(_config.Token))
                {
                    issues.Add("Токен бота не настроен");
                }
                else if (_config.Token.Length < 40)
                {
                    issues.Add("Токен бота выглядит подозрительно коротким");
                }

                if (_config.AdminUsers.Count == 0)
                {
                    issues.Add("Не настроены администраторы бота");
                }

                if (string.IsNullOrEmpty(_config.ChatId))
                {
                    issues.Add("ID чата не настроен");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"Ошибка проверки конфигурации: {ex.Message}");
            }
        }

        private async Task CheckForSuspiciousFilesAsync(List<string> issues)
        {
            try
            {
                if (!Directory.Exists(_config.ReportsFolder))
                    return;

                var allFiles = Directory.GetFiles(_config.ReportsFolder, "*", SearchOption.AllDirectories);
                var suspiciousFiles = allFiles.Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return DangerousExtensions.Contains(ext) || !AllowedFileExtensions.Contains(ext);
                }).ToList();

                if (suspiciousFiles.Count > 0)
                {
                    issues.Add($"Обнаружено подозрительных файлов: {suspiciousFiles.Count}");

                    foreach (var file in suspiciousFiles.Take(5)) // Показываем первые 5
                    {
                        issues.Add($"Подозрительный файл: {Path.GetFileName(file)}");
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"Ошибка поиска подозрительных файлов: {ex.Message}");
            }
        }

        private async Task CheckSystemResourcesAsync(List<string> issues)
        {
            try
            {
                // Проверяем доступное место на диске
                if (Directory.Exists(_config.ReportsFolder))
                {
                    var drive = new DriveInfo(Path.GetPathRoot(_config.ReportsFolder)!);
                    var freeSpaceGB = drive.AvailableFreeSpace / (1024L * 1024L * 1024L);

                    if (freeSpaceGB < 1)
                    {
                        issues.Add($"Мало свободного места на диске: {freeSpaceGB} ГБ");
                    }
                }

                // Проверяем использование памяти
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var memoryMB = process.WorkingSet64 / (1024 * 1024);

                if (memoryMB > 500) // Если больше 500 МБ
                {
                    issues.Add($"Высокое использование памяти: {memoryMB} МБ");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"Ошибка проверки системных ресурсов: {ex.Message}");
            }
        }

        private async Task CheckAuditLogForSuspiciousActivityAsync(List<string> issues)
        {
            try
            {
                lock (_lockObject)
                {
                    var recentEvents = _securityEvents
                        .Where(e => e.Timestamp > DateTime.Now.AddHours(-24))
                        .ToList();

                    var criticalEvents = recentEvents.Count(e => e.Level == SecurityLevel.Critical);
                    if (criticalEvents > 5)
                    {
                        issues.Add($"Много критических событий за последние 24 часа: {criticalEvents}");
                    }

                    var failedAccess = recentEvents.Count(e => e.EventType.Contains("Unauthorized"));
                    if (failedAccess > 10)
                    {
                        issues.Add($"Много попыток неавторизованного доступа: {failedAccess}");
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"Ошибка анализа аудит-лога: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }
}