namespace PingMonitor.Web.Models;

public sealed class ApplicationSettings
{
    public const int SingletonId = 1;

    public int ApplicationSettingsId { get; set; } = SingletonId;

    public string SiteUrl { get; set; } = string.Empty;

    public int DefaultPingIntervalSeconds { get; set; } = 60;

    public int DefaultRetryIntervalSeconds { get; set; } = 5;

    public int DefaultTimeoutMs { get; set; } = 1000;

    public int DefaultFailureThreshold { get; set; } = 3;

    public int DefaultRecoveryThreshold { get; set; } = 2;

    public bool DegradedEvaluationEnabled { get; set; } = true;

    public int DegradedBaselineLookbackMinutes { get; set; } = 1440;

    public int DegradedCurrentWindowMinutes { get; set; } = 60;

    public double DegradedPacketLossIncreasePercentagePoints { get; set; } = 20d;

    public double DegradedRttIncreasePercent { get; set; } = 20d;

    public double DegradedJitterIncreasePercent { get; set; } = 20d;

    public int DegradedMinimumSamples { get; set; } = 10;

    public bool EnableAutomaticUpdateChecks { get; set; } = true;

    public int AutomaticUpdateCheckIntervalMinutes { get; set; } = 15;

    public bool AutomaticallyDownloadAndStageUpdates { get; set; }

    public bool AllowDevBuildAutoStageWithoutVersionComparison { get; set; }

    public bool AllowPreviewReleases { get; set; }

    public DateTimeOffset? UpdaterOperationalSettingsInitializedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
