namespace PingMonitor.Web.Services.Telegram;

internal sealed class TelegramPollingBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramPollingBackgroundService> _logger;

    public TelegramPollingBackgroundService(IServiceScopeFactory scopeFactory, ILogger<TelegramPollingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telegram polling background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var polling = scope.ServiceProvider.GetRequiredService<ITelegramPollingService>();
                await polling.PollOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram polling cycle failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Telegram polling background service stopped.");
    }
}
