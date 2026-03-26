namespace PingMonitor.Web.ViewModels.Agents;

public sealed class ManageAgentsPageViewModel
{
    public required IReadOnlyList<ManageAgentRowViewModel> Agents { get; init; }
    public string? StatusMessage { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class ManageAgentRowViewModel
{
    public required string AgentId { get; init; }
    public required string Name { get; init; }
    public required string InstanceId { get; init; }
    public required bool Enabled { get; init; }
    public required bool ApiKeyRevoked { get; init; }
    public required DateTimeOffset? LastSeenUtc { get; init; }
    public required DateTimeOffset? LastHeartbeatUtc { get; init; }
    public required string AgentVersion { get; init; }
    public required string MachineName { get; init; }
    public required string Platform { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required int AssignmentCount { get; init; }
    public required double? UptimePercent { get; init; }
}

public sealed class RemoveAgentPageViewModel
{
    public string AgentId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public int AssignmentCount { get; init; }
    public string ConfirmationText { get; set; } = string.Empty;
    public string RequiredConfirmationText { get; init; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
