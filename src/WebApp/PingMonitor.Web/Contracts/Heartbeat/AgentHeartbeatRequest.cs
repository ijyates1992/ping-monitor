using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.Contracts.Heartbeat;

public sealed class AgentHeartbeatRequest
{
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string AgentVersion { get; init; } = string.Empty;

    [Required]
    public DateTimeOffset SentAtUtc { get; init; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string ConfigVersion { get; init; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int ActiveAssignments { get; init; }

    [Range(0, int.MaxValue)]
    public int QueuedResultCount { get; init; }

    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string Status { get; init; } = string.Empty;
}
