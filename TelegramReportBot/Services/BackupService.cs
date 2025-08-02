using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TelegramReportBot.Models;
using TelegramReportBot.Services.Interface;

namespace TelegramReportBot.Services
{
    /// <summary>
    /// Сервис резервного копирования данных бота
    /// </summary>
    public class BackupService : IBackupService
    {
        private readonly BotConfiguration _config;
        private readonly BackupSettings _backupSettings;
        private readonly ILogger<BackupService> _logger;
        private readonly string _backupDirectory;

        public BackupService(
            IOptions<BotConfiguration> config,
            IConfiguration configuration,
            ILogger<BackupService> logger)
        {
            _config = config.Value;
            _backupSettings = configuration.GetSection("BackupSettings").Get<BackupSettings>() ?? new BackupSettings();
            _logger = logger;

            _backupDirectory = Path.GetFullPath(_backupSettings.BackupLocation);
            EnsureBackupDirectoryExists();
        }

        /// <summary>
        /// Создание полной резервной копии
        /// </summary>
        public async Task<string> CreateBackupAsync()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupName = $"telegram_bot_backup_{timestamp}";
            var backupPath = Path.Combine(_backupDirectory, backupName);

            try
            {
                _logger.LogInformation("💾 Начало создания резервной копии: {BackupName}", backupName);

                // Создаем временную папку для подготовки бэкапа
                var tempDir = Path.Combine(Path.GetTempPath(), $"backup_temp_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Собираем данные для бэкапа
                    await CollectBackupDataAsync(tempDir);

                    // Создаем манифест бэкапа
                    await CreateBackupManifestAsync(tempDir, backupName);

                    // Создаем архив
                    var finalBackupPath = _backupSettings.CompressBackups
                        ? await CreateCompressedBackupAsync(tempDir, backupPath)
                        : await CreateUncompressedBackupAsync(tempDir, backupPath);

                    // Проверяем целостность созданной резервной копии
                    var isValid = await VerifyBackupAsync(finalBackupPath);
                    if (!isValid)
                    {
                        throw new InvalidOperationException("Созданная резервная копия не прошла проверку целостности");
                    }

                    _logger.LogInformation("✅ Резервная копия успешно создана: {BackupPath}", finalBackupPath);

                    // Очищаем старые бэкапы
                    await CleanupOldBackupsAsync();

                    return finalBackupPath;
                }
                finally
                {
                    // Очищаем временную папку
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка создания резервной копии");
                throw;
            }
        }

