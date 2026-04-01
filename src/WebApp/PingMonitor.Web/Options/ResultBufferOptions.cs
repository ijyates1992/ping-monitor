namespace PingMonitor.Web.Options;

public sealed class ResultBufferOptions
{
    public const string SectionName = "ResultBuffer";

    public bool ResultBufferEnabled { get; set; } = true;
    public int ResultBufferMaxBatchSize { get; set; } = 500;
    public int ResultBufferFlushIntervalSeconds { get; set; } = 60;
    public int ResultBufferMaxQueueSize { get; set; } = 5000;
}
