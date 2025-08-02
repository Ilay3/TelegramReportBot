using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using TelegramReportBot.Models;
using TelegramReportBot.Models.Enum;
using TelegramReportBot.Services.Interface;

namespace TelegramReportBot.Services
{
    /// <summary>
    /// Сервис динамического управления конфигурацией бота
    /// </summary>
    public class ConfigurationService : IConfigurationService
    {
        private readonly IConfiguration _configuration;
        private readonly IOptionsMonitor<BotConfiguration> _botConfigMonitor;
        private readonly ILogger<ConfigurationService> _logger;
        private readonly INotificationService _notificationService;

        private BotConfiguration _currentConfig;
        private readonly object _configLock = new();
        private readonly string _configFilePath;
        private readonly FileSystemWatcher _configWatcher;
        private readonly Dictionary<string, object> _runtimeSettings;

        public ConfigurationService(
            IConfiguration configuration,
            IOptionsMonitor<BotConfiguration> botConfigMonitor,
            ILogger<ConfigurationService> logger,
            INotificationService notificationService)
        {
            _configuration = configuration;
            _botConfigMonitor = botConfigMonitor;
            _logger = logger;
            _notificationService = notificationService;
            _currentConfig = _botConfigMonitor.CurrentValue;
            _runtimeSettings = new Dictionary<string, object>();

            _configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

            // Настраиваем мониторинг изменений конфигурационного файла
            _configWatcher = new FileSystemWatcher(Directory.GetCurrentDirectory(), "appsettings*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _configWatcher.Changed += OnConfigFileChanged;

            // Подписываемся на изменения конфигурации
            _botConfigMonitor.OnChange(OnConfigurationChanged);

            _logger.LogInformation("⚙️ Сервис конфигурации инициализирован");
        }

        /// <summary>
        /// Получение текущей конфигурации
        /// </summary>
        public async Task<BotConfiguration> GetConfigurationAsync()
        {
            lock (_configLock)
            {
                // Возвращаем копию текущей конфигурации
                return JsonSerializer.Deserialize<BotConfiguration>(
                    JsonSerializer.Serialize(_currentConfig))!;
            }
        }

        /// <summary>
        /// Обновление конфигурации
        /// </summary>
        public async Task UpdateConfigurationAsync(BotConfiguration configuration)
        {
            try
            {
                _logger.LogInformation("🔄 Обновление конфигурации...");

                // Валидируем новую конфигурацию
                var validationResult = await ValidateConfigurationAsync(configuration);
                if (!validationResult.IsValid)
                {
                    var errors = string.Join(", ", validationResult.Errors);
                    throw new ArgumentException($"Конфигурация не прошла валидацию: {errors}");
                }

                // Создаём резервную копию текущей конфигурации
                await CreateConfigBackupAsync();

                // Читаем текущий файл конфигурации
                var currentConfigJson = await File.ReadAllTextAsync(_configFilePath);
                var currentConfigDocument = JsonDocument.Parse(currentConfigJson);
                var currentRoot = currentConfigDocument.RootElement.Clone();

                // Создаём новый JSON с обновлённой секцией BotConfiguration
                var newConfigJson = UpdateBotConfigurationInJson(currentRoot, configuration);

                // Записываем обновлённую конфигурацию
                await File.WriteAllTextAsync(_configFilePath, newConfigJson);

                lock (_configLock)
                {
                    _currentConfig = configuration;
                }

                _logger.LogInformation("✅ Конфигурация успешно обновлена");

                await _notificationService.SendSuccessNotificationAsync(
                    "Конфигурация обновлена",
                    "Конфигурация бота успешно обновлена и применена"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка обновления конфигурации");

                await _notificationService.SendErrorNotificationAsync(
                    "Ошибка обновления конфигурации",
                    "Не удалось обновить конфигурацию бота",
                    ex
                );

                throw;
            }
        }

        /// <summary>
        /// Перезагрузка конфигурации из файла
        /// </summary>
        public async Task ReloadConfigurationAsync()
        {
            try
            {
                _logger.LogInformation("🔄 Перезагрузка конфигурации из файла...");

                // Принудительно перезагружаем конфигурацию
                if (_configuration is IConfigurationRoot configRoot)
                {
                    configRoot.Reload();
                }

                // Получаем обновлённую конфигурацию
                var newConfig = _botConfigMonitor.CurrentValue;

                // Валидируем перезагруженную конфигурацию
                var validationResult = await ValidateConfigurationAsync(newConfig);
                if (!validationResult.IsValid)
                {
                    var errors = string.Join(", ", validationResult.Errors);
                    _logger.LogError("❌ Перезагруженная конфигурация не прошла валидацию: {Errors}", errors);

                    await _notificationService.SendErrorNotificationAsync(
                        "Ошибка конфигурации",
                        $"Перезагруженная конфигурация содержит ошибки: {errors}"
                    );

                    return;
                }

                lock (_configLock)
                {
                    _currentConfig = newConfig;
                }

                _logger.LogInformation("✅ Конфигурация успешно перезагружена");

                await _notificationService.SendSuccessNotificationAsync(
                    "Конфигурация перезагружена",
                    "Конфигурация успешно перезагружена из файла"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка перезагрузки конфигурации");

                await _notificationService.SendErrorNotificationAsync(
                    "Ошибка перезагрузки конфигурации",
                    "Не удалось перезагрузить конфигурацию из файла",
                    ex
                );
            }
        }

        /// <summary>
        /// Валидация конфигурации
        /// </summary>
        public async Task<ValidationResult> ValidateConfigurationAsync(BotConfiguration configuration)
        {
            var result = new ValidationResult();

            try
            {
                _logger.LogDebug("🔍 Валидация конфигурации...");

                // Проверка токена бота
                if (string.IsNullOrWhiteSpace(configuration.Token))
                {
                    result.Errors.Add("Токен бота не может быть пустым");
                }
                else if (configuration.Token.Length < 40)
                {
                    result.Warnings.Add("Токен бота выглядит подозрительно коротким");
                }

                // Проверка ID чата
                if (string.IsNullOrWhiteSpace(configuration.ChatId))
                {
                    result.Errors.Add("ID чата не может быть пустым");
                }
                else if (!configuration.ChatId.StartsWith("-"))
                {
                    result.Warnings.Add("ID чата группы обычно начинается с '-'");
                }

                // Проверка топиков
                var topicIds = new[] { configuration.TopicIds.UserErrors, configuration.TopicIds.ServerErrors, configuration.TopicIds.Warnings };
                if (topicIds.Any(id => id <= 0))
                {
                    result.Errors.Add("ID топиков должны быть положительными числами");
                }

                if (topicIds.Distinct().Count() != topicIds.Length)
                {
                    result.Errors.Add("ID топиков должны быть уникальными");
                }

                // Проверка папки отчётов
                if (string.IsNullOrWhiteSpace(configuration.ReportsFolder))
                {
                    result.Errors.Add("Папка отчётов не может быть пустой");
                }
                else
                {
                    try
                    {
                        var fullPath = Path.GetFullPath(configuration.ReportsFolder);
                        result.Metadata["ReportsFolder_FullPath"] = fullPath;

                        // Проверяем возможность создания папки
                        if (!Directory.Exists(fullPath))
                        {
                            try
                            {
                                Directory.CreateDirectory(fullPath);
                                Directory.Delete(fullPath); // Удаляем тестовую папку
                                result.Warnings.Add("Папка отчётов будет создана при запуске");
                            }
                            catch
                            {
                                result.Errors.Add($"Невозможно создать папку отчётов: {fullPath}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Некорректный путь папки отчётов: {ex.Message}");
                    }
                }

                // Проверка файла базы данных
                if (string.IsNullOrWhiteSpace(configuration.SentFilesDatabase))
                {
                    result.Errors.Add("Путь к файлу базы данных не может быть пустым");
                }
                else
                {
                    try
                    {
                        var dbDirectory = Path.GetDirectoryName(Path.GetFullPath(configuration.SentFilesDatabase));
                        if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
                        {
                            result.Warnings.Add("Папка для файла базы данных будет создана автоматически");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Некорректный путь к файлу базы данных: {ex.Message}");
                    }
                }

                // Проверка администраторов
                if (!configuration.AdminUsers.Any())
                {
                    result.Warnings.Add("Не указаны администраторы бота");
                }

                // Проверка настроек rate limiting
                if (configuration.RateLimiting != null)
                {
                    if (configuration.RateLimiting.MaxFilesPerMinute <= 0)
                    {
                        result.Errors.Add("Максимум файлов в минуту должен быть больше 0");
                    }

                    if (configuration.RateLimiting.MaxFilesPerHour <= 0)
                    {
                        result.Errors.Add("Максимум файлов в час должен быть больше 0");
                    }

                    if (configuration.RateLimiting.CooldownBetweenUploads < 0)
                    {
                        result.Errors.Add("Задержка между загрузками не может быть отрицательной");
                    }
                }

                // Добавляем метаданные
                result.Metadata["ValidationTime"] = DateTime.Now;
                result.Metadata["ConfigurationSize"] = JsonSerializer.Serialize(configuration).Length;

                result.IsValid = !result.Errors.Any();

                if (result.IsValid)
                {
                    _logger.LogDebug("✅ Конфигурация прошла валидацию");
                }
                else
                {
                    _logger.LogWarning("❌ Конфигурация не прошла валидацию: {Errors}", string.Join(", ", result.Errors));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка валидации конфигурации");
                result.IsValid = false;
                result.Errors.Add($"Ошибка валидации: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Экспорт конфигурации
        /// </summary>
        public async Task<string> ExportConfigurationAsync()
        {
            try
            {
                var config = await GetConfigurationAsync();

                // Маскируем чувствительные данные для экспорта
                var exportConfig = new
                {
                    BotConfiguration = new
                    {
                        Token = MaskSensitiveData(config.Token),
                        ChatId = config.ChatId,
                        TopicIds = config.TopicIds,
                        ReportsFolder = config.ReportsFolder,
                        SentFilesDatabase = config.SentFilesDatabase,
                        AdminUsers = config.AdminUsers?.Select(u => MaskSensitiveData(u)).ToList(),
                        NotificationSettings = config.NotificationSettings,
                        RateLimiting = config.RateLimiting
                    },
                    ExportInfo = new
                    {
                        ExportedAt = DateTime.Now,
                        ExportedBy = "ConfigurationService",
                        Version = "2.0"
                    }
                };

                var json = JsonSerializer.Serialize(exportConfig, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                _logger.LogInformation("📤 Конфигурация экспортирована");
                return json;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка экспорта конфигурации");
                throw;
            }
        }

        /// <summary>
        /// Импорт конфигурации
        /// </summary>
        public async Task ImportConfigurationAsync(string configData)
        {
            try
            {
                _logger.LogInformation("📥 Импорт конфигурации...");

                // Парсим JSON
                var importData = JsonSerializer.Deserialize<JsonElement>(configData);

                if (!importData.TryGetProperty("botConfiguration", out var botConfigElement))
                {
                    throw new ArgumentException("Файл не содержит конфигурацию бота");
                }

                // Десериализуем конфигурацию бота
                var botConfig = JsonSerializer.Deserialize<BotConfiguration>(
                    botConfigElement.GetRawText(),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
                );

                if (botConfig == null)
                {
                    throw new ArgumentException("Не удалось десериализовать конфигурацию бота");
                }

                // Предупреждаем о чувствительных данных
                if (botConfig.Token.Contains("***"))
                {
                    throw new ArgumentException("Импортируемая конфигурация содержит замаскированные данные. Требуется полная конфигурация.");
                }

                // Обновляем конфигурацию
                await UpdateConfigurationAsync(botConfig);

                _logger.LogInformation("✅ Конфигурация успешно импортирована");

                await _notificationService.SendSuccessNotificationAsync(
                    "Конфигурация импортирована",
                    "Конфигурация успешно импортирована и применена"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка импорта конфигурации");

                await _notificationService.SendErrorNotificationAsync(
                    "Ошибка импорта конфигурации",
                    "Не удалось импортировать конфигурацию",
                    ex
                );

                throw;
            }
        }

        /// <summary>
        /// Получение значения конфигурации по ключу
        /// </summary>
        public T GetConfigValue<T>(string key, T defaultValue = default)
        {
            try
            {
                // Сначала проверяем runtime настройки
                lock (_configLock)
                {
                    if (_runtimeSettings.TryGetValue(key, out var runtimeValue) && runtimeValue is T typedValue)
                    {
                        return typedValue;
                    }
                }

                // Затем проверяем основную конфигурацию
                var configValue = _configuration.GetValue<T>(key, defaultValue);
                return configValue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ошибка получения значения конфигурации по ключу {Key}", key);
                return defaultValue;
            }
        }

        /// <summary>
        /// Установка значения конфигурации в runtime
        /// </summary>
        public async Task SetConfigValueAsync<T>(string key, T value)
        {
            try
            {
                lock (_configLock)
                {
                    _runtimeSettings[key] = value!;
                }

                _logger.LogInformation("⚙️ Установлено runtime значение конфигурации: {Key} = {Value}", key, value);

                await _notificationService.SendSystemNotificationAsync(
                    "Runtime настройка изменена",
                    $"Изменена настройка {key} = {value}",
                    NotificationPriority.Low
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка установки значения конфигурации: {Key}", key);
                throw;
            }
        }

        /// <summary>
        /// Получение информации о конфигурации
        /// </summary>
        public async Task<ConfigurationInfo> GetConfigurationInfoAsync()
        {
            var info = new ConfigurationInfo();

            try
            {
                var configFileInfo = new System.IO.FileInfo(_configFilePath);

                info.ConfigFilePath = _configFilePath;
                info.ConfigFileExists = configFileInfo.Exists;
                info.ConfigFileSize = configFileInfo.Exists ? configFileInfo.Length : 0;
                info.ConfigFileLastModified = configFileInfo.Exists ? configFileInfo.LastWriteTime : null;

                var config = await GetConfigurationAsync();
                var validationResult = await ValidateConfigurationAsync(config);

                info.IsValid = validationResult.IsValid;
                info.ValidationErrors = validationResult.Errors;
                info.ValidationWarnings = validationResult.Warnings;

                info.RuntimeSettingsCount = _runtimeSettings.Count;
                info.AdminUsersCount = config.AdminUsers?.Count ?? 0;

                info.LastReloadTime = DateTime.Now; // Приблизительно
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка получения информации о конфигурации");
                info.ValidationErrors = new List<string> { ex.Message };
            }

            return info;
        }

        // Приватные методы

        private async void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                _logger.LogInformation("📄 Обнаружено изменение файла конфигурации: {FileName}", e.Name);

                // Ждём немного, чтобы файл был полностью записан
                await Task.Delay(2000);

                // Перезагружаем конфигурацию
                await ReloadConfigurationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка обработки изменения файла конфигурации");
            }
        }

        private void OnConfigurationChanged(BotConfiguration newConfig, string? name)
        {
            try
            {
                _logger.LogInformation("🔄 Обнаружено изменение конфигурации");

                lock (_configLock)
                {
                    _currentConfig = newConfig;
                }

                // Асинхронно уведомляем об изменении
                _ = Task.Run(async () =>
                {
                    await _notificationService.SendSystemNotificationAsync(
                        "Конфигурация изменена",
                        "Обнаружены изменения в конфигурации бота",
                        NotificationPriority.Normal
                    );
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка обработки изменения конфигурации");
            }
        }

        private async Task CreateConfigBackupAsync()
        {
            try
            {
                var backupPath = $"{_configFilePath}.backup.{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Copy(_configFilePath, backupPath);

                _logger.LogInformation("💾 Создана резервная копия конфигурации: {BackupPath}", backupPath);

                // Удаляем старые резервные копии (оставляем последние 10)
                var backupFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "appsettings.json.backup.*")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .Skip(10)
                    .ToArray();

                foreach (var oldBackup in backupFiles)
                {
                    try
                    {
                        File.Delete(oldBackup);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Не удалось удалить старую резервную копию: {BackupPath}", oldBackup);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Не удалось создать резервную копию конфигурации");
            }
        }

        private string UpdateBotConfigurationInJson(JsonElement rootElement, BotConfiguration newBotConfig)
        {
            var rootDict = JsonElementToDictionary(rootElement);

            // Обновляем секцию BotConfiguration
            rootDict["BotConfiguration"] = new Dictionary<string, object>
            {
                ["Token"] = newBotConfig.Token,
                ["ChatId"] = newBotConfig.ChatId,
                ["TopicIds"] = new Dictionary<string, object>
                {
                    ["Warnings"] = newBotConfig.TopicIds.Warnings,
                    ["UserErrors"] = newBotConfig.TopicIds.UserErrors,
                    ["ServerErrors"] = newBotConfig.TopicIds.ServerErrors
                },
                ["ReportsFolder"] = newBotConfig.ReportsFolder,
                ["SentFilesDatabase"] = newBotConfig.SentFilesDatabase,
                ["AdminUsers"] = newBotConfig.AdminUsers,
                ["NotificationSettings"] = newBotConfig.NotificationSettings != null ? new Dictionary<string, object>
                {
                    ["SendStartupNotifications"] = newBotConfig.NotificationSettings.SendStartupNotifications,
                    ["SendShutdownNotifications"] = newBotConfig.NotificationSettings.SendShutdownNotifications,
                    ["SendErrorNotifications"] = newBotConfig.NotificationSettings.SendErrorNotifications,
                    ["SendDailyReports"] = newBotConfig.NotificationSettings.SendDailyReports,
                    ["DailyReportTime"] = newBotConfig.NotificationSettings.DailyReportTime
                } : null,
                ["RateLimiting"] = newBotConfig.RateLimiting != null ? new Dictionary<string, object>
                {
                    ["MaxFilesPerMinute"] = newBotConfig.RateLimiting.MaxFilesPerMinute,
                    ["MaxFilesPerHour"] = newBotConfig.RateLimiting.MaxFilesPerHour,
                    ["CooldownBetweenUploads"] = newBotConfig.RateLimiting.CooldownBetweenUploads,
                    ["MaxRetries"] = newBotConfig.RateLimiting.MaxRetries
                } : null
            };

            return JsonSerializer.Serialize(rootDict, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        private Dictionary<string, object> JsonElementToDictionary(JsonElement element)
        {
            var dictionary = new Dictionary<string, object>();

            foreach (var property in element.EnumerateObject())
            {
                dictionary[property.Name] = JsonElementToObject(property.Value);
            }

            return dictionary;
        }

        private object JsonElementToObject(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => JsonElementToDictionary(element),
                JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToArray(),
                JsonValueKind.String => element.GetString()!,
                JsonValueKind.Number => element.TryGetInt32(out var intValue) ? intValue : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null!,
                _ => element.ToString()
            };
        }

        private string MaskSensitiveData(string data)
        {
            if (string.IsNullOrEmpty(data))
                return data;

            if (data.Length <= 8)
                return "***";

            return data.Substring(0, 4) + "***" + data.Substring(data.Length - 4);
        }

        public void Dispose()
        {
            _configWatcher?.Dispose();
        }
    }

    /// <summary>
    /// Информация о конфигурации
    /// </summary>
    public class ConfigurationInfo
    {
        public string ConfigFilePath { get; set; } = string.Empty;
        public bool ConfigFileExists { get; set; }
        public long ConfigFileSize { get; set; }
        public DateTime? ConfigFileLastModified { get; set; }
        public bool IsValid { get; set; }
        public List<string> ValidationErrors { get; set; } = new();
        public List<string> ValidationWarnings { get; set; } = new();
        public int RuntimeSettingsCount { get; set; }
        public int AdminUsersCount { get; set; }
        public DateTime? LastReloadTime { get; set; }
    }
}