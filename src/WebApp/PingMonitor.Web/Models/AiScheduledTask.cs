namespace PingMonitor.Web.Models;

public enum AiScheduledTaskScheduleKind { Once = 0, Daily = 1, Weekly = 2, Monthly = 3 }
public enum AiScheduledTaskDeliveryTarget { TelegramOwner = 0 }
public enum AiScheduledTaskLastStatus { NeverRun = 0, Running = 1, Succeeded = 2, Failed = 3, Disabled = 4, Completed = 5 }

public sealed class AiScheduledTask
{
    public const int IdMaxLength = 64;
    public const int OwnerUserIdMaxLength = 255;
    public const int NameMaxLength = 128;
    public const int PromptMaxLength = 4000;
    public const int TimeZoneIdMaxLength = 128;
    public const int LastErrorMaxLength = 512;
    public const int LastResponsePreviewMaxLength = 1000;

    public string AiScheduledTaskId { get; set; } = string.Empty;
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
    public DateTimeOffset? NextRunAtUtc { get; set; }
    public DateTimeOffset? LastRunAtUtc { get; set; }
    public DateTimeOffset? LastSucceededAtUtc { get; set; }
    public DateTimeOffset? LastFailedAtUtc { get; set; }
    public AiScheduledTaskLastStatus LastStatus { get; set; } = AiScheduledTaskLastStatus.NeverRun;
    public string? LastError { get; set; }
    public string? LastResponsePreview { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
