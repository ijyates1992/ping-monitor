using PingMonitor.Web.Services.ApplicationMetadata;
using PingMonitor.Web.Options;
using Microsoft.Extensions.Options;

namespace PingMonitor.Web.Services.ApplicationUpdate;

public interface IApplicationUpdateCheckService
{
    ApplicationUpdateCheckResult BuildNotPerformedResult();
    Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken);
}

public sealed class ApplicationUpdateCheckService : IApplicationUpdateCheckService
{
    private readonly IApplicationMetadataProvider _applicationMetadataProvider;
    private readonly IApplicationUpdateReleaseLookupService _releaseLookupService;
    private readonly ApplicationUpdateOptions _options;

    public ApplicationUpdateCheckService(
        IApplicationMetadataProvider applicationMetadataProvider,
        IApplicationUpdateReleaseLookupService releaseLookupService,
        IOptions<ApplicationUpdateOptions> options)
    {
        _applicationMetadataProvider = applicationMetadataProvider;
        _releaseLookupService = releaseLookupService;
        _options = options.Value;
    }

    public ApplicationUpdateCheckResult BuildNotPerformedResult()
    {
        var currentVersion = _applicationMetadataProvider.GetSnapshot().Version;
        return new ApplicationUpdateCheckResult
        {
            State = _options.Enabled
                ? ApplicationUpdateCheckState.CheckNotPerformed
                : ApplicationUpdateCheckState.CheckDisabled,
            CurrentVersionRaw = currentVersion,
            CurrentVersionNormalized = ApplicationUpdateVersion.TryParse(currentVersion, out var current)
                ? current.ToString()
                : null
        };
    }

    public async Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        var currentVersion = _applicationMetadataProvider.GetSnapshot().Version;

        if (!_options.Enabled)
        {
            return new ApplicationUpdateCheckResult
            {
                State = ApplicationUpdateCheckState.CheckDisabled,
                CurrentVersionRaw = currentVersion,
                CurrentVersionNormalized = ApplicationUpdateVersion.TryParse(currentVersion, out var disabledCurrent)
                    ? disabledCurrent.ToString()
                    : null,
                ErrorMessage = "Update checks are disabled by configuration."
            };
        }

        var (release, errorMessage) = await _releaseLookupService.TryGetLatestReleaseAsync(cancellationToken);
        if (release is null)
        {
            return new ApplicationUpdateCheckResult
            {
                State = ApplicationUpdateCheckState.CheckFailed,
                CurrentVersionRaw = currentVersion,
                CurrentVersionNormalized = ApplicationUpdateVersion.TryParse(currentVersion, out var failureCurrent)
                    ? failureCurrent.ToString()
                    : null,
                ErrorMessage = errorMessage ?? "Unable to check for updates."
            };
        }

        var currentParsed = ApplicationUpdateVersion.TryParse(currentVersion, out var currentNormalized);
        var latestParsed = ApplicationUpdateVersion.TryParse(release.TagName, out var latestNormalized);

        var state = ResolveState(currentParsed, latestParsed, currentNormalized, latestNormalized);
        var comparisonErrorMessage = state switch
        {
            ApplicationUpdateCheckState.CurrentVersionUnknown => "Installed version is not in expected Vx.x.x format.",
            ApplicationUpdateCheckState.LatestVersionUnknown => "Latest release tag is not in expected Vx.x.x format.",
            _ => null
        };

        return new ApplicationUpdateCheckResult
        {
            State = state,
            CurrentVersionRaw = currentVersion,
            CurrentVersionNormalized = currentParsed ? currentNormalized.ToString() : null,
            LatestVersionRaw = release.TagName,
            LatestVersionNormalized = latestParsed ? latestNormalized.ToString() : null,
            ReleaseName = release.Name,
            ReleasePublishedAtUtc = release.PublishedAtUtc,
            ReleaseIsPrerelease = release.IsPrerelease,
            ReleaseUrl = release.HtmlUrl,
            ErrorMessage = comparisonErrorMessage
        };
    }

    private static ApplicationUpdateCheckState ResolveState(
        bool currentParsed,
        bool latestParsed,
        ApplicationUpdateVersion currentVersion,
        ApplicationUpdateVersion latestVersion)
    {
        if (!currentParsed)
        {
            return ApplicationUpdateCheckState.CurrentVersionUnknown;
        }

        if (!latestParsed)
        {
            return ApplicationUpdateCheckState.LatestVersionUnknown;
        }

        return latestVersion.CompareTo(currentVersion) > 0
            ? ApplicationUpdateCheckState.UpdateAvailable
            : ApplicationUpdateCheckState.AlreadyUpToDate;
    }
}
