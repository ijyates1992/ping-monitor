using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Endpoints;

public sealed class CreateEndpointPageViewModel
{
    [Required(ErrorMessage = "Endpoint name is required.")]
    [Display(Name = "Endpoint name")]
    public string EndpointName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Target is required.")]
    [Display(Name = "Target")]
    public string Target { get; set; } = string.Empty;

    [Required(ErrorMessage = "Agent selection is required.")]
    [Display(Name = "Agent")]
    public string AgentId { get; set; } = string.Empty;

    [Display(Name = "Depends on endpoints")]
    public List<string> DependsOnEndpointIds { get; set; } = [];

    [Range(1, int.MaxValue, ErrorMessage = "Ping interval must be at least 1 second.")]
    [Display(Name = "Ping interval (seconds)")]
    public int PingIntervalSeconds { get; set; } = 60;

    [Range(1, int.MaxValue, ErrorMessage = "Retry interval must be at least 1 second.")]
    [Display(Name = "Retry interval (seconds)")]
    public int RetryIntervalSeconds { get; set; } = 5;

    [Range(1, int.MaxValue, ErrorMessage = "Timeout must be at least 1 millisecond.")]
    [Display(Name = "Timeout (ms)")]
    public int TimeoutMs { get; set; } = 1000;

    [Range(1, int.MaxValue, ErrorMessage = "Failure threshold must be at least 1.")]
    [Display(Name = "Failure threshold")]
    public int FailureThreshold { get; set; } = 3;

    [Range(1, int.MaxValue, ErrorMessage = "Recovery threshold must be at least 1.")]
    [Display(Name = "Recovery threshold")]
    public int RecoveryThreshold { get; set; } = 2;

    [Display(Name = "Endpoint enabled")]
    public bool EndpointEnabled { get; set; } = true;

    [Display(Name = "Assignment enabled")]
    public bool AssignmentEnabled { get; set; } = true;

    public IReadOnlyList<CreateEndpointAgentOptionViewModel> AvailableAgents { get; set; } = [];

    public IReadOnlyList<CreateEndpointDependencyOptionViewModel> AvailableDependencyEndpoints { get; set; } = [];
}

public sealed class CreateEndpointAgentOptionViewModel
{
    public string AgentId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public sealed class CreateEndpointDependencyOptionViewModel
{
    public string EndpointId { get; init; } = string.Empty;
    public string EndpointName { get; init; } = string.Empty;
}
