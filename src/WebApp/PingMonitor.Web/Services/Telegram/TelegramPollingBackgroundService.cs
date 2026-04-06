using PingMonitor.Web.Services.StartupGate;

namespace PingMonitor.Web.Services.Telegram;

internal sealed class TelegramPollingBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollLoopDelay = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStartupGateRuntimeState _startupGateRuntimeState;
    private readonly ILogger<TelegramPollingBackgroundService> _logger;

    public TelegramPollingBackgroundService(
        IServiceScopeFactory scopeFactory,
        IStartupGateRuntimeState startupGateRuntimeState,
        ILogger<TelegramPollingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _startupGateRuntimeState = startupGateRuntimeState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telegram polling background service started.");

        var wasBlockedByStartupGate = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_startupGateRuntimeState.CurrentMode != StartupMode.Normal)
            {
                if (!wasBlockedByStartupGate)
                {
                    _logger.LogInformation("Telegram polling is paused because Startup Gate is active.");
                    wasBlockedByStartupGate = true;
                }

                try
                {
                    await Task.Delay(PollLoopDelay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                continue;
            }

            if (wasBlockedByStartupGate)
            {
                _logger.LogInformation("Startup Gate is cleared. Telegram polling is resuming normal operation.");
                wasBlockedByStartupGate = false;
            }

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
                await Task.Delay(PollLoopDelay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Telegram polling background service stopped.");
    }
}
