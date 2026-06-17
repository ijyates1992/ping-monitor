using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.AiScheduledTasks;

public sealed record AiScheduledTaskDto(string AiScheduledTaskId, string OwnerUserId, string Name, string Prompt, bool Enabled, DateTimeOffset? FirstRunAtUtc, bool RepeatEnabled, int? RepeatEvery, AiScheduledTaskRepeatUnit? RepeatUnit, AiScheduledTaskMissedRunPolicy MissedRunPolicy, string TimeZoneId, AiScheduledTaskDeliveryTarget DeliveryTarget, DateTimeOffset? NextRunAtUtc, DateTimeOffset? LastRunAtUtc, AiScheduledTaskLastStatus LastStatus, string? LastError, string? LastResponsePreview, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc)
{
    public string RepeatSummary => RepeatEnabled && RepeatEvery is not null && RepeatUnit is not null ? $"Every {RepeatEvery} {RepeatUnit}" : "One-off";
}

public sealed class SaveAiScheduledTaskCommand
{
    public string? AiScheduledTaskId { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public DateTimeOffset? FirstRunAtUtc { get; set; }
    public bool RepeatEnabled { get; set; }
    public int? RepeatEvery { get; set; }
    public AiScheduledTaskRepeatUnit? RepeatUnit { get; set; }
    public AiScheduledTaskMissedRunPolicy MissedRunPolicy { get; set; } = AiScheduledTaskMissedRunPolicy.Skip;
    public string TimeZoneId { get; set; } = "UTC";
    public AiScheduledTaskDeliveryTarget DeliveryTarget { get; set; } = AiScheduledTaskDeliveryTarget.TelegramOwner;
}

public sealed record AiScheduledTaskMutationResult(bool Succeeded, string? ErrorMessage, AiScheduledTaskDto? Task);
public sealed record AiScheduledTaskDueDecision(bool ShouldRunNow, DateTimeOffset? NextRunAtUtc, bool DisableTask, AiScheduledTaskLastStatus? FinalStatus);

public interface IAiScheduledTaskService
{
    Task<IReadOnlyList<AiScheduledTaskDto>> ListForUserAsync(string ownerUserId, CancellationToken cancellationToken);
    Task<AiScheduledTaskDto?> GetForUserAsync(string ownerUserId, string taskId, CancellationToken cancellationToken);
    Task<AiScheduledTaskMutationResult> SaveAsync(SaveAiScheduledTaskCommand command, CancellationToken cancellationToken);
    Task<AiScheduledTaskMutationResult> DeleteAsync(string ownerUserId, string taskId, CancellationToken cancellationToken);
    DateTimeOffset? CalculateNextRunUtc(DateTimeOffset firstRunAtUtc, bool repeatEnabled, int? repeatEvery, AiScheduledTaskRepeatUnit? repeatUnit, string timeZoneId, DateTimeOffset nowUtc);
    AiScheduledTaskDueDecision EvaluateDue(DateTimeOffset firstRunAtUtc, bool repeatEnabled, int? repeatEvery, AiScheduledTaskRepeatUnit? repeatUnit, AiScheduledTaskMissedRunPolicy missedRunPolicy, string timeZoneId, DateTimeOffset nowUtc);
}
