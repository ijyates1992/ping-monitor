namespace PingMonitor.Web.Services;

public sealed class UpdateApplicationSettingsCommand
{
    public string SiteUrl { get; init; } = string.Empty;
    public int DefaultPingIntervalSeconds { get; init; }
    public int DefaultRetryIntervalSeconds { get; init; }
    public int DefaultTimeoutMs { get; init; }
    public int DefaultFailureThreshold { get; init; }
    public int DefaultRecoveryThreshold { get; init; }
    public bool DegradedEvaluationEnabled { get; init; } = true;
    public int DegradedBaselineLookbackMinutes { get; init; } = 1440;
    public int DegradedCurrentWindowMinutes { get; init; } = 60;
    public double DegradedPacketLossIncreasePercentagePoints { get; init; } = 20d;
    public double DegradedRttIncreasePercent { get; init; } = 20d;
    public double DegradedJitterIncreasePercent { get; init; } = 20d;
    public int DegradedMinimumSamples { get; init; } = 10;
    public bool NetworkDiagramsEnabled { get; init; }
}
