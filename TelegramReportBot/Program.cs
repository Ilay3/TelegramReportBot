using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using TelegramReportBot.Models;
using TelegramReportBot.Models.Enum;
using TelegramReportBot.Services;
using TelegramReportBot.Services.Interface;

namespace TelegramReportBot
{
    /// <summary>
    /// Главный класс консольного приложения - Расширенная версия
    /// </summary>
    internal class Program
    {
        /// <summary>
        /// Точка входа в приложение
        /// </summary>
        static async Task Main(string[] args)
        {
            // Расширенная настройка Serilog с дополнительными sink'ами
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "TelegramReportBot")
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File("Logs/bot-log-.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .WriteTo.File("Logs/error-log-.txt",
                    restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 60)
                .CreateLogger();

            try
            {
                Log.Information("=== 🚀 ЗАПУСК TELEGRAM REPORT BOT v2.0 ===");
                Log.Information("Время запуска: {StartTime}", DateTime.Now);
                Log.Information("Версия .NET: {DotNetVersion}", Environment.Version);
                Log.Information("Операционная система: {OS}", Environment.OSVersion);

                // Создание и настройка хоста приложения
                var host = CreateHostBuilder(args).Build();

                // Регистрируем обработчик корректного завершения
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    Log.Information("Получен сигнал завершения работы (Ctrl+C)");
                };

