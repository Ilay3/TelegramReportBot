using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TelegramReportBot.Core.Interfaces;
using TelegramReportBot.Core.Models.Configuration;
using TelegramReportBot.Infrastructure.Services;

namespace TelegramReportBot;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.Configure<BotConfiguration>(
                    context.Configuration.GetSection("BotConfiguration"));

                services.AddSingleton<ITelegramBotService, TelegramBotService>();
                services.AddHostedService<BotHostedService>();
            })
            .Build();

        await host.RunAsync();
    }
}

public class BotHostedService : IHostedService
{
    private readonly ITelegramBotService _bot;

    public BotHostedService(ITelegramBotService bot)
    {
        _bot = bot;
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _bot.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        _bot.StopAsync(cancellationToken);
}
