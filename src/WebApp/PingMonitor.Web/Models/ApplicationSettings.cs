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

    public bool EnableAutomaticUpdateChecks { get; set; } = true;

    public int AutomaticUpdateCheckIntervalMinutes { get; set; } = 15;

    public bool AutomaticallyDownloadAndStageUpdates { get; set; }

    public bool AllowDevBuildAutoStageWithoutVersionComparison { get; set; }

    public bool AllowPreviewReleases { get; set; }

    public DateTimeOffset? UpdaterOperationalSettingsInitializedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
