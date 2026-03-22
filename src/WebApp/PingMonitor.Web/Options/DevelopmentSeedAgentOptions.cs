namespace PingMonitor.Web.Options;

public sealed class DevelopmentSeedAgentOptions
{
    public const string SectionName = "DevelopmentSeedAgent";

    public bool Enabled { get; set; }
    public string InstanceId { get; set; } = "dev-agent-01";
    public string? ApiKey { get; set; }
    public string Name { get; set; } = "Local Development Agent";
    public string? Site { get; set; } = "Local";
}
