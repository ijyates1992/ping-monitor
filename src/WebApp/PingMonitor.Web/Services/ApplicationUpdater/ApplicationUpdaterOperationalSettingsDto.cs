namespace PingMonitor.Web.Services.ApplicationUpdater;

public sealed class ApplicationUpdaterOperationalSettingsDto
{
    public bool EnableAutomaticUpdateChecks { get; init; }
    public int AutomaticUpdateCheckIntervalMinutes { get; init; }
    public bool AutomaticallyDownloadAndStageUpdates { get; init; }
    public bool AllowDevBuildAutoStageWithoutVersionComparison { get; init; }
    public bool AllowPreviewReleases { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
