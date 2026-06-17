using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.AiScheduledTasks;

public sealed record AiScheduledTaskDto(string AiScheduledTaskId, string OwnerUserId, string Name, string Prompt, bool Enabled, AiScheduledTaskScheduleKind ScheduleKind, DateTimeOffset? RunOnceAtUtc, TimeOnly? TimeOfDayLocal, DayOfWeek? DayOfWeek, int? DayOfMonth, string TimeZoneId, AiScheduledTaskDeliveryTarget DeliveryTarget, DateTimeOffset? NextRunAtUtc, DateTimeOffset? LastRunAtUtc, AiScheduledTaskLastStatus LastStatus, string? LastError, string? LastResponsePreview, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed class SaveAiScheduledTaskCommand
{
    public string? AiScheduledTaskId { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public AiScheduledTaskScheduleKind ScheduleKind { get; set; }
    public DateTimeOffset? RunOnceAtUtc { get; set; }
    public TimeOnly? TimeOfDayLocal { get; set; }
    public DayOfWeek? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public AiScheduledTaskDeliveryTarget DeliveryTarget { get; set; } = AiScheduledTaskDeliveryTarget.TelegramOwner;
}

public sealed record AiScheduledTaskMutationResult(bool Succeeded, string? ErrorMessage, AiScheduledTaskDto? Task);

public interface IAiScheduledTaskService
{
    Task<IReadOnlyList<AiScheduledTaskDto>> ListForUserAsync(string ownerUserId, CancellationToken cancellationToken);
    Task<AiScheduledTaskDto?> GetForUserAsync(string ownerUserId, string taskId, CancellationToken cancellationToken);
    Task<AiScheduledTaskMutationResult> SaveAsync(SaveAiScheduledTaskCommand command, CancellationToken cancellationToken);
    Task<AiScheduledTaskMutationResult> DeleteAsync(string ownerUserId, string taskId, CancellationToken cancellationToken);
    DateTimeOffset? CalculateNextRunUtc(AiScheduledTaskScheduleKind kind, DateTimeOffset? runOnceAtUtc, TimeOnly? timeOfDayLocal, DayOfWeek? dayOfWeek, int? dayOfMonth, string timeZoneId, DateTimeOffset nowUtc);
}
