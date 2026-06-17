namespace PingMonitor.Web.Models;

public enum AiEventTriggerType { EndpointStateChanged = 0, AgentStateChanged = 1 }
public enum AiEventTriggerScopeMode { AllVisibleEndpoints = 0, SelectedEndpoints = 1, SelectedEndpointGroups = 2, EndpointsAssignedToSelectedAgents = 3, AllVisibleAgents = 4, SelectedAgents = 5 }
public enum AiEventTriggerRateLimitUnit { Minutes = 0, Hours = 1, Days = 2 }
public enum AiEventTriggeredTaskDeliveryTarget { TelegramOwner = 0 }
public enum AiEventTriggeredTaskRunStatus { Pending = 0, Running = 1, Succeeded = 2, Failed = 3, RateLimited = 4, Skipped = 5 }

public sealed class AiEventTriggeredTask
{
    public const int IdMaxLength = 64;
    public const int OwnerUserIdMaxLength = 255;
    public const int NameMaxLength = 128;
    public const int PromptMaxLength = 4000;
    public const int TargetStatesJsonMaxLength = 512;
    public const int ScopeSelectionJsonMaxLength = 4096;
    public const int LastErrorMaxLength = 512;
    public const int LastResponsePreviewMaxLength = 1000;

    public string AiEventTriggeredTaskId { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public AiEventTriggerType TriggerType { get; set; }
    public string EndpointTargetStatesJson { get; set; } = "[]";
    public string AgentTargetStatesJson { get; set; } = "[]";
    public AiEventTriggerScopeMode ScopeMode { get; set; }
    public string ScopeSelectionJson { get; set; } = "[]";
    public int RateLimitValue { get; set; } = 30;
    public AiEventTriggerRateLimitUnit RateLimitUnit { get; set; } = AiEventTriggerRateLimitUnit.Minutes;
    public AiEventTriggeredTaskDeliveryTarget DeliveryTarget { get; set; } = AiEventTriggeredTaskDeliveryTarget.TelegramOwner;
    public DateTimeOffset? LastTriggeredAtUtc { get; set; }
    public DateTimeOffset? LastRunAtUtc { get; set; }
    public DateTimeOffset? LastSucceededAtUtc { get; set; }
    public DateTimeOffset? LastFailedAtUtc { get; set; }
    public DateTimeOffset? LastRateLimitedAtUtc { get; set; }
    public int RateLimitedCount { get; set; }
    public AiEventTriggeredTaskRunStatus LastStatus { get; set; } = AiEventTriggeredTaskRunStatus.Pending;
    public string? LastError { get; set; }
    public string? LastResponsePreview { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class AiEventTriggeredTaskRun
{
    public const int IdMaxLength = 64;
    public const int TaskIdMaxLength = 64;
    public const int TriggerContextJsonMaxLength = 8192;
    public const int ErrorMaxLength = 512;
    public string AiEventTriggeredTaskRunId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public AiEventTriggerType TriggerType { get; set; }
    public string TriggerContextJson { get; set; } = string.Empty;
    public AiEventTriggeredTaskRunStatus Status { get; set; } = AiEventTriggeredTaskRunStatus.Pending;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? Error { get; set; }
}
