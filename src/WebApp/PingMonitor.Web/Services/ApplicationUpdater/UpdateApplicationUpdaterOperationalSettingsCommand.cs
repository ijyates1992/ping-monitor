namespace PingMonitor.Web.Services.ApplicationUpdater;

public sealed class UpdateApplicationUpdaterOperationalSettingsCommand
{
    public bool EnableAutomaticUpdateChecks { get; init; }
    public int AutomaticUpdateCheckIntervalMinutes { get; init; }
    public bool AutomaticallyDownloadAndStageUpdates { get; init; }
    public bool AllowPreviewReleases { get; init; }
}
