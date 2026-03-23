namespace PingMonitor.Web.Models;

public sealed class ResultBatch
{
    public string ResultBatchId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public int AcceptedCount { get; set; }
}
