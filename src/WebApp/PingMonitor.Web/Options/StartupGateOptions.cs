namespace PingMonitor.Web.Options;

public sealed class StartupGateOptions
{
    public const string SectionName = "StartupGate";

    public string StorageDirectory { get; set; } = "App_Data/StartupGate";
    public int RequiredSchemaVersion { get; set; } = 2;
    public int DefaultMySqlPort { get; set; } = 3306;
}
