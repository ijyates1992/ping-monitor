namespace PingMonitor.Web.Models;

public sealed class AssignmentRttMinuteBucket
{
    public string AssignmentId { get; set; } = string.Empty;
    public DateTimeOffset BucketStartUtc { get; set; }
    public int SampleCount { get; set; }
    public long SumRttMs { get; set; }
    public int MinRttMs { get; set; }
    public int MaxRttMs { get; set; }
    public int FirstRttMs { get; set; }
    public int LastRttMs { get; set; }
    public DateTimeOffset FirstSampleUtc { get; set; }
    public DateTimeOffset LastSampleUtc { get; set; }
    public double IntraBucketDeltaSumMs { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
