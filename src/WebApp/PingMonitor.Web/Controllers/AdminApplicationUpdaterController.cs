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
    private readonly ApplicationUpdaterOptions _updaterOptions;

    public AdminApplicationUpdaterController(
        IApplicationMetadataProvider applicationMetadataProvider,
        IApplicationUpdateDetectionService applicationUpdateDetectionService,
        IOptions<ApplicationUpdaterOptions> updaterOptions)
    {
        _applicationMetadataProvider = applicationMetadataProvider;
        _applicationUpdateDetectionService = applicationUpdateDetectionService;
        _updaterOptions = updaterOptions.Value;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var currentVersion = _applicationMetadataProvider.GetSnapshot().Version;
        var result = _updaterOptions.UpdateChecksEnabled
            ? ApplicationUpdateCheckResult.NotPerformed(currentVersion, _updaterOptions.AllowPreviewReleases)
            : ApplicationUpdateCheckResult.Disabled(currentVersion, _updaterOptions.AllowPreviewReleases);

        return View("Index", ToViewModel(result));
    }

    [HttpPost("check")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Check([FromForm] bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        var result = await _applicationUpdateDetectionService.CheckForUpdatesAsync(allowPreviewReleases, cancellationToken);
        return View("Index", ToViewModel(result));
    }

    private ApplicationUpdaterPageViewModel ToViewModel(ApplicationUpdateCheckResult result)
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
            LatestPublishedAtUtc = result.LatestApplicableRelease?.PublishedAtUtc
        };
    }
}
