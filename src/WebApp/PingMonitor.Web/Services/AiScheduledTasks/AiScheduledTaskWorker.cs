using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.AiChat;
using PingMonitor.Web.Services.StartupGate;
using PingMonitor.Web.Services.Telegram;

namespace PingMonitor.Web.Services.AiScheduledTasks;

internal sealed class AiScheduledTaskWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStartupGateRuntimeState _startupGate;
    private readonly ILogger<AiScheduledTaskWorker> _logger;
    private readonly SemaphoreSlim _concurrency = new(1, 1);

    public AiScheduledTaskWorker(IServiceScopeFactory scopeFactory, IStartupGateRuntimeState startupGate, ILogger<AiScheduledTaskWorker> logger)
    { _scopeFactory = scopeFactory; _startupGate = startupGate; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_startupGate.IsOperationalMode) await RunDueTasksAsync(stoppingToken);
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    internal async Task RunDueTasksAsync(CancellationToken cancellationToken)
    {
        if (!_startupGate.IsOperationalMode) return;
        if (!await _concurrency.WaitAsync(0, cancellationToken)) return;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PingMonitorDbContext>();
            var now = DateTimeOffset.UtcNow;
            var task = await db.AiScheduledTasks.Where(x => x.Enabled && x.NextRunAtUtc != null && x.NextRunAtUtc <= now && x.LastStatus != AiScheduledTaskLastStatus.Running).OrderBy(x => x.NextRunAtUtc).FirstOrDefaultAsync(cancellationToken);
            if (task is null) return;
            task.LastStatus = AiScheduledTaskLastStatus.Running; task.LastRunAtUtc = now; task.UpdatedAtUtc = now; await db.SaveChangesAsync(cancellationToken);

            var scheduler = scope.ServiceProvider.GetRequiredService<IAiScheduledTaskService>();
            try
            {
                var chat = scope.ServiceProvider.GetRequiredService<IAiChatService>();
                var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, task.OwnerUserId)], "ScheduledAiTask"));
                var response = await chat.SendAsync(new AiChatRequest { Source = AiChatSource.ScheduledTask, UserMessage = task.Prompt, ApplicationUserId = task.OwnerUserId, Principal = principal }, cancellationToken);
                if (!response.Succeeded || string.IsNullOrWhiteSpace(response.AssistantMessage)) throw new InvalidOperationException(response.ErrorMessage ?? "AI provider did not return a scheduled task response.");
                var telegram = scope.ServiceProvider.GetRequiredService<ITelegramDirectMessageSender>();
                var send = await telegram.SendToUserAsync(task.OwnerUserId, $"Ping Monitor scheduled AI task: {task.Name}\n\n{response.AssistantMessage}", cancellationToken);
                if (!send.Succeeded) throw new InvalidOperationException(send.Message);
                task.LastStatus = task.ScheduleKind == AiScheduledTaskScheduleKind.Once ? AiScheduledTaskLastStatus.Completed : AiScheduledTaskLastStatus.Succeeded;
                task.LastSucceededAtUtc = DateTimeOffset.UtcNow; task.LastError = null; task.LastResponsePreview = Truncate(response.AssistantMessage, AiScheduledTask.LastResponsePreviewMaxLength);
                task.Enabled = task.ScheduleKind != AiScheduledTaskScheduleKind.Once;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scheduled AI task {TaskId} failed for owner {OwnerUserId}.", task.AiScheduledTaskId, task.OwnerUserId);
                task.LastStatus = AiScheduledTaskLastStatus.Failed; task.LastFailedAtUtc = DateTimeOffset.UtcNow; task.LastError = Truncate(ex.Message, AiScheduledTask.LastErrorMaxLength);
            }
            task.NextRunAtUtc = task.Enabled ? scheduler.CalculateNextRunUtc(task.ScheduleKind, task.RunOnceAtUtc, task.TimeOfDayLocal, task.DayOfWeek, task.DayOfMonth, task.TimeZoneId, DateTimeOffset.UtcNow) : null;
            task.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { _concurrency.Release(); }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
