using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationMetadata;

namespace PingMonitor.Web.Services.ApplicationUpdater;

public interface IApplicationUpdateDetectionService
{
    Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(bool allowPreviewReleases, CancellationToken cancellationToken);
}

internal sealed class ApplicationUpdateDetectionService : IApplicationUpdateDetectionService
{
    private readonly IApplicationMetadataProvider _applicationMetadataProvider;
    private readonly IGitHubReleaseLookupService _gitHubReleaseLookupService;
    private readonly ApplicationUpdaterOptions _options;

    public ApplicationUpdateDetectionService(
        IApplicationMetadataProvider applicationMetadataProvider,
        IGitHubReleaseLookupService gitHubReleaseLookupService,
        Microsoft.Extensions.Options.IOptions<ApplicationUpdaterOptions> options)
    {
        _applicationMetadataProvider = applicationMetadataProvider;
        _gitHubReleaseLookupService = gitHubReleaseLookupService;
        _options = options.Value;
    }

    public async Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        var snapshot = _applicationMetadataProvider.GetSnapshot();
        var currentVersion = ReleaseVersionParser.ParseCurrent(snapshot.Version);

        if (!_options.UpdateChecksEnabled)
        {
            return ApplicationUpdateCheckResult.Disabled(snapshot.Version, allowPreviewReleases);
        }

        if (string.IsNullOrWhiteSpace(_options.GitHubOwner)
            || string.IsNullOrWhiteSpace(_options.GitHubRepository)
            || string.IsNullOrWhiteSpace(_options.GitHubApiBaseUrl))
        {
            return ApplicationUpdateCheckResult.Failed(
                snapshot.Version,
                allowPreviewReleases,
                "Update checks are misconfigured. GitHub owner/repository/API base URL are required.");
        }

        GitHubReleaseSummary? latestRelease;
        try
        {
            latestRelease = await _gitHubReleaseLookupService.GetLatestApplicableReleaseAsync(allowPreviewReleases, cancellationToken);
        }
        catch (Exception ex)
        {
            return ApplicationUpdateCheckResult.Failed(
                snapshot.Version,
                allowPreviewReleases,
                $"Unable to check GitHub releases: {ex.Message}");
        }

        if (latestRelease is null)
        {
            return ApplicationUpdateCheckResult.NoApplicableRelease(snapshot.Version, allowPreviewReleases, currentVersion);
        }

        if (currentVersion.IsDevBuild)
        {
            return ApplicationUpdateCheckResult.DevBuildComparisonSkipped(snapshot.Version, allowPreviewReleases, latestRelease);
        }

        if (!currentVersion.IsReleaseVersion || currentVersion.ReleaseVersion is null)
        {
            return ApplicationUpdateCheckResult.CurrentVersionUnknown(snapshot.Version, allowPreviewReleases, latestRelease);
        }

        if (!ReleaseVersionParser.TryParseReleaseVersion(latestRelease.TagName, out var latestVersion))
        {
            return ApplicationUpdateCheckResult.LatestVersionUnknown(snapshot.Version, allowPreviewReleases, latestRelease);
        }

        var comparison = latestVersion.CompareTo(currentVersion.ReleaseVersion.Value);
        if (comparison > 0)
        {
            return ApplicationUpdateCheckResult.UpdateAvailable(snapshot.Version, allowPreviewReleases, latestRelease);
        }

        return ApplicationUpdateCheckResult.UpToDate(snapshot.Version, allowPreviewReleases, latestRelease);
    }
}

public enum ApplicationUpdateCheckState
{
    CheckNotPerformed = 0,
    UpdateCheckDisabled = 1,
    NoApplicableRelease = 2,
    UpdateAvailable = 3,
    UpToDate = 4,
    CheckFailed = 5,
    DevBuildComparisonSkipped = 6,
    CurrentVersionUnknown = 7,
    LatestVersionUnknown = 8
}

