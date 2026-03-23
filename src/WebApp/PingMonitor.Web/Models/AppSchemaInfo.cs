namespace PingMonitor.Web.Models;

public sealed class AppSchemaInfo
{
    public int AppSchemaInfoId { get; set; }
    public int CurrentSchemaVersion { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
