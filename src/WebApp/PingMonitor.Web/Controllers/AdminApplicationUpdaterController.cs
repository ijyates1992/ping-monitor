using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationMetadata;
using PingMonitor.Web.Services.ApplicationUpdater;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Admin;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("admin/application-updater")]
public sealed class AdminApplicationUpdaterController : Controller
{
    private readonly IApplicationMetadataProvider _applicationMetadataProvider;
    private readonly IApplicationUpdateDetectionService _applicationUpdateDetectionService;
    private readonly IApplicationUpdateStagingService _applicationUpdateStagingService;
    private readonly IApplicationUpdateStagingStateStore _applicationUpdateStagingStateStore;
    private readonly ApplicationUpdaterOptions _updaterOptions;

    public AdminApplicationUpdaterController(
        IApplicationMetadataProvider applicationMetadataProvider,
        IApplicationUpdateDetectionService applicationUpdateDetectionService,
        IApplicationUpdateStagingService applicationUpdateStagingService,
        IApplicationUpdateStagingStateStore applicationUpdateStagingStateStore,
        IOptions<ApplicationUpdaterOptions> updaterOptions)
    {
        _applicationMetadataProvider = applicationMetadataProvider;
        _applicationUpdateDetectionService = applicationUpdateDetectionService;
        _applicationUpdateStagingService = applicationUpdateStagingService;
        _applicationUpdateStagingStateStore = applicationUpdateStagingStateStore;
        _updaterOptions = updaterOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var currentVersion = _applicationMetadataProvider.GetSnapshot().Version;
        var result = _updaterOptions.UpdateChecksEnabled
            ? ApplicationUpdateCheckResult.NotPerformed(currentVersion, _updaterOptions.AllowPreviewReleases)
            : ApplicationUpdateCheckResult.Disabled(currentVersion, _updaterOptions.AllowPreviewReleases);

        var staged = await _applicationUpdateStagingStateStore.ReadAsync(cancellationToken);
        return View("Index", ToViewModel(result, staged));
    }

    [HttpPost("check")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Check([FromForm] bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        var result = await _applicationUpdateDetectionService.CheckForUpdatesAsync(allowPreviewReleases, cancellationToken);
        var staged = await _applicationUpdateStagingStateStore.ReadAsync(cancellationToken);
        return View("Index", ToViewModel(result, staged));
    }

    [HttpPost("stage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stage([FromForm] bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        await _applicationUpdateStagingService.StageLatestApplicableReleaseAsync(allowPreviewReleases, cancellationToken);
        var result = await _applicationUpdateDetectionService.CheckForUpdatesAsync(allowPreviewReleases, cancellationToken);
        var staged = await _applicationUpdateStagingStateStore.ReadAsync(cancellationToken);
        return View("Index", ToViewModel(result, staged));
    }

    private ApplicationUpdaterPageViewModel ToViewModel(ApplicationUpdateCheckResult result, ApplicationUpdateStagingState? staged)
    {
        return new ApplicationUpdaterPageViewModel
        {
            CurrentVersion = result.CurrentVersion,
            AllowPreviewReleases = result.AllowPreviewReleases,
            UpdateChecksEnabled = _updaterOptions.UpdateChecksEnabled,
            RepositoryOwner = _updaterOptions.GitHubOwner,
            RepositoryName = _updaterOptions.GitHubRepository,
            State = result.State,
            Message = result.Message,
            LatestVersion = result.LatestApplicableRelease?.TagName,
            LatestReleaseName = result.LatestApplicableRelease?.Name,
            LatestIsPrerelease = result.LatestApplicableRelease?.IsPrerelease,
            LatestReleaseUrl = result.LatestApplicableRelease?.HtmlUrl,
            LatestPublishedAtUtc = result.LatestApplicableRelease?.PublishedAtUtc,
            StagedUpdate = staged
        };
    }
}
