using PingMonitor.Web.Services.ApplicationUpdater;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class ApplicationUpdaterPageViewModel
{
    public string CurrentVersion { get; init; } = string.Empty;
    public bool AllowPreviewReleases { get; set; }
    public bool UpdateChecksEnabled { get; init; }
    public bool EnableAutomaticUpdateChecks { get; init; }
    public bool AutomaticallyDownloadAndStageUpdates { get; init; }
    public int AutomaticUpdateCheckIntervalMinutes { get; init; }
    public string RepositoryOwner { get; init; } = string.Empty;
    public string RepositoryName { get; init; } = string.Empty;
    public bool PowerShellPrerequisiteAvailable { get; init; }
    public string? PowerShellResolvedPath { get; init; }
    public string PowerShellPrerequisiteMessage { get; init; } = string.Empty;
    public ApplicationUpdateCheckState State { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? LatestVersion { get; init; }
    public string? LatestReleaseName { get; init; }
    public bool? LatestIsPrerelease { get; init; }
    public string? LatestReleaseUrl { get; init; }
    public DateTimeOffset? LatestPublishedAtUtc { get; init; }
    public ApplicationUpdaterRuntimeState? RuntimeState { get; init; }
    public ApplicationUpdateStagingState? StagedUpdate { get; init; }
}
