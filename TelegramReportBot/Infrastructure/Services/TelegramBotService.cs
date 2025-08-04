using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
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
using File = System.IO.File;

namespace TelegramReportBot.Infrastructure.Services;

public class TelegramBotService : ITelegramBotService
{
    private readonly ITelegramBotClient _botClient;
    private readonly BotConfiguration _config;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly ReceiverOptions _receiverOptions;
    private readonly HashSet<string> _sentFiles;
    private readonly string _sentFilesPath;


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

        _sentFilesPath = Path.IsPathRooted(_config.SentFilesDatabase)
            ? _config.SentFilesDatabase
            : Path.Combine(AppContext.BaseDirectory, _config.SentFilesDatabase);
        if (File.Exists(_sentFilesPath))
            _sentFiles = File.ReadAllLines(_sentFilesPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_sentFilesPath)!);
            _sentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
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

    public async Task<bool> SendPdfFileAsync(string filePath, string caption, int? threadId = null)
    {
        if (!File.Exists(filePath))
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
            await _botClient.SendDocumentAsync(
                _config.ChatId,
                inputFile,
                caption: caption,
                messageThreadId: threadId,
                cancellationToken: CancellationToken.None);
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
            case "📜 логи":
            case "логи":
                await SendLogFileAsync(token);
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
            new[] { new KeyboardButton("⚠️ Предупреждения"), new KeyboardButton("📜 Логи") }
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

        string GetFilter(string key) =>
            _config.FileFilters.TryGetValue(key, out var value) ? value : string.Empty;

        var userFilter = GetFilter(nameof(ReportType.UserErrors));
        var serverFilter = GetFilter(nameof(ReportType.ServerErrors));
        var warningFilter = GetFilter(nameof(ReportType.Warnings));

        IEnumerable<string> filtered = reportType switch
        {
            ReportType.UserErrors => files.Where(f => f.Contains(userFilter, StringComparison.OrdinalIgnoreCase)),
            ReportType.ServerErrors => files.Where(f => f.Contains(serverFilter, StringComparison.OrdinalIgnoreCase)),
            ReportType.Warnings => files.Where(f => f.Contains(warningFilter, StringComparison.OrdinalIgnoreCase)),
            _ => files
        };
        filtered = filtered.Where(f => !_sentFiles.Contains(Path.GetFileName(f))).ToList();

        if (!filtered.Any())
        {
            await _botClient.SendTextMessageAsync(_config.ChatId, "Новых файлов не найдено.", cancellationToken: token);
            return;

        }

        var failed = new List<string>();
        foreach (var file in filtered)
        {
            var threadId = GetThreadId(reportType, file, userFilter, serverFilter, warningFilter);
            var ok = await SendPdfFileAsync(file, Path.GetFileName(file), threadId);
            if (ok)
            {
                _sentFiles.Add(Path.GetFileName(file));
                await File.WriteAllLinesAsync(_sentFilesPath, _sentFiles, token);
            }
            else
            {
                failed.Add(Path.GetFileName(file));
            }
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

    private int? GetThreadId(ReportType type, string filePath, string userFilter, string serverFilter, string warningFilter)
    {
        return type switch
        {
            ReportType.UserErrors => _config.TopicIds.UserErrors,
            ReportType.ServerErrors => _config.TopicIds.ServerErrors,
            ReportType.Warnings => _config.TopicIds.Warnings,
            ReportType.All when !string.IsNullOrEmpty(userFilter) && filePath.Contains(userFilter, StringComparison.OrdinalIgnoreCase) => _config.TopicIds.UserErrors,
            ReportType.All when !string.IsNullOrEmpty(serverFilter) && filePath.Contains(serverFilter, StringComparison.OrdinalIgnoreCase) => _config.TopicIds.ServerErrors,
            ReportType.All when !string.IsNullOrEmpty(warningFilter) && filePath.Contains(warningFilter, StringComparison.OrdinalIgnoreCase) => _config.TopicIds.Warnings,
            _ => (int?)null
        };
    }

    public Task SendReportsAsync(CancellationToken token) => SendFilesAsync(ReportType.All, token);

    public async Task SendErrorReportsAsync(CancellationToken token)
    {
        await SendFilesAsync(ReportType.UserErrors, token);
        await SendFilesAsync(ReportType.ServerErrors, token);
    }

    public async Task SendLogFileAsync(CancellationToken token)
    {
        var logsDir = Path.Combine(AppContext.BaseDirectory, "Logs");
        if (!Directory.Exists(logsDir))
        {
            await _botClient.SendTextMessageAsync(_config.ChatId, "Логи отсутствуют.", cancellationToken: token);
            return;
        }

        var file = Directory.GetFiles(logsDir, "log-*.txt")
            .OrderByDescending(f => f)
            .FirstOrDefault();
        if (file == null)
        {
            await _botClient.SendTextMessageAsync(_config.ChatId, "Логи отсутствуют.", cancellationToken: token);
            return;
        }

        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var inputFile = InputFile.FromStream(stream, Path.GetFileName(file));
        await _botClient.SendDocumentAsync(_config.ChatId, inputFile, cancellationToken: token);
    }

    public async Task SendWeeklyStatisticsAsync(CancellationToken token)
    {
        var now = DateTime.Now;
        var files = Directory.Exists(_config.ReportsFolder)
            ? Directory.GetFiles(_config.ReportsFolder, "*.pdf")
            : Array.Empty<string>();

        var count = files.Count(f => File.GetCreationTime(f) > now.AddDays(-7));


        await _botClient.SendTextMessageAsync(
            _config.ChatId,
            $"Отчёты за неделю: {count}",
            cancellationToken: token);
    }
}
