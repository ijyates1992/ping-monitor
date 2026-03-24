using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Agents;

public sealed class DeployAgentPageViewModel
{
    [Required(ErrorMessage = "Agent name is required.")]
    [StringLength(255, ErrorMessage = "Agent name cannot exceed 255 characters.")]
    public string AgentName { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
}