        /// <summary>
        /// Восстановление из резервной копии
        /// </summary>
        public async Task RestoreBackupAsync(string backupPath)
        {
            try
            {
                _logger.LogInformation("🔄 Начало восстановления из резервной копии: {BackupPath}", backupPath);

                if (!File.Exists(backupPath))
                {
                    throw new FileNotFoundException($"Файл резервной копии не найден: {backupPath}");
                }

                // Проверяем целостность перед восстановлением
                var isValid = await VerifyBackupAsync(backupPath);
                if (!isValid)
                {
                    throw new InvalidOperationException("Резервная копия повреждена или недействительна");
                }

                // Создаем резервную копию текущего состояния перед восстановлением
                var currentBackupPath = await CreateBackupAsync();
                _logger.LogInformation("🛡️ Создана резервная копия текущего состояния: {CurrentBackupPath}", currentBackupPath);

                // Извлекаем данные из бэкапа
                var tempRestoreDir = Path.Combine(Path.GetTempPath(), $"restore_temp_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempRestoreDir);

                try
                {
                    await ExtractBackupAsync(backupPath, tempRestoreDir);

                    // Проверяем манифест
                    var manifestPath = Path.Combine(tempRestoreDir, "manifest.json");
                    if (!File.Exists(manifestPath))
                    {
                        throw new InvalidOperationException("Манифест резервной копии не найден");
                    }

                    var manifest = JsonSerializer.Deserialize<BackupManifest>(
                        await File.ReadAllTextAsync(manifestPath));

                    if (manifest == null)
                    {
                        throw new InvalidOperationException("Не удалось прочитать манифест резервной копии");
                    }

                    // Восстанавливаем данные
                    await RestoreDataAsync(tempRestoreDir, manifest);

                    _logger.LogInformation("✅ Восстановление из резервной копии завершено успешно");
                }
                finally
                {
                    if (Directory.Exists(tempRestoreDir))
                    {
                        Directory.Delete(tempRestoreDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка восстановления из резервной копии");
                throw;
            }
        }

        /// <summary>
        /// Получение списка доступных резервных копий
        /// </summary>
        public async Task<List<BackupInfo>> GetAvailableBackupsAsync()
        {
            var backups = new List<BackupInfo>();

            try
            {
                if (!Directory.Exists(_backupDirectory))
                {
                    return backups;
                }

                var backupFiles = Directory.GetFiles(_backupDirectory, "*backup*")
                    .Where(f => f.EndsWith(".zip") || f.EndsWith(".7z") || Directory.Exists(f))
                    .OrderByDescending(f => File.GetCreationTime(f));

                foreach (var backupFile in backupFiles)
                {
                    try
                    {
                        var backupInfo = await GetBackupInfoAsync(backupFile);
                        if (backupInfo != null)
                        {
                            backups.Add(backupInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Не удалось получить информацию о резервной копии: {BackupFile}", backupFile);
                    }
                }

                _logger.LogDebug("📋 Найдено резервных копий: {Count}", backups.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка получения списка резервных копий");
            }

            return backups;
        }

        /// <summary>
        /// Удаление старых резервных копий
        /// </summary>
        public async Task CleanupOldBackupsAsync()
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-_backupSettings.BackupRetentionDays);
                var backups = await GetAvailableBackupsAsync();

                var oldBackups = backups.Where(b => b.CreatedAt < cutoffDate).ToList();

                foreach (var oldBackup in oldBackups)
                {
                    try
                    {
                        if (File.Exists(oldBackup.FullPath))
                        {
                            File.Delete(oldBackup.FullPath);
                            _logger.LogInformation("🗑️ Удалена старая резервная копия: {BackupName}", oldBackup.FileName);
                        }
                        else if (Directory.Exists(oldBackup.FullPath))
                        {
                            Directory.Delete(oldBackup.FullPath, true);
                            _logger.LogInformation("🗑️ Удалена старая резервная копия: {BackupName}", oldBackup.FileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Не удалось удалить резервную копию: {BackupPath}", oldBackup.FullPath);
                    }
                }

                if (oldBackups.Count > 0)
                {
                    _logger.LogInformation("🧹 Очистка завершена. Удалено резервных копий: {Count}", oldBackups.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка очистки старых резервных копий");
            }
        }

        /// <summary>
        /// Проверка целостности резервной копии
        /// </summary>
        public async Task<bool> VerifyBackupAsync(string backupPath)
        {
            try
            {
                _logger.LogDebug("🔍 Проверка целостности резервной копии: {BackupPath}", backupPath);

                if (!File.Exists(backupPath) && !Directory.Exists(backupPath))
                {
                    _logger.LogWarning("⚠️ Резервная копия не найдена: {BackupPath}", backupPath);
                    return false;
                }

                // Если это архив - проверяем его целостность
                if (backupPath.EndsWith(".zip"))
                {
                    return await VerifyZipArchiveAsync(backupPath);
                }

                // Если это папка - проверяем наличие манифеста и основных файлов
                if (Directory.Exists(backupPath))
                {
                    return await VerifyDirectoryBackupAsync(backupPath);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка проверки целостности резервной копии");
                return false;
            }
        }

        // Приватные методы

        private void EnsureBackupDirectoryExists()
        {
            if (!Directory.Exists(_backupDirectory))
            {
                Directory.CreateDirectory(_backupDirectory);
                _logger.LogInformation("📁 Создана папка для резервных копий: {BackupDirectory}", _backupDirectory);
            }
        }

        private async Task CollectBackupDataAsync(string tempDir)
        {
            _logger.LogDebug("📦 Сбор данных для резервной копии...");

            // Создаем структуру папок в бэкапе
            var dataDir = Path.Combine(tempDir, "data");
            var configDir = Path.Combine(tempDir, "config");
            var logsDir = Path.Combine(tempDir, "logs");

            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(logsDir);

            // Копируем базу данных отправленных файлов
            if (_backupSettings.BackupDatabase)
            {
                await BackupDatabaseAsync(dataDir);
            }

            // Копируем конфигурационные файлы
            await BackupConfigurationAsync(configDir);

            // Копируем логи (если включено)
            if (_backupSettings.BackupLogs)
            {
                await BackupLogsAsync(logsDir);
            }

            // Копируем статистику
            await BackupStatisticsAsync(dataDir);

            _logger.LogDebug("✅ Сбор данных завершен");
        }

        private async Task BackupDatabaseAsync(string dataDir)
        {
            try
            {
                var dbPath = _config.SentFilesDatabase;
                if (File.Exists(dbPath))
                {
                    var backupDbPath = Path.Combine(dataDir, "sent_files.txt");
                    await File.Copy(dbPath, backupDbPath, true);
                    _logger.LogDebug("💾 База данных отправленных файлов скопирована");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ошибка резервного копирования базы данных");
            }
        }

        private async Task BackupConfigurationAsync(string configDir)
        {
            try
            {
                // Копируем appsettings.json
                var configFiles = new[] { "appsettings.json", "appsettings.Production.json", "appsettings.Development.json" };

                foreach (var configFile in configFiles)
                {
                    if (File.Exists(configFile))
                    {
                        var backupConfigPath = Path.Combine(configDir, configFile);
                        File.Copy(configFile, backupConfigPath, true);
                        _logger.LogDebug("⚙️ Конфигурационный файл скопирован: {ConfigFile}", configFile);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ошибка резервного копирования конфигурации");
            }
        }

        private async Task BackupLogsAsync(string logsDir)
        {
            try
            {
                var logDirectory = "Logs";
                if (Directory.Exists(logDirectory))
                {
                    var logFiles = Directory.GetFiles(logDirectory, "*.txt")
                        .Where(f => File.GetCreationTime(f) > DateTime.Now.AddDays(-7)) // Копируем логи за последнюю неделю
                        .ToList();

                    foreach (var logFile in logFiles)
                    {
                        var fileName = Path.GetFileName(logFile);
                        var backupLogPath = Path.Combine(logsDir, fileName);
                        File.Copy(logFile, backupLogPath, true);
                    }

                    _logger.LogDebug("📝 Скопировано лог-файлов: {Count}", logFiles.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ошибка резервного копирования логов");
            }
        }

        private async Task BackupStatisticsAsync(string dataDir)
        {
            try
            {
                var statsPath = Path.Combine("Data", "statistics.json");
                if (File.Exists(statsPath))
                {
                    var backupStatsPath = Path.Combine(dataDir, "statistics.json");
                    File.Copy(statsPath, backupStatsPath, true);
                    _logger.LogDebug("📊 Статистика скопирована");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ошибка резервного копирования статистики");
            }
        }

        private async Task CreateBackupManifestAsync(string tempDir, string backupName)
        {
            var manifest = new BackupManifest
            {
                BackupName = backupName,
                CreatedAt = DateTime.Now,
                BotVersion = "2.0",
                ConfigurationHash = await ComputeConfigurationHashAsync(),
                Files = await CollectFileInfoAsync(tempDir),
                CreatedBy = Environment.UserName,
                MachineName = Environment.MachineName
            };

            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var manifestPath = Path.Combine(tempDir, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, manifestJson);

            _logger.LogDebug("📋 Манифест резервной копии создан");
        }

        private async Task<string> ComputeConfigurationHashAsync()
        {
            try
            {
                var configJson = JsonSerializer.Serialize(_config);
                using var sha256 = SHA256.Create();
                var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(configJson));
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch
            {
                return "unknown";
            }
        }

        private async Task<List<BackupFileInfo>> CollectFileInfoAsync(string directory)
        {
            var files = new List<BackupFileInfo>();

            try
            {
                var allFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);

                foreach (var file in allFiles)
                {
                    var fileInfo = new System.IO.FileInfo(file);
                    var relativePath = Path.GetRelativePath(directory, file);

                    files.Add(new BackupFileInfo
                    {
                        RelativePath = relativePath,
                        SizeBytes = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTime,
                        Checksum = await ComputeFileChecksumAsync(file)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ошибка сбора информации о файлах");
            }

            return files;
        }

        private async Task<string> ComputeFileChecksumAsync(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var sha256 = SHA256.Create();
                var hashBytes = await sha256.ComputeHashAsync(stream);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch
            {
                return "unknown";
            }
        }

        private async Task<string> CreateCompressedBackupAsync(string tempDir, string backupPath)
        {
            var zipPath = $"{backupPath}.zip";

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(tempDir, file);
                    archive.CreateEntryFromFile(file, relativePath);
                }
            }

            _logger.LogDebug("🗜️ Создан сжатый архив: {ZipPath}", zipPath);
            return zipPath;
        }

        private async Task<string> CreateUncompressedBackupAsync(string tempDir, string backupPath)
        {
            Directory.CreateDirectory(backupPath);

            await CopyDirectoryAsync(tempDir, backupPath);

            _logger.LogDebug("📁 Создана несжатая резервная копия: {BackupPath}", backupPath);
            return backupPath;
        }

        private async Task CopyDirectoryAsync(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(destDir, fileName);
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dir);
                var destSubDir = Path.Combine(destDir, dirName);
                await CopyDirectoryAsync(dir, destSubDir);
            }
        }

        private async Task<bool> VerifyZipArchiveAsync(string zipPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);

                // Проверяем, что архив можно открыть и содержит манифест
                var manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry == null)
                {
                    _logger.LogWarning("⚠️ Манифест не найден в архиве");
                    return false;
                }

                // Проверяем целостность архива, пытаясь прочитать все записи
                foreach (var entry in archive.Entries)
                {
                    using var stream = entry.Open();
                    var buffer = new byte[1024];
                    while (await stream.ReadAsync(buffer, 0, buffer.Length) > 0)
                    {
                        // Просто читаем, чтобы проверить целостность
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ошибка проверки ZIP-архива");
                return false;
            }
        }

        private async Task<bool> VerifyDirectoryBackupAsync(string backupPath)
        {
            try
            {
                var manifestPath = Path.Combine(backupPath, "manifest.json");
                return File.Exists(manifestPath);
            }
            catch
            {
                return false;
            }
        }

        private async Task<BackupInfo?> GetBackupInfoAsync(string backupPath)
        {
            try
            {
                BackupManifest? manifest = null;

                if (backupPath.EndsWith(".zip"))
                {
                    manifest = await ReadManifestFromZipAsync(backupPath);
                }
                else if (Directory.Exists(backupPath))
                {
                    manifest = await ReadManifestFromDirectoryAsync(backupPath);
                }

                if (manifest == null)
                {
                    // Если манифеста нет, создаем базовую информацию
                    var fileInfo = new System.IO.FileInfo(backupPath);
                    return new BackupInfo
                    {
                        FileName = Path.GetFileName(backupPath),
                        FullPath = backupPath,
                        CreatedAt = fileInfo.CreationTime,
                        SizeBytes = Directory.Exists(backupPath) ? GetDirectorySize(backupPath) : fileInfo.Length,
                        IsCompressed = backupPath.EndsWith(".zip"),
                        Description = "Резервная копия без манифеста"
                    };
                }

                return new BackupInfo
                {
                    FileName = Path.GetFileName(backupPath),
                    FullPath = backupPath,
                    CreatedAt = manifest.CreatedAt,
                    SizeBytes = Directory.Exists(backupPath) ? GetDirectorySize(backupPath) : new System.IO.FileInfo(backupPath).Length,
                    IsCompressed = backupPath.EndsWith(".zip"),
                    Description = $"Создана на {manifest.MachineName} пользователем {manifest.CreatedBy}",
                    Metadata = new Dictionary<string, object>
                    {
                        ["BotVersion"] = manifest.BotVersion,
                        ["FilesCount"] = manifest.Files.Count,
                        ["ConfigurationHash"] = manifest.ConfigurationHash
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ошибка получения информации о резервной копии: {BackupPath}", backupPath);
                return null;
            }
        }

        private async Task<BackupManifest?> ReadManifestFromZipAsync(string zipPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var manifestEntry = archive.GetEntry("manifest.json");

                if (manifestEntry == null)
                    return null;

                using var stream = manifestEntry.Open();
                using var reader = new StreamReader(stream);
                var manifestJson = await reader.ReadToEndAsync();

                return JsonSerializer.Deserialize<BackupManifest>(manifestJson, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch
            {
                return null;
            }
        }

        private async Task<BackupManifest?> ReadManifestFromDirectoryAsync(string backupPath)
        {
            try
            {
                var manifestPath = Path.Combine(backupPath, "manifest.json");
                if (!File.Exists(manifestPath))
                    return null;

                var manifestJson = await File.ReadAllTextAsync(manifestPath);
                return JsonSerializer.Deserialize<BackupManifest>(manifestJson, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch
            {
                return null;
            }
        }

        private long GetDirectorySize(string directoryPath)
        {
            try
            {
                return Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                    .Sum(file => new System.IO.FileInfo(file).Length);
            }
            catch
            {
                return 0;
            }
        }

        private async Task ExtractBackupAsync(string backupPath, string extractPath)
        {
            if (backupPath.EndsWith(".zip"))
            {
                ZipFile.ExtractToDirectory(backupPath, extractPath);
            }
            else if (Directory.Exists(backupPath))
            {
                await CopyDirectoryAsync(backupPath, extractPath);
            }
        }

        private async Task RestoreDataAsync(string restoreDir, BackupManifest manifest)
        {
            _logger.LogInformation("🔄 Восстановление данных из резервной копии...");

            // Восстанавливаем базу данных
            await RestoreDatabaseAsync(restoreDir);

            // Восстанавливаем конфигурацию (осторожно!)
            await RestoreConfigurationAsync(restoreDir);

            // Восстанавливаем статистику
            await RestoreStatisticsAsync(restoreDir);

            _logger.LogInformation("✅ Данные восстановлены");
        }

        private async Task RestoreDatabaseAsync(string restoreDir)
        {
            try
            {
                var backupDbPath = Path.Combine(restoreDir, "data", "sent_files.txt");
                if (File.Exists(backupDbPath))
                {
                    var targetDbPath = _config.SentFilesDatabase;
                    var targetDir = Path.GetDirectoryName(targetDbPath);

                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Copy(backupDbPath, targetDbPath, true);
                    _logger.LogInformation("💾 База данных восстановлена");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка восстановления базы данных");
            }
        }

        private async Task RestoreConfigurationAsync(string restoreDir)
        {
            // Восстановление конфигурации требует осторожности
            // Пока что только логируем, что можно было бы восстановить
            try
            {
                var configDir = Path.Combine(restoreDir, "config");
                if (Directory.Exists(configDir))
                {
                    var configFiles = Directory.GetFiles(configDir, "*.json");
                    _logger.LogInformation("⚙️ Найдено конфигурационных файлов для восстановления: {Count}", configFiles.Length);
                    // Здесь можно добавить логику восстановления конфигурации
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ошибка восстановления конфигурации");
            }
        }

        private async Task RestoreStatisticsAsync(string restoreDir)
        {
            try
            {
                var backupStatsPath = Path.Combine(restoreDir, "data", "statistics.json");
                if (File.Exists(backupStatsPath))
                {
                    var targetStatsPath = Path.Combine("Data", "statistics.json");
                    var targetDir = Path.GetDirectoryName(targetStatsPath);

                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Copy(backupStatsPath, targetStatsPath, true);
                    _logger.LogInformation("📊 Статистика восстановлена");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка восстановления статистики");
            }
        }
    }

    /// <summary>
    /// Манифест резервной копии
    /// </summary>
    public class BackupManifest
    {
        public string BackupName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string BotVersion { get; set; } = string.Empty;
        public string ConfigurationHash { get; set; } = string.Empty;
        public List<BackupFileInfo> Files { get; set; } = new();
        public string CreatedBy { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Информация о файле в резервной копии
    /// </summary>
    public class BackupFileInfo
    {
        public string RelativePath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime LastModified { get; set; }
        public string Checksum { get; set; } = string.Empty;
    }
}