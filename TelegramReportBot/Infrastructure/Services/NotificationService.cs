using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;
using TelegramReportBot.Core.Enum;
using TelegramReportBot.Core.Enums;
using TelegramReportBot.Core.Interfaces;
using TelegramReportBot.Core.Models.Configuration;
using TelegramReportBot.Core.Models.Notifications;

namespace TelegramReportBot.Infrastructure.Services
{
    /// <summary>
    /// Сервис управления уведомлениями и подписками пользователей
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly BotConfiguration _config;
        private readonly ITelegramBotService _telegramService;
        private readonly ILogger<NotificationService> _logger;

        private readonly ConcurrentQueue<NotificationMessage> _notificationQueue;
        private readonly ConcurrentDictionary<string, HashSet<NotificationType>> _userSubscriptions;
        private readonly List<NotificationMessage> _notificationHistory;
        private readonly Timer _processQueueTimer;
        private readonly Timer _scheduledNotificationsTimer;
        private readonly SemaphoreSlim _processingLock;

        private readonly Dictionary<string, List<ScheduledNotification>> _scheduledNotifications;
        private readonly object _historyLock = new();

        public NotificationService(
            IOptions<BotConfiguration> config,
            ITelegramBotService telegramService,
            ILogger<NotificationService> logger)
        {
            _config = config.Value;
            _telegramService = telegramService;
            _logger = logger;

            _notificationQueue = new ConcurrentQueue<NotificationMessage>();
            _userSubscriptions = new ConcurrentDictionary<string, HashSet<NotificationType>>();
            _notificationHistory = new List<NotificationMessage>();
            _scheduledNotifications = new Dictionary<string, List<ScheduledNotification>>();
            _processingLock = new SemaphoreSlim(1, 1);

            // Таймеры для обработки очереди и запланированных уведомлений
            _processQueueTimer = new Timer(ProcessNotificationQueue, null,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

            _scheduledNotificationsTimer = new Timer(ProcessScheduledNotifications, null,
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

            LoadUserSubscriptions();
            _logger.LogInformation("📢 Сервис уведомлений инициализирован");
        }

        /// <summary>
        /// Отправка уведомления
        /// </summary>
        public async Task SendNotificationAsync(NotificationMessage notification)
        {
            try
            {
                if (notification == null)
                {
                    _logger.LogWarning("⚠️ Попытка отправки пустого уведомления");
                    return;
                }

                // Устанавливаем время создания если не задано
                if (notification.CreatedAt == default)
                {
                    notification.CreatedAt = DateTime.Now;
                }

                // Определяем получателей
                if (!notification.Recipients.Any())
                {
                    notification.Recipients = GetDefaultRecipients(notification.Type, notification.Priority);
                }

                // Фильтруем получателей по подпискам
                notification.Recipients = FilterRecipientsBySubscriptions(notification.Recipients, notification.Type);

                if (!notification.Recipients.Any())
                {
                    _logger.LogDebug("📭 Нет подписчиков для уведомления типа {Type}", notification.Type);
                    return;
                }

                _logger.LogInformation("📤 Отправка уведомления '{Title}' типа {Type} с приоритетом {Priority} получателям: {Recipients}",
                    notification.Title, notification.Type, notification.Priority, string.Join(", ", notification.Recipients));

                // Добавляем в очередь для асинхронной обработки
                _notificationQueue.Enqueue(notification);

                // Добавляем в историю
                AddToHistory(notification);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка отправки уведомления");
            }
        }

        /// <summary>
        /// Отправка группового уведомления
        /// </summary>
        public async Task SendBulkNotificationAsync(List<string> recipients, string message)
        {
            try
            {
                var notification = new NotificationMessage
                {
                    Title = "Групповое уведомление",
                    Message = message,
                    Type = NotificationType.Info,
                    Priority = NotificationPriority.Normal,
                    Recipients = recipients
                };

                await SendNotificationAsync(notification);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка отправки группового уведомления");
            }
        }

        /// <summary>
        /// Планирование отложенного уведомления
        /// </summary>
        public async Task ScheduleNotificationAsync(NotificationMessage notification, DateTime when)
        {
            try
            {
                if (when <= DateTime.Now)
                {
                    // Если время уже прошло, отправляем сразу
                    await SendNotificationAsync(notification);
                    return;
                }

                var scheduledNotification = new ScheduledNotification
                {
                    Id = Guid.NewGuid().ToString(),
                    Notification = notification,
                    ScheduledTime = when,
                    CreatedAt = DateTime.Now
                };

                var dayKey = when.ToString("yyyy-MM-dd");

                lock (_scheduledNotifications)
                {
                    if (!_scheduledNotifications.ContainsKey(dayKey))
                    {
                        _scheduledNotifications[dayKey] = new List<ScheduledNotification>();
                    }

                    _scheduledNotifications[dayKey].Add(scheduledNotification);
                }

                _logger.LogInformation("⏰ Запланировано уведомление '{Title}' на {ScheduledTime}",
                    notification.Title, when.ToString("dd.MM.yyyy HH:mm"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка планирования уведомления");
            }
        }

        /// <summary>
        /// Получение истории уведомлений
        /// </summary>
        public async Task<List<NotificationMessage>> GetNotificationHistoryAsync(int count = 100)
        {
            lock (_historyLock)
            {
                return _notificationHistory
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(count)
                    .ToList();
            }
        }

        /// <summary>
        /// Управление подписками пользователей
        /// </summary>
        public async Task ManageUserSubscriptionAsync(string userId, NotificationType type, bool subscribe)
        {
            try
            {
                if (!_userSubscriptions.ContainsKey(userId))
                {
                    _userSubscriptions[userId] = new HashSet<NotificationType>();
                }

                var userSubs = _userSubscriptions[userId];

                if (subscribe)
                {
                    userSubs.Add(type);
                    _logger.LogInformation("✅ Пользователь {UserId} подписался на уведомления типа {Type}", userId, type);
                }
                else
                {
                    userSubs.Remove(type);
                    _logger.LogInformation("❌ Пользователь {UserId} отписался от уведомлений типа {Type}", userId, type);
                }

                // Сохраняем подписки
                await SaveUserSubscriptionsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка управления подписками пользователя {UserId}", userId);
            }
        }

        /// <summary>
        /// Отправка уведомления о системных событиях
        /// </summary>
        public async Task SendSystemNotificationAsync(string title, string message, NotificationPriority priority = NotificationPriority.Normal)
        {
            var notification = new NotificationMessage
            {
                Title = title,
                Message = message,
                Type = NotificationType.System,
                Priority = priority,
                Recipients = _config.AdminUsers
            };

            await SendNotificationAsync(notification);
        }

        /// <summary>
        /// Отправка уведомления об ошибке
        /// </summary>
        public async Task SendErrorNotificationAsync(string title, string errorMessage, Exception? exception = null)
        {
            var message = errorMessage;
            if (exception != null)
            {
                message += $"\n\nДетали ошибки:\n{exception.Message}";
                if (!string.IsNullOrEmpty(exception.StackTrace))
                {
                    message += $"\n\nСтек вызовов:\n{exception.StackTrace.Substring(0, Math.Min(500, exception.StackTrace.Length))}";
                }
            }

            var notification = new NotificationMessage
            {
                Title = $"🚨 {title}",
                Message = message,
                Type = NotificationType.Error,
                Priority = NotificationPriority.Critical,
                Recipients = _config.AdminUsers
            };

            await SendNotificationAsync(notification);
        }

        /// <summary>
        /// Отправка уведомления об успехе
        /// </summary>
        public async Task SendSuccessNotificationAsync(string title, string message, List<string>? recipients = null)
        {
            var notification = new NotificationMessage
            {
                Title = $"✅ {title}",
                Message = message,
                Type = NotificationType.Success,
                Priority = NotificationPriority.Normal,
                Recipients = recipients ?? _config.AdminUsers
            };

            await SendNotificationAsync(notification);
        }

        /// <summary>
        /// Отправка предупреждения
        /// </summary>
        public async Task SendWarningNotificationAsync(string title, string message, List<string>? recipients = null)
        {
            var notification = new NotificationMessage
            {
                Title = $"⚠️ {title}",
                Message = message,
                Type = NotificationType.Warning,
                Priority = NotificationPriority.High,
                Recipients = recipients ?? _config.AdminUsers
            };

            await SendNotificationAsync(notification);
        }

        /// <summary>
        /// Получение статистики уведомлений
        /// </summary>
        public async Task<NotificationStatistics> GetNotificationStatisticsAsync()
        {
            var stats = new NotificationStatistics();

            lock (_historyLock)
            {
                var now = DateTime.Now;
                var today = now.Date;
                var weekAgo = now.AddDays(-7);
                var monthAgo = now.AddDays(-30);

                stats.TotalNotifications = _notificationHistory.Count;
                stats.NotificationsToday = _notificationHistory.Count(n => n.CreatedAt.Date == today);
                stats.NotificationsThisWeek = _notificationHistory.Count(n => n.CreatedAt >= weekAgo);
                stats.NotificationsThisMonth = _notificationHistory.Count(n => n.CreatedAt >= monthAgo);

                stats.NotificationsByType = _notificationHistory
                    .GroupBy(n => n.Type)
                    .ToDictionary(g => g.Key, g => g.Count());

                stats.NotificationsByPriority = _notificationHistory
                    .GroupBy(n => n.Priority)
                    .ToDictionary(g => g.Key, g => g.Count());

                var lastNotification = _notificationHistory.OrderByDescending(n => n.CreatedAt).FirstOrDefault();
                if (lastNotification != null)
                {
                    stats.LastNotification = lastNotification.CreatedAt;
                    stats.LastNotificationTitle = lastNotification.Title;
                }

                stats.SubscribersCount = _userSubscriptions.Count;
                stats.QueueSize = _notificationQueue.Count;
            }

            lock (_scheduledNotifications)
            {
                stats.ScheduledNotificationsCount = _scheduledNotifications.Values.Sum(list => list.Count);
            }

            return stats;
        }

        // Приватные методы

        private List<string> GetDefaultRecipients(NotificationType type, NotificationPriority priority)
        {
            // По умолчанию критические уведомления отправляем всем админам
            if (priority == NotificationPriority.Critical)
            {
                return new List<string>(_config.AdminUsers);
            }

            // Системные уведомления только админам
            if (type == NotificationType.System || type == NotificationType.Error)
            {
                return new List<string>(_config.AdminUsers);
            }

            // Информационные уведомления могут не отправляться автоматически
            return new List<string>();
        }

        private List<string> FilterRecipientsBySubscriptions(List<string> recipients, NotificationType type)
        {
            var filteredRecipients = new List<string>();

            foreach (var recipient in recipients)
            {
                // Админы получают все уведомления независимо от подписок
                if (_config.AdminUsers.Contains(recipient))
                {
                    filteredRecipients.Add(recipient);
                    continue;
                }

                // Проверяем подписки пользователя
                if (_userSubscriptions.TryGetValue(recipient, out var subscriptions))
                {
                    if (subscriptions.Contains(type) || subscriptions.Contains(NotificationType.System))
                    {
                        filteredRecipients.Add(recipient);
                    }
                }
                else
                {
                    // Если подписки не настроены, отправляем только критические уведомления
                    if (type == NotificationType.Error || type == NotificationType.System)
                    {
                        filteredRecipients.Add(recipient);
                    }
                }
            }

            return filteredRecipients;
        }

        private void AddToHistory(NotificationMessage notification)
        {
            lock (_historyLock)
            {
                _notificationHistory.Add(notification);

                // Ограничиваем размер истории
                if (_notificationHistory.Count > 1000)
                {
                    _notificationHistory.RemoveRange(0, 100);
                }
            }
        }

        private async void ProcessNotificationQueue(object? state)
        {
            if (!await _processingLock.WaitAsync(100))
            {
                return; // Пропускаем, если уже идет обработка
            }

            try
            {
                var processedCount = 0;
                var maxBatch = 5; // Обрабатываем максимум 5 уведомлений за раз

                while (processedCount < maxBatch && _notificationQueue.TryDequeue(out var notification))
                {
                    try
                    {
                        await ProcessSingleNotificationAsync(notification);
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Ошибка обработки уведомления '{Title}'", notification.Title);
                    }
                }

                if (processedCount > 0)
                {
                    _logger.LogDebug("📤 Обработано уведомлений: {ProcessedCount}", processedCount);
                }
            }
            finally
            {
                _processingLock.Release();
            }
        }

        private async Task ProcessSingleNotificationAsync(NotificationMessage notification)
        {
            var formattedMessage = FormatNotificationMessage(notification);

            foreach (var recipient in notification.Recipients)
            {
                try
                {
                    // Пытаемся отправить как ID чата
                    if (long.TryParse(recipient, out var chatId))
                    {
                        // Здесь нужно использовать прямой доступ к Telegram Bot API
                        // Пока что логируем, что сообщение нужно отправить
                        _logger.LogInformation("📨 Отправка уведомления получателю {Recipient}: {Message}",
                            recipient, formattedMessage);
                    }
                    else
                    {
                        // Обработка username
                        _logger.LogInformation("📨 Отправка уведомления пользователю {Username}: {Message}",
                            recipient, formattedMessage);
                    }

                    // Небольшая задержка между отправками
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Ошибка отправки уведомления получателю {Recipient}", recipient);
                }
            }
        }

        private string FormatNotificationMessage(NotificationMessage notification)
        {
            var priorityIcon = notification.Priority switch
            {
                NotificationPriority.Critical => "🔴",
                NotificationPriority.High => "🟡",
                NotificationPriority.Normal => "🔵",
                NotificationPriority.Low => "⚪",
                _ => ""
            };

            var typeIcon = notification.Type switch
            {
                NotificationType.Error => "❌",
                NotificationType.Warning => "⚠️",
                NotificationType.Success => "✅",
                NotificationType.Info => "ℹ️",
                NotificationType.System => "🔧",
                _ => ""
            };

            var formattedMessage = $"{priorityIcon}{typeIcon} **{notification.Title}**\n\n{notification.Message}";

            if (notification.CreatedAt != default)
            {
                formattedMessage += $"\n\n🕐 {notification.CreatedAt:dd.MM.yyyy HH:mm:ss}";
            }

            return formattedMessage;
        }

        private async void ProcessScheduledNotifications(object? state)
        {
            try
            {
                var now = DateTime.Now;
                var currentDayKey = now.ToString("yyyy-MM-dd");
                var previousDayKey = now.AddDays(-1).ToString("yyyy-MM-dd");

                List<ScheduledNotification> dueNotifications;

                lock (_scheduledNotifications)
                {
                    dueNotifications = new List<ScheduledNotification>();
                    var keysToProcess = new[] { previousDayKey, currentDayKey };

                    foreach (var dayKey in keysToProcess)
                    {
                        if (!_scheduledNotifications.TryGetValue(dayKey, out var notifications))
                            continue;

                        dueNotifications.AddRange(
                            notifications.Where(sn => sn.ScheduledTime <= now && !sn.IsSent));

                        if (dayKey == previousDayKey)
                        {
                            notifications.RemoveAll(sn => sn.IsSent);
                            if (!notifications.Any())
                            {
                                _scheduledNotifications.Remove(dayKey);
                            }
                        }
                    }
                }

                foreach (var scheduledNotification in dueNotifications)
                {
                    try
                    {
                        await SendNotificationAsync(scheduledNotification.Notification);
                        scheduledNotification.IsSent = true;
                        scheduledNotification.SentAt = now;

                        _logger.LogInformation("⏰ Отправлено запланированное уведомление: {Title}",
                            scheduledNotification.Notification.Title);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Ошибка отправки запланированного уведомления: {Title}",
                            scheduledNotification.Notification.Title);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка обработки запланированных уведомлений");
            }
        }

        private void LoadUserSubscriptions()
        {
            try
            {
                var subscriptionsFile = Path.Combine("Data", "user_subscriptions.json");
                if (File.Exists(subscriptionsFile))
                {
                    var json = File.ReadAllText(subscriptionsFile);
                    var subscriptions = JsonSerializer.Deserialize<Dictionary<string, NotificationType[]>>(json);

                    if (subscriptions != null)
                    {
                        foreach (var kvp in subscriptions)
                        {
                            _userSubscriptions[kvp.Key] = new HashSet<NotificationType>(kvp.Value);
                        }

                        _logger.LogInformation("📋 Загружены подписки для {Count} пользователей", subscriptions.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ошибка загрузки подписок пользователей");
            }
        }

        private async Task SaveUserSubscriptionsAsync()
        {
            try
            {
                var subscriptionsData = _userSubscriptions.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToArray()
                );

                var json = JsonSerializer.Serialize(subscriptionsData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                var subscriptionsFile = Path.Combine("Data", "user_subscriptions.json");
                var directory = Path.GetDirectoryName(subscriptionsFile);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(subscriptionsFile, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка сохранения подписок пользователей");
            }
        }

        public void Dispose()
        {
            _processQueueTimer?.Dispose();
            _scheduledNotificationsTimer?.Dispose();
            _processingLock?.Dispose();
        }
    }

}
