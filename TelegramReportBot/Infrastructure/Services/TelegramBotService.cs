using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramReportBot.Core.Enum;
using TelegramReportBot.Core.Enums;
using TelegramReportBot.Core.Interface;
using TelegramReportBot.Core.Interfaces;
using TelegramReportBot.Core.Models;

namespace TelegramReportBot.Infrastructure.Services;

/// <summary>
/// Сервис для работы с Telegram Bot API
/// </summary>
public class TelegramBotService : ITelegramBotService
{
    private readonly ITelegramBotClient _botClient;
    private readonly BotConfiguration _config;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly ReceiverOptions _receiverOptions;

    public event Func<ReportType, string, Task>? ManualDistributionRequested;

    public TelegramBotService(IOptions<BotConfiguration> config, ILogger<TelegramBotService> logger)
    {
        _config = config.Value;
        _logger = logger;

        // Инициализация Telegram Bot Client
        _botClient = new TelegramBotClient(_config.Token);

        // Настройки получения обновлений
        _receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message },
            ThrowPendingUpdates = true
        };
    }

    /// <summary>
    /// Запуск бота
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 Запуск Telegram-бота...");

        try
        {
            // Получаем информацию о боте
            var me = await _botClient.GetMeAsync(cancellationToken);
            _logger.LogInformation("🤖 Бот запущен: @{Username} (ID: {BotId})", me.Username, me.Id);

            // Начинаем получать обновления
            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: _receiverOptions,
                cancellationToken: cancellationToken
            );

            _logger.LogInformation("✅ Бот готов к работе");
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
        return Task.CompletedTask;
    }

    /// <summary>
    /// Отправка PDF-файла
    /// </summary>
    public async Task<bool> SendPdfFileAsync(string filePath, int topicId, string caption)
    {
        try
        {
            _logger.LogInformation("📤 Отправка файла {FileName} в топик {TopicId}",
                Path.GetFileName(filePath), topicId);

            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var inputFile = InputFile.FromStream(fileStream, Path.GetFileName(filePath));

            var message = await _botClient.SendDocumentAsync(
                chatId: _config.ChatId,
                document: inputFile,
                caption: caption,
                messageThreadId: topicId,
                cancellationToken: CancellationToken.None
            );

            _logger.LogInformation("✅ Файл {FileName} успешно отправлен в топик {TopicId}. MessageId: {MessageId}",
                Path.GetFileName(filePath), topicId, message.MessageId);

            return true;
        }
        catch (ApiRequestException apiEx) when (apiEx.ErrorCode == 429)
        {
            _logger.LogWarning("⏳ Rate limit от Telegram API. Повторная попытка через 5 секунд");
            await Task.Delay(5000);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка отправки файла {FileName} в топик {TopicId}",
                Path.GetFileName(filePath), topicId);
            return false;
        }
    }

    /// <summary>
    /// Отправка уведомления о запуске
    /// </summary>
    public async Task SendStartupNotificationAsync()
    {
        try
        {
            var startupMessage = $"""
                🚀 **TELEGRAM REPORTS BOT ЗАПУЩЕН**
                
                ⏰ **Время запуска:** {DateTime.Now:dd.MM.yyyy HH:mm:ss}
                📁 **Папка мониторинга:** `{_config.ReportsFolder}`
                👥 **Администраторы:** {_config.AdminUsers.Count}
                
                ✅ Бот готов к работе!
                
                **Доступные команды:**
                • `/рассылка` - ручная рассылка всех файлов
                • `/статус` - статус системы
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
    /// Отправка уведомления об ошибке
    /// </summary>
    public async Task SendErrorNotificationAsync(Exception error)
    {
        try
        {
            var errorMessage = $"""
                🚨 **КРИТИЧЕСКАЯ ОШИБКА В БОТЕ**
                
                ⏰ **Время:** {DateTime.Now:dd.MM.yyyy HH:mm:ss}
                ❌ **Ошибка:** `{error.Message}`
                
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
            if (update.Type == UpdateType.Message && update.Message?.Text is { } messageText)
            {
                await HandleMessageAsync(update.Message, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка обработки обновления");
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var userId = message.From?.Id.ToString() ?? "Unknown";
        var messageText = message.Text!;

        _logger.LogInformation("💬 Сообщение от {UserId}: {MessageText}", userId, messageText);

        switch (messageText.ToLowerInvariant())
        {
            case "/рассылка":
            case "/start":
                await HandleDistributionCommand(chatId, userId, cancellationToken);
                break;
            case "/статус":
                await HandleStatusCommand(chatId, cancellationToken);
                break;
            default:
                if (messageText.StartsWith("/"))
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "❓ Неизвестная команда. Доступные команды:\n• /рассылка - ручная рассылка\n• /статус - статус системы",
                        cancellationToken: cancellationToken
                    );
                }
                break;
        }
    }

    private async Task HandleDistributionCommand(long chatId, string userId, CancellationToken cancellationToken)
    {
        try
        {
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "🔄 Запуск ручной рассылки всех новых файлов...",
                cancellationToken: cancellationToken
            );

            // Вызываем событие для запуска рассылки
            if (ManualDistributionRequested != null)
            {
                await ManualDistributionRequested.Invoke(ReportType.All, userId);
            }

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "✅ Рассылка завершена!",
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка обработки команды рассылки");
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "❌ Ошибка при выполнении рассылки",
                cancellationToken: cancellationToken
            );
        }
    }

    private async Task HandleStatusCommand(long chatId, CancellationToken cancellationToken)
    {
        var statusText = $"""
            📊 **СТАТУС СИСТЕМЫ**
            
            🟢 **Статус:** Работает
            ⏰ **Время работы:** {DateTime.Now:HH:mm:ss dd.MM.yyyy}
            📁 **Папка:** `{_config.ReportsFolder}`
            """;

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: statusText,
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken
        );
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

    private async Task SendToAdminsAsync(string message)
    {
        foreach (var adminUser in _config.AdminUsers)
        {
            try
            {
                if (long.TryParse(adminUser.Replace("@", ""), out var chatId))
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