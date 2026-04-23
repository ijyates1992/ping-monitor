using PingMonitor.Web.Services.ApplicationUpdater;
using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class ApplicationUpdaterPageViewModel
{
    public string CurrentVersion { get; init; } = string.Empty;
    public bool AllowPreviewReleases { get; set; }
    public bool UpdateChecksEnabled { get; init; }
    public bool EnableAutomaticUpdateChecks { get; set; }
    public bool AutomaticallyDownloadAndStageUpdates { get; set; }

    [Range(1, 1440, ErrorMessage = "Automatic check interval must be between 1 and 1440 minutes.")]
    public int AutomaticUpdateCheckIntervalMinutes { get; set; }

    public bool SettingsSaved { get; init; }
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
