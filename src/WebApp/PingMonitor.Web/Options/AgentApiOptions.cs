namespace PingMonitor.Web.Options;

public sealed class AgentApiOptions
{
    public const string SectionName = "AgentApi";

    public int ConfigRefreshSeconds { get; set; } = 300;
    public int HeartbeatIntervalSeconds { get; set; } = 60;
    public int ResultBatchIntervalSeconds { get; set; } = 10;
    public int MaxResultBatchSize { get; set; } = 500;
    public string ConfigVersion { get; set; } = "cfg_v1_initial";
}