public sealed class ApplicationUpdateCheckResult
{
    public ApplicationUpdateCheckState State { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public bool AllowPreviewReleases { get; init; }
    public bool IsCurrentBuildDev { get; init; }
    public bool SemanticComparisonPerformed { get; init; }
    public bool SemanticComparisonSkipped => !SemanticComparisonPerformed;
    public bool ReleaseDiscoverySucceeded => LatestApplicableRelease is not null;
    public string Message { get; init; } = string.Empty;
    public GitHubReleaseSummary? LatestApplicableRelease { get; init; }

    internal static ApplicationUpdateCheckResult NotPerformed(string currentVersion, bool allowPreviewReleases)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.CheckNotPerformed,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            IsCurrentBuildDev = ReleaseVersionParser.ParseCurrent(currentVersion).IsDevBuild,
            Message = "No update check has been performed yet."
        };
    }

    internal static ApplicationUpdateCheckResult Disabled(string currentVersion, bool allowPreviewReleases)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.UpdateCheckDisabled,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            IsCurrentBuildDev = ReleaseVersionParser.ParseCurrent(currentVersion).IsDevBuild,
            Message = "Update checks are disabled by configuration."
        };
    }

    internal static ApplicationUpdateCheckResult Failed(string currentVersion, bool allowPreviewReleases, string message)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.CheckFailed,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            IsCurrentBuildDev = ReleaseVersionParser.ParseCurrent(currentVersion).IsDevBuild,
            Message = message,
        };
    }

    internal static ApplicationUpdateCheckResult NoApplicableRelease(string currentVersion, bool allowPreviewReleases, VersionParseResult parsedCurrentVersion)
    {
        var currentVersionDescriptor = parsedCurrentVersion.IsReleaseVersion
            ? "Current version parsed successfully."
            : "Current version could not be parsed as a strict release version.";

        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.NoApplicableRelease,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            IsCurrentBuildDev = parsedCurrentVersion.IsDevBuild,
            Message = $"No applicable GitHub release was found for the current preview-release filter. {currentVersionDescriptor}"
        };
    }

    internal static ApplicationUpdateCheckResult UpdateAvailable(string currentVersion, bool allowPreviewReleases, GitHubReleaseSummary latestRelease)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.UpdateAvailable,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            SemanticComparisonPerformed = true,
            Message = "A newer release is available.",
            LatestApplicableRelease = latestRelease
        };
    }

    internal static ApplicationUpdateCheckResult UpToDate(string currentVersion, bool allowPreviewReleases, GitHubReleaseSummary latestRelease)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.UpToDate,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            SemanticComparisonPerformed = true,
            Message = "Current version is already up to date.",
            LatestApplicableRelease = latestRelease
        };
    }

    internal static ApplicationUpdateCheckResult DevBuildComparisonSkipped(string currentVersion, bool allowPreviewReleases, GitHubReleaseSummary latestRelease)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.DevBuildComparisonSkipped,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            IsCurrentBuildDev = true,
            LatestApplicableRelease = latestRelease,
            Message = $"Latest applicable release {latestRelease.TagName} was detected, but this instance is running a DEV build, so semantic version comparison was skipped."
        };
    }

    internal static ApplicationUpdateCheckResult CurrentVersionUnknown(string currentVersion, bool allowPreviewReleases, GitHubReleaseSummary latestRelease)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.CurrentVersionUnknown,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            LatestApplicableRelease = latestRelease,
            Message = $"Latest applicable release {latestRelease.TagName} was detected, but current version format is not recognized as strict Vx.x.x and is not a DEV build. Comparison could not be performed safely."
        };
    }

    internal static ApplicationUpdateCheckResult LatestVersionUnknown(string currentVersion, bool allowPreviewReleases, GitHubReleaseSummary latestRelease)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.LatestVersionUnknown,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            LatestApplicableRelease = latestRelease,
            SemanticComparisonPerformed = true,
            Message = $"Latest applicable release {latestRelease.TagName} was detected, but its tag is not a strict Vx.x.x version. Comparison could not be performed safely."
        };
    }
}

public sealed class GitHubReleaseSummary
{
    public string TagName { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? Body { get; init; }
    public bool IsPrerelease { get; init; }
    public string? HtmlUrl { get; init; }
    public DateTimeOffset? PublishedAtUtc { get; init; }
    public string? AssetsApiUrl { get; init; }
    public IReadOnlyList<GitHubReleaseAssetSummary> Assets { get; init; } = [];
    public int? RequiredSchemaVersion { get; init; }
}

public sealed class GitHubReleaseAssetSummary
{
    public string Name { get; init; } = string.Empty;
    public string? BrowserDownloadUrl { get; init; }
    public long? SizeBytes { get; init; }
}
