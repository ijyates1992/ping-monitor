using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.Contracts.Hello;

public sealed class AgentHelloRequest
{
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string AgentVersion { get; init; } = string.Empty;

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string MachineName { get; init; } = string.Empty;

    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string Platform { get; init; } = string.Empty;

    [Required]
    [MinLength(1)]
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    [Required]
    public DateTimeOffset StartedAtUtc { get; init; }
}
