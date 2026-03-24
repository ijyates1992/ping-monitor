namespace PingMonitor.Web.Services;

public sealed class AgentProvisioningResult
{
    public required string AgentId { get; init; }
    public required string InstanceId { get; init; }
    public required string AgentName { get; init; }
    public required string PackageFileName { get; init; }
    public required byte[] PackageBytes { get; init; }
}
