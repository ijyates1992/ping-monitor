namespace PingMonitor.Web.Options;

public sealed class AgentProvisioningOptions
{
    public const string SectionName = "AgentProvisioning";

    public string? ServerUrl { get; set; }
}
