using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using TelegramReportBot.Core.Interfaces;
using TelegramReportBot.Core.Models.Configuration;

namespace TelegramReportBot.Infrastructure.Services;

/// <summary>
/// Сервис планировщика, запускающий задачи по расписанию
/// </summary>
public class ScheduleService : BackgroundService
{
    private readonly ITelegramBotService _botService;
    private readonly ScheduleSettings _schedule;
    private readonly ILogger<ScheduleService> _logger;

    public ScheduleService(IOptions<BotConfiguration> config, ITelegramBotService botService, ILogger<ScheduleService> logger)
    {
        _botService = botService;
        _logger = logger;
        _schedule = config.Value.Schedule;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CronExpression? statsCron = string.IsNullOrWhiteSpace(_schedule.WeeklyStatisticsCron)
            ? null
            : CronExpression.Parse(_schedule.WeeklyStatisticsCron);
        CronExpression? reportsCron = string.IsNullOrWhiteSpace(_schedule.ReportsCron)
            ? null
            : CronExpression.Parse(_schedule.ReportsCron);
        CronExpression? errorCron = string.IsNullOrWhiteSpace(_schedule.ErrorReportsCron)
            ? null
            : CronExpression.Parse(_schedule.ErrorReportsCron);

        if (statsCron == null && reportsCron == null)
        {
            _logger.LogInformation("Планировщик не настроен");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            var nextStats = statsCron?.GetNextOccurrence(now, TimeZoneInfo.Local);
            var nextReports = reportsCron?.GetNextOccurrence(now, TimeZoneInfo.Local);
            var nextErrors = errorCron?.GetNextOccurrence(now, TimeZoneInfo.Local);

            var nextRuns = new[] { nextStats, nextReports, nextErrors }
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();

            if (!nextRuns.Any())
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            var next = nextRuns.Min();
            var delay = next - now;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, stoppingToken);

            if (nextStats.HasValue && next == nextStats.Value)
            {
                _logger.LogInformation("Запуск плановой отправки статистики");
                await _botService.SendWeeklyStatisticsAsync(stoppingToken);
            }

            if (nextReports.HasValue && next == nextReports.Value)
            {
                _logger.LogInformation("Запуск плановой рассылки отчётов");
                await _botService.SendReportsAsync(stoppingToken);
            }

            if (nextErrors.HasValue && next == nextErrors.Value)
            {
                _logger.LogInformation("Запуск плановой рассылки отчётов об ошибках");
                await _botService.SendErrorReportsAsync(stoppingToken);
            }
        }
    }
}