                // Запуск приложения
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "💥 Критическая ошибка при запуске приложения");
                Environment.ExitCode = 1;
            }
            finally
            {
                Log.Information("=== 🏁 ЗАВЕРШЕНИЕ РАБОТЫ TELEGRAM REPORT BOT ===");
                Log.Information("Время завершения: {EndTime}", DateTime.Now);
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Создание и настройка хоста приложения
        /// </summary>
        static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog() // Используем Serilog для логирования
                .UseWindowsService() // Поддержка Windows Service
                .UseSystemd() // Поддержка systemd на Linux
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    // Настройка конфигурации с поддержкой разных окружений
                    var env = hostingContext.HostingEnvironment.EnvironmentName;

                    config.SetBasePath(Directory.GetCurrentDirectory())
                          .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                          .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
                          .AddEnvironmentVariables("TELEGRAM_BOT_")
                          .AddCommandLine(args);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    // Регистрируем конфигурацию
                    services.Configure<BotConfiguration>(
                        hostContext.Configuration.GetSection("BotConfiguration"));

                    services.Configure<PerformanceSettings>(
                        hostContext.Configuration.GetSection("PerformanceSettings"));

                    // Регистрируем основные сервисы
                    services.AddSingleton<ITelegramBotService, TelegramBotService>();
                    services.AddSingleton<IFileProcessingService, FileProcessingService>();
                    services.AddSingleton<IFileWatcherService, FileWatcherService>();
                    services.AddSingleton<IStatisticsService, StatisticsService>();
                    services.AddSingleton<IBackupService, BackupService>();
                    services.AddSingleton<ISecurityService, SecurityService>();

                    // Регистрируем hosted services
                    services.AddHostedService<BotHostedService>();
                    services.AddHostedService<StatisticsHostedService>();
                    services.AddHostedService<BackupHostedService>();
                });

        /// <summary>
        /// Главный hosted service для управления жизненным циклом бота
        /// </summary>
        public class BotHostedService : BackgroundService
        {
            private readonly ITelegramBotService _telegramService;
            private readonly IFileWatcherService _fileWatcherService;
            private readonly IFileProcessingService _fileProcessingService;
            private readonly IStatisticsService _statisticsService;
            private readonly ISecurityService _securityService;
            private readonly ILogger<BotHostedService> _logger;

            public BotHostedService(
                ITelegramBotService telegramService,
                IFileWatcherService fileWatcherService,
                IFileProcessingService fileProcessingService,
                IStatisticsService statisticsService,
                ISecurityService securityService,
                ILogger<BotHostedService> logger)
            {
                _telegramService = telegramService;
                _fileWatcherService = fileWatcherService;
                _fileProcessingService = fileProcessingService;
                _statisticsService = statisticsService;
                _securityService = securityService;
                _logger = logger;
            }

            /// <summary>
            /// Запуск всех сервисов
            /// </summary>
            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                try
                {
                    _logger.LogInformation("🔄 Инициализация всех сервисов бота...");

                    // Проверяем безопасность перед запуском
                    await _securityService.PerformSecurityCheckAsync();

                    // Подписываемся на события
                    _telegramService.ManualDistributionRequested += OnManualDistributionRequested;
                    _telegramService.StatisticsRequested += OnStatisticsRequested;
                    _telegramService.AdminCommandReceived += OnAdminCommandReceived;

                    // Запускаем сервисы в правильном порядке
                    _logger.LogInformation("📊 Инициализация сервиса статистики...");
                    await _statisticsService.InitializeAsync();

                    _logger.LogInformation("🤖 Запуск Telegram-бота...");
                    await _telegramService.StartAsync(stoppingToken);

                    _logger.LogInformation("📁 Запуск мониторинга файлов...");
                    await _fileWatcherService.StartAsync(stoppingToken);

                    _logger.LogInformation("✅ Все сервисы успешно запущены!");
                    _logger.LogInformation("📱 Управление через Telegram: /reports, /status, /admin");
                    _logger.LogInformation("🔍 Мониторинг активен. Ожидание файлов...");

                    // Уведомляем о запуске
                    await _telegramService.SendStartupNotificationAsync();

                    // Основной цикл работы
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                        // Периодическая проверка здоровья системы
                        await PerformHealthCheckAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("⏹️ Получен сигнал остановки приложения");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "💥 Критическая ошибка в основном цикле бота");

                    // Уведомляем администраторов об ошибке
                    try
                    {
                        await _telegramService.SendErrorNotificationAsync(ex);
                    }
                    catch
                    {
                        // Игнорируем ошибки отправки уведомлений
                    }

                    throw;
                }
            }

            /// <summary>
            /// Остановка всех сервисов
            /// </summary>
            public override async Task StopAsync(CancellationToken cancellationToken)
            {
                _logger.LogInformation("🔄 Остановка всех сервисов...");

                try
                {
                    // Уведомляем о завершении работы
                    await _telegramService.SendShutdownNotificationAsync();

                    // Отписываемся от событий
                    _telegramService.ManualDistributionRequested -= OnManualDistributionRequested;
                    _telegramService.StatisticsRequested -= OnStatisticsRequested;
                    _telegramService.AdminCommandReceived -= OnAdminCommandReceived;

                    // Останавливаем сервисы в обратном порядке
                    await _fileWatcherService.StopAsync(cancellationToken);
                    await _telegramService.StopAsync(cancellationToken);
                    await _statisticsService.SaveStatisticsAsync();

                    _logger.LogInformation("✅ Все сервисы корректно остановлены");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Ошибка при остановке сервисов");
                }

                await base.StopAsync(cancellationToken);
            }

            /// <summary>
            /// Обработка запроса ручной рассылки
            /// </summary>
            private async Task OnManualDistributionRequested(ReportType reportType, string userId)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                _logger.LogInformation("📤 Запуск ручной рассылки типа {ReportType} пользователем {UserId}",
                    reportType, userId);

                try
                {
                    // Записываем статистику
                    _statisticsService.RecordManualDistribution(reportType, userId);

                    int processedFiles;
                    switch (reportType)
                    {
                        case ReportType.All:
                            processedFiles = await _fileProcessingService.ProcessAllNewFilesAsync();
                            break;
                        default:
                            processedFiles = await _fileProcessingService.ProcessFilesByTypeAsync(reportType);
                            break;
                    }

                    stopwatch.Stop();

                    _logger.LogInformation("✅ Ручная рассылка {ReportType} завершена. " +
                        "Обработано файлов: {ProcessedFiles}, Время: {Duration}ms",
                        reportType, processedFiles, stopwatch.ElapsedMilliseconds);

                    // Обновляем статистику
                    _statisticsService.RecordDistributionCompleted(processedFiles, stopwatch.Elapsed);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Ошибка при выполнении ручной рассылки {ReportType}", reportType);
                    _statisticsService.RecordDistributionError(reportType, ex.Message);
                }
            }

            /// <summary>
            /// Обработка запроса статистики
            /// </summary>
            private async Task<StatisticsReport> OnStatisticsRequested(string userId)
            {
                _logger.LogInformation("📊 Запрос статистики от пользователя {UserId}", userId);

                try
                {
                    var report = await _statisticsService.GenerateReportAsync();
                    _statisticsService.RecordStatisticsRequest(userId);
                    return report;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Ошибка при генерации статистики");
                    throw;
                }
            }

            /// <summary>
            /// Обработка административных команд
            /// </summary>
            private async Task OnAdminCommandReceived(AdminCommand command, string userId)
            {
                _logger.LogInformation("⚙️ Получена админ-команда {Command} от {UserId}", command, userId);

                try
                {
                    // Проверяем права администратора
                    if (!await _securityService.IsAdminUserAsync(userId))
                    {
                        _logger.LogWarning("🚫 Попытка выполнения админ-команды неавторизованным пользователем {UserId}", userId);
                        return;
                    }

                    switch (command)
                    {
                        case AdminCommand.ClearSentFiles:
                            await _fileProcessingService.ClearSentFilesAsync();
                            break;
                        case AdminCommand.ForceBackup:
                            // Реализация принудительного бэкапа
                            break;
                        case AdminCommand.ReloadConfig:
                            // Реализация перезагрузки конфигурации
                            break;
                    }

                    _statisticsService.RecordAdminAction(command, userId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Ошибка выполнения админ-команды {Command}", command);
                }
            }

            /// <summary>
            /// Проверка здоровья системы
            /// </summary>
            private async Task PerformHealthCheckAsync()
            {
                try
                {
                    // Проверяем доступность папки
                    var healthStatus = await _fileProcessingService.GetHealthStatusAsync();

                    if (!healthStatus.IsHealthy)
                    {
                        _logger.LogWarning("⚠️ Обнаружены проблемы в системе: {Issues}",
                            string.Join(", ", healthStatus.Issues));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Ошибка при проверке здоровья системы");
                }
            }
        }

        /// <summary>
        /// Hosted service для периодической генерации статистики
        /// </summary>
        public class StatisticsHostedService : BackgroundService
        {
            private readonly IStatisticsService _statisticsService;
            private readonly ILogger<StatisticsHostedService> _logger;

            public StatisticsHostedService(
                IStatisticsService statisticsService,
                ILogger<StatisticsHostedService> logger)
            {
                _statisticsService = statisticsService;
                _logger = logger;
            }

            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        // Сохраняем статистику каждые 10 минут
                        await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                        await _statisticsService.SaveStatisticsAsync();

                        _logger.LogDebug("📊 Статистика сохранена");
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Ошибка в сервисе статистики");
                    }
                }
            }
        }

        /// <summary>
        /// Hosted service для автоматического резервного копирования
        /// </summary>
        public class BackupHostedService : BackgroundService
        {
            private readonly IBackupService _backupService;
            private readonly ILogger<BackupHostedService> _logger;

            public BackupHostedService(
                IBackupService backupService,
                ILogger<BackupHostedService> logger)
            {
                _backupService = backupService;
                _logger = logger;
            }

            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        // Создаём резервную копию каждые 24 часа
                        await Task.Delay(TimeSpan.FromHours(24), stoppingToken);

                        _logger.LogInformation("💾 Создание автоматической резервной копии...");
                        await _backupService.CreateBackupAsync();
                        _logger.LogInformation("✅ Резервная копия создана успешно");
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Ошибка создания резервной копии");
                    }
                }
            }
        }
    }
}