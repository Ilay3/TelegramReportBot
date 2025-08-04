using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramReportBot.Core.Enum;
using TelegramReportBot.Core.Interfaces;
using TelegramReportBot.Core.Models.Configuration;

namespace TelegramReportBot.Infrastructure.Services;

public class TelegramBotService : ITelegramBotService
{
    private readonly ITelegramBotClient _botClient;
    private readonly BotConfiguration _config;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly ReceiverOptions _receiverOptions;

    public TelegramBotService(IOptions<BotConfiguration> config, ILogger<TelegramBotService> logger)
    {
        _config = config.Value;
        _logger = logger;
        _botClient = new TelegramBotClient(_config.Token);
        _receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message },
            ThrowPendingUpdates = true
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Запуск Telegram-бота...");
        _botClient.StartReceiving(HandleUpdateAsync, HandleErrorAsync, _receiverOptions, cancellationToken);
        await SendStartupNotificationAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Остановка Telegram-бота...");
        return Task.CompletedTask;
    }

    public async Task<bool> SendPdfFileAsync(string filePath, string caption)
    {
        if (!System.IO.File.Exists(filePath))
        {
            var ex = new FileNotFoundException("Файл не найден", filePath);
            await SendErrorNotificationAsync(ex);
            _logger.LogWarning(ex, "Файл отсутствует {File}", filePath);
            return false;
        }

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var inputFile = InputFile.FromStream(stream, Path.GetFileName(filePath));
            await _botClient.SendDocumentAsync(_config.ChatId, inputFile, caption: caption, cancellationToken: CancellationToken.None);
            return true;
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 429)
        {
            await SendErrorNotificationAsync(ex);
            _logger.LogWarning("Rate limit от Telegram API");
            return false;
        }
        catch (Exception ex)
        {
            await SendErrorNotificationAsync(ex);
            _logger.LogError(ex, "Ошибка отправки файла {File}", filePath);
            return false;
        }
    }

    public async Task SendStartupNotificationAsync()
    {
        await _botClient.SendTextMessageAsync(_config.ChatId, "Бот запущен и готов к работе.", cancellationToken: CancellationToken.None);
    }

    public async Task SendErrorNotificationAsync(Exception error)
    {
        await _botClient.SendTextMessageAsync(_config.ChatId, $"Ошибка: {error.Message}", cancellationToken: CancellationToken.None);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken token)
    {
        if (update.Type != UpdateType.Message || update.Message?.Text == null)
            return;

        var text = update.Message.Text.ToLowerInvariant();
        var chatId = update.Message.Chat.Id;

        if (!long.TryParse(_config.ChatId, out var allowedChat) || chatId != allowedChat)
        {
            await _botClient.SendTextMessageAsync(chatId, "Доступ запрещён.", cancellationToken: token);
            return;
        }

        switch (text)
        {
            case "/start":
                await SendMainMenuAsync(chatId, token);
                break;
            case "📤 рассылка":
            case "рассылка":
                await SendFilesAsync(ReportType.All, token);
                break;
            case "👤 пользовательские ошибки":
            case "пользовательские ошибки":
                await SendFilesAsync(ReportType.UserErrors, token);
                break;
            case "🖥️ серверные ошибки":
            case "серверные ошибки":
                await SendFilesAsync(ReportType.ServerErrors, token);
                break;
            case "⚠️ предупреждения":
            case "предупреждения":
                await SendFilesAsync(ReportType.Warnings, token);
                break;
            case "📊 статистика":
            case "статистика":
                await SendWeeklyStatisticsAsync(token);
                break;
            default:
                await SendMainMenuAsync(chatId, token);
                break;
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken token)
    {
        _logger.LogError(ex, "Ошибка Telegram");
        return Task.CompletedTask;
    }

    private async Task SendMainMenuAsync(long chatId, CancellationToken token)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("📤 Рассылка"), new KeyboardButton("📊 Статистика") },
            new[] { new KeyboardButton("👤 Пользовательские ошибки"), new KeyboardButton("🖥️ Серверные ошибки") },
            new[] { new KeyboardButton("⚠️ Предупреждения") }
        })
        {
            ResizeKeyboard = true
        };

        await _botClient.SendTextMessageAsync(chatId, "Выберите действие:", replyMarkup: keyboard, cancellationToken: token);
    }

    private async Task SendFilesAsync(ReportType reportType, CancellationToken token)
    {
        if (!Directory.Exists(_config.ReportsFolder))
        {
            await _botClient.SendTextMessageAsync(_config.ChatId, "Папка с отчётами не найдена.", cancellationToken: token);
            return;
        }

        var files = Directory.GetFiles(_config.ReportsFolder, "*.pdf");

        IEnumerable<string> filtered = reportType switch
        {
            ReportType.UserErrors => files.Where(f => f.Contains("user", StringComparison.OrdinalIgnoreCase)),
            ReportType.ServerErrors => files.Where(f => f.Contains("server", StringComparison.OrdinalIgnoreCase)),
            ReportType.Warnings => files.Where(f => f.Contains("warning", StringComparison.OrdinalIgnoreCase)),
            _ => files
        };

        if (!filtered.Any())
        {
            await _botClient.SendTextMessageAsync(_config.ChatId, "Файлы не найдены.", cancellationToken: token);
            return;
        }

        var failed = new List<string>();
        foreach (var file in filtered)
        {
            var ok = await SendPdfFileAsync(file, Path.GetFileName(file));
            if (!ok)
                failed.Add(Path.GetFileName(file));
        }

        if (failed.Any())
        {
            await _botClient.SendTextMessageAsync(_config.ChatId,
                $"Не удалось отправить: {string.Join(", ", failed)}",
                cancellationToken: token);
        }
        else
        {
            await _botClient.SendTextMessageAsync(_config.ChatId, "Готово.", cancellationToken: token);
        }
    }

    private async Task SendWeeklyStatisticsAsync(CancellationToken token)
    {
        var now = DateTime.Now;
        var files = Directory.Exists(_config.ReportsFolder)
            ? Directory.GetFiles(_config.ReportsFolder, "*.pdf")
            : Array.Empty<string>();

        var count = files.Count(f => System.IO.File.GetCreationTime(f) > now.AddDays(-7));

        await _botClient.SendTextMessageAsync(
            _config.ChatId,
            $"Отчёты за неделю: {count}",
            cancellationToken: token);
    }
}
