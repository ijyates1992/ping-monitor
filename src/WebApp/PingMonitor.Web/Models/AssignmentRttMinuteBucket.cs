namespace PingMonitor.Web.Models;

public sealed class AssignmentRttMinuteBucket
{
    public string AssignmentId { get; set; } = string.Empty;
    public DateTimeOffset BucketStartUtc { get; set; }
    public int SampleCount { get; set; }
    public double SumRttMs { get; set; }
    public double MinRttMs { get; set; }
    public double MaxRttMs { get; set; }
    public double FirstRttMs { get; set; }
    public double LastRttMs { get; set; }
    public DateTimeOffset FirstSampleUtc { get; set; }
    public DateTimeOffset LastSampleUtc { get; set; }
    public double IntraBucketDeltaSumMs { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
