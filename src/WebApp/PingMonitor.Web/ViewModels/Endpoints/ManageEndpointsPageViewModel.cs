using System.ComponentModel.DataAnnotations;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.ViewModels.Endpoints;

public sealed class ManageEndpointsPageViewModel
{
    public IReadOnlyList<ManageEndpointRowViewModel> Rows { get; init; } = [];
    public string? GroupId { get; set; }
    public IReadOnlyList<EndpointGroupOptionViewModel> AvailableGroups { get; init; } = [];
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class ManageEndpointRowViewModel
{
    public string AssignmentId { get; init; } = string.Empty;
    public string EndpointId { get; init; } = string.Empty;
    public string EndpointName { get; init; } = string.Empty;
    public string IconKey { get; init; } = "generic";
    public string Target { get; init; } = string.Empty;
    public string AgentDisplay { get; init; } = string.Empty;
    public IReadOnlyList<string> DependencyEndpointNames { get; init; } = [];
    public IReadOnlyList<string> DependencyChildEndpointNames { get; init; } = [];
    public IReadOnlyList<string> GroupNames { get; init; } = [];
    public bool EndpointEnabled { get; init; }
    public bool AssignmentEnabled { get; init; }
    public int PingIntervalSeconds { get; init; }
    public int RetryIntervalSeconds { get; init; }
    public int TimeoutMs { get; init; }
    public int FailureThreshold { get; init; }
    public int RecoveryThreshold { get; init; }
    public EndpointStateKind CurrentState { get; init; } = EndpointStateKind.Unknown;
    public double? LastRttMs { get; init; }
    public double? AverageRttMs { get; init; }
    public double? HighestRttMs { get; init; }
    public double? LowestRttMs { get; init; }
    public double? JitterMs { get; init; }
}

public sealed class EditEndpointPageViewModel
{
    [Required]
    public string AssignmentId { get; set; } = string.Empty;

    [Required]
    public string EndpointId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Endpoint name is required.")]
    [Display(Name = "Endpoint name")]
    public string EndpointName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Target is required.")]
    [Display(Name = "Target")]
    public string Target { get; set; } = string.Empty;

    [Display(Name = "Icon")]
    public string IconKey { get; set; } = "generic";

    [Required(ErrorMessage = "Agent selection is required.")]
    [Display(Name = "Agent")]
    public string AgentId { get; set; } = string.Empty;

    [Display(Name = "Depends on endpoints")]
    public List<string> DependsOnEndpointIds { get; set; } = [];

    [Display(Name = "Groups")]
    public List<string> GroupIds { get; set; } = [];

    [Display(Name = "Endpoint enabled")]
    public bool EndpointEnabled { get; set; }

    [Display(Name = "Assignment enabled")]
    public bool AssignmentEnabled { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Ping interval must be at least 1 second.")]
    [Display(Name = "Ping interval (seconds)")]
    public int PingIntervalSeconds { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Retry interval must be at least 1 second.")]
    [Display(Name = "Retry interval (seconds)")]
    public int RetryIntervalSeconds { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Timeout must be at least 1 millisecond.")]
    [Display(Name = "Timeout (ms)")]
    public int TimeoutMs { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Failure threshold must be at least 1.")]
    [Display(Name = "Failure threshold")]
    public int FailureThreshold { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Recovery threshold must be at least 1.")]
    [Display(Name = "Recovery threshold")]
    public int RecoveryThreshold { get; set; }

    public IReadOnlyList<EndpointAgentOptionViewModel> AvailableAgents { get; set; } = [];
    public IReadOnlyList<EndpointDependencyOptionViewModel> AvailableDependencies { get; set; } = [];
    public IReadOnlyList<EndpointGroupOptionViewModel> AvailableGroups { get; set; } = [];
    public IReadOnlyList<EndpointIconOptionViewModel> AvailableIcons { get; set; } = [];
}

public sealed class EndpointAgentOptionViewModel
{
    public string AgentId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public sealed class EndpointDependencyOptionViewModel
{
    public string EndpointId { get; init; } = string.Empty;
    public string EndpointName { get; init; } = string.Empty;
}

public sealed class EditEndpointOptionsViewModel
{
    public IReadOnlyList<EndpointAgentOptionViewModel> Agents { get; init; } = [];
    public IReadOnlyList<EndpointDependencyOptionViewModel> Dependencies { get; init; } = [];
    public IReadOnlyList<EndpointGroupOptionViewModel> Groups { get; init; } = [];
}

public sealed class RemoveEndpointPageViewModel
{
    public string AssignmentId { get; init; } = string.Empty;
    public string EndpointId { get; init; } = string.Empty;
    public string EndpointName { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string AgentDisplay { get; init; } = string.Empty;
    public string ConfirmationText { get; set; } = string.Empty;
    public string RequiredConfirmationText { get; init; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
