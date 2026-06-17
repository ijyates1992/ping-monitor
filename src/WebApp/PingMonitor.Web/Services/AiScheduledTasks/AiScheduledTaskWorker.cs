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
            var scheduler = scope.ServiceProvider.GetRequiredService<IAiScheduledTaskService>();
            if (task.FirstRunAtUtc is null)
            {
                task.Enabled = false;
                task.NextRunAtUtc = null;
                task.LastStatus = AiScheduledTaskLastStatus.Disabled;
                task.UpdatedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var dueDecision = scheduler.EvaluateDue(task.FirstRunAtUtc.Value, task.RepeatEnabled, task.RepeatEvery, task.RepeatUnit, task.MissedRunPolicy, task.TimeZoneId, now);
            var isStaleMissedRun = task.MissedRunPolicy == AiScheduledTaskMissedRunPolicy.Skip && task.NextRunAtUtc < now.AddMinutes(-2);
            if (!dueDecision.ShouldRunNow && (isStaleMissedRun || dueDecision.DisableTask))
            {
                task.NextRunAtUtc = dueDecision.NextRunAtUtc;
                if (dueDecision.DisableTask) task.Enabled = false;
                if (dueDecision.FinalStatus is not null) task.LastStatus = dueDecision.FinalStatus.Value;
                task.UpdatedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            task.LastStatus = AiScheduledTaskLastStatus.Running; task.LastRunAtUtc = now; task.UpdatedAtUtc = now; await db.SaveChangesAsync(cancellationToken);

            try
            {
                var chat = scope.ServiceProvider.GetRequiredService<IAiChatService>();
                var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, task.OwnerUserId)], "ScheduledAiTask"));
                var response = await chat.SendAsync(new AiChatRequest { Source = AiChatSource.ScheduledTask, UserMessage = task.Prompt, ApplicationUserId = task.OwnerUserId, Principal = principal }, cancellationToken);
                if (!response.Succeeded || string.IsNullOrWhiteSpace(response.AssistantMessage)) throw new InvalidOperationException(response.ErrorMessage ?? "AI provider did not return a scheduled task response.");
                var telegram = scope.ServiceProvider.GetRequiredService<ITelegramDirectMessageSender>();
                var send = await telegram.SendToUserAsync(task.OwnerUserId, $"Ping Monitor scheduled AI task: {task.Name}\n\n{response.AssistantMessage}", cancellationToken);
                if (!send.Succeeded) throw new InvalidOperationException(send.Message);
                task.LastStatus = task.RepeatEnabled ? AiScheduledTaskLastStatus.Succeeded : AiScheduledTaskLastStatus.Completed;
                task.LastSucceededAtUtc = DateTimeOffset.UtcNow; task.LastError = null; task.LastResponsePreview = Truncate(response.AssistantMessage, AiScheduledTask.LastResponsePreviewMaxLength);
                task.Enabled = task.RepeatEnabled;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scheduled AI task {TaskId} failed for owner {OwnerUserId}.", task.AiScheduledTaskId, task.OwnerUserId);
                task.LastStatus = AiScheduledTaskLastStatus.Failed; task.LastFailedAtUtc = DateTimeOffset.UtcNow; task.LastError = Truncate(ex.Message, AiScheduledTask.LastErrorMaxLength);
            }
            if (task.FirstRunAtUtc is not null)
            {
                var due = scheduler.EvaluateDue(task.FirstRunAtUtc.Value, task.RepeatEnabled, task.RepeatEvery, task.RepeatUnit, task.MissedRunPolicy, task.TimeZoneId, DateTimeOffset.UtcNow);
                task.NextRunAtUtc = task.Enabled ? due.NextRunAtUtc : null;
            }
            else
            {
                task.NextRunAtUtc = null;
                task.Enabled = false;
            }
            task.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { _concurrency.Release(); }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
