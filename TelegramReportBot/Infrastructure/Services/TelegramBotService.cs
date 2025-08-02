using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramReportBot.Core.Enum;
using TelegramReportBot.Core.Enums;
using TelegramReportBot.Core.Interface;
using TelegramReportBot.Core.Interfaces;
using TelegramReportBot.Core.Models.Configuration;
using TelegramReportBot.Core.Models.Statistics;

namespace TelegramReportBot.Infrastructure.Services;

/// <summary>
/// Расширенный сервис для работы с Telegram Bot API
/// </summary>
public class TelegramBotService : ITelegramBotService
{
    private readonly ITelegramBotClient _botClient;
    private readonly BotConfiguration _config;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly ReceiverOptions _receiverOptions;
    private readonly SemaphoreSlim _rateLimitSemaphore;
    private readonly Queue<DateTime> _recentUploads;
    private readonly object _rateLimitLock = new();

    public event Func<ReportType, string, Task>? ManualDistributionRequested;
    public event Func<string, Task<StatisticsReport>>? StatisticsRequested;
    public event Func<AdminCommand, string, Task>? AdminCommandReceived;

    public TelegramBotService(IOptions<BotConfiguration> config, ILogger<TelegramBotService> logger)
    {
        _config = config.Value;
        _logger = logger;

        // Инициализация Telegram Bot Client
        _botClient = new TelegramBotClient(_config.Token);

        // Настройки получения обновлений
        _receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] {
                UpdateType.Message,
                UpdateType.CallbackQuery,
                UpdateType.InlineQuery,
                UpdateType.ChatMember
            },
            ThrowPendingUpdates = true
        };

        // Инициализация rate limiting
        _rateLimitSemaphore = new SemaphoreSlim(_config.RateLimiting.MaxFilesPerMinute);
        _recentUploads = new Queue<DateTime>();
    }

    /// <summary>
    /// Запуск бота с расширенными возможностями
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 Запуск расширенного Telegram-бота...");

        try
        {
            // Получаем информацию о боте
            var me = await _botClient.GetMeAsync(cancellationToken);
            _logger.LogInformation("🤖 Бот запущен: @{Username} (ID: {BotId})", me.Username, me.Id);
            _logger.LogInformation("📋 Имя бота: {FirstName}", me.FirstName);
            _logger.LogInformation("🔧 Поддерживает inline-запросы: {SupportsInlineQueries}", me.SupportsInlineQueries);

            // Устанавливаем команды бота
            await SetBotCommandsAsync();

            // Начинаем получать обновления
            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: _receiverOptions,
                cancellationToken: cancellationToken
            );

            _logger.LogInformation("✅ Бот готов к работе в расширенном режиме");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Ошибка запуска бота");
            throw;
        }
    }

    /// <summary>
    /// Остановка бота
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("⏹️ Остановка Telegram-бота...");
        _rateLimitSemaphore?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Отправка PDF-файла с улучшенной обработкой ошибок
    /// </summary>
    public async Task<bool> SendPdfFileAsync(string filePath, int topicId, string caption)
    {
        // Проверяем rate limiting
        if (!await CheckRateLimitAsync())
        {
            _logger.LogWarning("🚫 Превышен лимит отправки файлов");
            return false;
        }

        var attempts = 0;
        var maxAttempts = _config.RateLimiting.MaxRetries;

        while (attempts < maxAttempts)
        {
            attempts++;

            try
            {
                _logger.LogInformation("📤 Отправка файла {FileName} в топик {TopicId} (попытка {Attempt}/{MaxAttempts})",
                    Path.GetFileName(filePath), topicId, attempts, maxAttempts);

                await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var inputFile = InputFile.FromStream(fileStream, Path.GetFileName(filePath));

                // Создаём расширенную подпись
                var extendedCaption = CreateExtendedCaption(filePath, caption);

                var message = await _botClient.SendDocumentAsync(
                    chatId: _config.ChatId,
                    document: inputFile,
                    caption: extendedCaption,
                    messageThreadId: topicId,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: CancellationToken.None
                );

                _logger.LogInformation("✅ Файл {FileName} успешно отправлен в топик {TopicId}. MessageId: {MessageId}",
                    Path.GetFileName(filePath), topicId, message.MessageId);

                // Записываем в rate limiting
                RecordUpload();

                return true;
            }
            catch (ApiRequestException apiEx) when (apiEx.ErrorCode == 429)
            {
                // Too Many Requests - ждём и повторяем
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempts) * 5); // Exponential backoff
                _logger.LogWarning("⏳ Rate limit от Telegram API. Ожидание {Delay}s перед повтором", delay.TotalSeconds);
                await Task.Delay(delay);
            }
            catch (ApiRequestException apiEx) when (apiEx.ErrorCode == 400 && apiEx.Message.Contains("file must be non-empty"))
            {
                _logger.LogError("📄 Файл {FileName} пустой", Path.GetFileName(filePath));
                return false;
            }
            catch (ApiRequestException apiEx) when (apiEx.ErrorCode == 413)
            {
                _logger.LogError("📦 Файл {FileName} слишком большой для Telegram", Path.GetFileName(filePath));
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка отправки файла {FileName} в топик {TopicId} (попытка {Attempt})",
                    Path.GetFileName(filePath), topicId, attempts);

                if (attempts >= maxAttempts)
                    break;

                await Task.Delay(_config.RateLimiting.CooldownBetweenUploads * attempts);
            }
        }

        return false;
    }

    /// <summary>
    /// Отправка уведомления о запуске
    /// </summary>
    public async Task SendStartupNotificationAsync()
    {
        if (!_config.NotificationSettings.SendStartupNotifications)
            return;

        try
        {
            var startupMessage = $"""
                🚀 **TELEGRAM REPORTS BOT ЗАПУЩЕН**
                
                ⏰ **Время запуска:** {DateTime.Now:dd.MM.yyyy HH:mm:ss}
                🖥️ **Сервер:** {Environment.MachineName}
                📁 **Папка мониторинга:** `{_config.ReportsFolder}`
                👥 **Администраторы:** {_config.AdminUsers.Count}
                
                ✅ Все сервисы запущены и готовы к работе!
                
                **Доступные команды:**
                • `/reports` - панель управления
                • `/status` - статус системы
                • `/admin` - панель администратора
                """;

            await SendToAdminsAsync(startupMessage);
            _logger.LogInformation("📢 Уведомление о запуске отправлено администраторам");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка отправки уведомления о запуске");
        }
    }

    /// <summary>
    /// Отправка уведомления о завершении работы
    /// </summary>
    public async Task SendShutdownNotificationAsync()
    {
        if (!_config.NotificationSettings.SendShutdownNotifications)
            return;

        try
        {
            var shutdownMessage = $"""
                ⏹️ **TELEGRAM REPORTS BOT ОСТАНОВЛЕН**
                
                ⏰ **Время завершения:** {DateTime.Now:dd.MM.yyyy HH:mm:ss}
                
                До свидания! 👋
                """;

            await SendToAdminsAsync(shutdownMessage);
            _logger.LogInformation("📢 Уведомление о завершении отправлено администраторам");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка отправки уведомления о завершении");
        }
    }

    /// <summary>
    /// Отправка уведомления об ошибке
    /// </summary>
    public async Task SendErrorNotificationAsync(Exception error)
    {
        if (!_config.NotificationSettings.SendErrorNotifications)
            return;

        try
        {
            var errorMessage = $"""
                🚨 **КРИТИЧЕСКАЯ ОШИБКА В БОТЕ**
                
                ⏰ **Время:** {DateTime.Now:dd.MM.yyyy HH:mm:ss}
                ❌ **Тип ошибки:** `{error.GetType().Name}`
                
                **Сообщение:**
                ```
                {error.Message}
                ```
                
                **Требуется вмешательство администратора!**
                """;

            await SendToAdminsAsync(errorMessage);
            _logger.LogInformation("🚨 Уведомление об ошибке отправлено администраторам");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка отправки уведомления об ошибке");
        }
    }

    // Приватные методы

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            switch (update.Type)
            {
                case UpdateType.Message:
                    await HandleMessageAsync(update.Message!, cancellationToken);
                    break;
                case UpdateType.CallbackQuery:
                    await HandleCallbackQueryAsync(update.CallbackQuery!, cancellationToken);
                    break;
                case UpdateType.InlineQuery:
                    await HandleInlineQueryAsync(update.InlineQuery!, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка обработки обновления {UpdateType}", update.Type);
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        if (message.Text is not { } messageText)
            return;

        var chatId = message.Chat.Id;
        var userId = message.From?.Id.ToString() ?? "Unknown";
        var userName = message.From?.Username ?? message.From?.FirstName ?? "Unknown";

        _logger.LogInformation("💬 Сообщение от {UserName} ({UserId}): {MessageText}",
            userName, userId, messageText);

        var parts = messageText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();

        switch (command)
        {
            case "/start":
                await ShowWelcomeMessageAsync(chatId, userName, cancellationToken);
                break;
            case "/reports" or "/отчеты":
                await ShowReportsMenuAsync(chatId, cancellationToken);
                break;
            case "/status" or "/статус":
                await ShowDetailedStatusAsync(chatId, cancellationToken);
                break;
            case "/admin" or "/админ":
                await ShowAdminPanelAsync(chatId, userId, cancellationToken);
                break;
            case "/help" or "/помощь":
                await ShowExtendedHelpAsync(chatId, cancellationToken);
                break;
            default:
                if (messageText.StartsWith("/"))
                {
                    await HandleUnknownCommandAsync(chatId, command, cancellationToken);
                }
                break;
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;
        var userId = callbackQuery.From.Id.ToString();
        var data = callbackQuery.Data!;

        await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);

        var parts = data.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var action = parts[0];

        switch (action)
        {
            case "send_all":
                await ProcessDistributionRequestAsync(ReportType.All, chatId, userId, "🔄 Запускаю рассылку всех отчётов...", cancellationToken);
                break;
            case "send_user":
                await ProcessDistributionRequestAsync(ReportType.UserErrors, chatId, userId, "👤 Отправляю пользовательские ошибки...", cancellationToken);
                break;
            case "send_server":
                await ProcessDistributionRequestAsync(ReportType.ServerErrors, chatId, userId, "🖥️ Отправляю серверные ошибки...", cancellationToken);
                break;
            case "send_warnings":
                await ProcessDistributionRequestAsync(ReportType.Warnings, chatId, userId, "⚠️ Отправляю предупреждения...", cancellationToken);
                break;
            case "refresh_menu":
                await ShowReportsMenuAsync(chatId, cancellationToken);
                break;
        }
    }

    private async Task HandleInlineQueryAsync(InlineQuery inlineQuery, CancellationToken cancellationToken)
    {
        try
        {
            var query = inlineQuery.Query.ToLowerInvariant();
            var results = new List<InlineQueryResult>();

            if (query.Contains("status") || string.IsNullOrEmpty(query))
            {
                results.Add(new InlineQueryResultArticle(
                    id: "status",
                    title: "📊 Статус бота",
                    inputMessageContent: new InputTextMessageContent("/status")
                ));
            }

            if (query.Contains("reports") || string.IsNullOrEmpty(query))
            {
                results.Add(new InlineQueryResultArticle(
                    id: "reports",
                    title: "📋 Панель отчётов",
                    inputMessageContent: new InputTextMessageContent("/reports")
                ));
            }

            await _botClient.AnswerInlineQueryAsync(
                inlineQueryId: inlineQuery.Id,
                results: results,
                cacheTime: 60,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка обработки inline query");
        }
    }

    // Методы UI - заглушки, которые нужно будет реализовать
    private async Task ShowWelcomeMessageAsync(long chatId, string userName, CancellationToken cancellationToken)
    {
        var welcomeMessage = $"👋 Добро пожаловать, {userName}!\n\nИспользуйте /reports для управления рассылкой.";
        await _botClient.SendTextMessageAsync(chatId, welcomeMessage, cancellationToken: cancellationToken);
    }

    private async Task ShowReportsMenuAsync(long chatId, CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("📋 Все отчёты", "send_all") },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("👤 Пользовательские", "send_user"),
                InlineKeyboardButton.WithCallbackData("🖥️ Серверные", "send_server")
            },
            new[] { InlineKeyboardButton.WithCallbackData("⚠️ Предупреждения", "send_warnings") },
            new[] { InlineKeyboardButton.WithCallbackData("🔄 Обновить", "refresh_menu") }
        });

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "📊 **ПАНЕЛЬ УПРАВЛЕНИЯ ОТЧЁТАМИ**\n\nВыберите тип рассылки:",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken
        );
    }

    private async Task ShowDetailedStatusAsync(long chatId, CancellationToken cancellationToken)
    {
        var statusText = $"""
            📊 **СТАТУС СИСТЕМЫ**
            
            🟢 **Статус:** Работает
            ⏰ **Время работы:** Информация загружается...
            📁 **Папка:** `{_config.ReportsFolder}`
            
            🕐 **Проверено:** {DateTime.Now:HH:mm:ss dd.MM.yyyy}
            """;

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: statusText,
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken
        );
    }

    // Остальные методы - заглушки
    private Task ShowAdminPanelAsync(long chatId, string userId, CancellationToken cancellationToken) => Task.CompletedTask;
    private Task ShowExtendedHelpAsync(long chatId, CancellationToken cancellationToken) => Task.CompletedTask;
    private Task HandleUnknownCommandAsync(long chatId, string command, CancellationToken cancellationToken) => Task.CompletedTask;
    private Task SetBotCommandsAsync() => Task.CompletedTask;

    private async Task ProcessDistributionRequestAsync(ReportType reportType, long chatId, string userId, string startMessage, CancellationToken cancellationToken)
    {
        try
        {
            var processingMessage = await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: startMessage,
                cancellationToken: cancellationToken
            );

            if (ManualDistributionRequested != null)
            {
                await ManualDistributionRequested.Invoke(reportType, userId);

                var completedMessage = "✅ Рассылка завершена";
                await _botClient.EditMessageTextAsync(
                    chatId: chatId,
                    messageId: processingMessage.MessageId,
                    text: completedMessage,
                    cancellationToken: cancellationToken
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка обработки запроса рассылки {ReportType}", reportType);
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        _logger.LogError("❌ Ошибка получения обновлений: {ErrorMessage}", errorMessage);
        return Task.CompletedTask;
    }

    // Вспомогательные методы
    private async Task<bool> CheckRateLimitAsync()
    {
        lock (_rateLimitLock)
        {
            var now = DateTime.Now;
            var oneMinuteAgo = now.AddMinutes(-1);

            while (_recentUploads.Count > 0 && _recentUploads.Peek() < oneMinuteAgo)
            {
                _recentUploads.Dequeue();
            }

            return _recentUploads.Count < _config.RateLimiting.MaxFilesPerMinute;
        }
    }

    private void RecordUpload()
    {
        lock (_rateLimitLock)
        {
            _recentUploads.Enqueue(DateTime.Now);
        }
    }

    private string CreateExtendedCaption(string filePath, string originalCaption)
    {
        var fileInfo = new System.IO.FileInfo(filePath);
        var fileSize = fileInfo.Length;
        var timestamp = DateTime.Now;

        return $"""
            📄 **{originalCaption}**
            
            📊 **Размер:** {FormatFileSize(fileSize)}
            ⏰ **Обработан:** {timestamp:HH:mm:ss dd.MM.yyyy}
            🤖 *Отправлено автоматически*
            """;
    }

    private string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} байт";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} КБ";
        return $"{bytes / (1024 * 1024):F1} МБ";
    }

    private async Task SendToAdminsAsync(string message)
    {
        foreach (var adminUser in _config.AdminUsers)
        {
            try
            {
                if (long.TryParse(adminUser, out var chatId))
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: message,
                        parseMode: ParseMode.Markdown
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка отправки сообщения админу {AdminUser}", adminUser);
            }
        }
    }
}