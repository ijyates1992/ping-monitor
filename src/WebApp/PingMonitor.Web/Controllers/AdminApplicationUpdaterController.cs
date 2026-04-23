using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;
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
    private readonly IApplicationUpdateApplyService _applicationUpdateApplyService;
    private readonly ApplicationUpdaterOptions _updaterOptions;

    public AdminApplicationUpdaterController(
        IApplicationMetadataProvider applicationMetadataProvider,
        IApplicationUpdateDetectionService applicationUpdateDetectionService,
        IApplicationUpdateStagingService applicationUpdateStagingService,
        IApplicationUpdateApplyService applicationUpdateApplyService,
        IOptions<ApplicationUpdaterOptions> updaterOptions)
    {
        _applicationMetadataProvider = applicationMetadataProvider;
        _applicationUpdateDetectionService = applicationUpdateDetectionService;
        _applicationUpdateStagingService = applicationUpdateStagingService;
        _applicationUpdateApplyService = applicationUpdateApplyService;
        _updaterOptions = updaterOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var currentVersion = _applicationMetadataProvider.GetSnapshot().Version;
        var result = _updaterOptions.UpdateChecksEnabled
            ? ApplicationUpdateCheckResult.NotPerformed(currentVersion, _updaterOptions.AllowPreviewReleases)
            : ApplicationUpdateCheckResult.Disabled(currentVersion, _updaterOptions.AllowPreviewReleases);

        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        return View("Index", ToViewModel(result, staged));
    }

    [HttpPost("check")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Check([FromForm] bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        var result = await _applicationUpdateDetectionService.CheckForUpdatesAsync(allowPreviewReleases, cancellationToken);
        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        return View("Index", ToViewModel(result, staged));
    }

    [HttpPost("stage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stage([FromForm] bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        await _applicationUpdateStagingService.StageLatestApplicableReleaseAsync(allowPreviewReleases, cancellationToken);
        var result = await _applicationUpdateDetectionService.CheckForUpdatesAsync(allowPreviewReleases, cancellationToken);
        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        return View("Index", ToViewModel(result, staged));
    }

    [HttpPost("apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply([FromForm] bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        var requestedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "unknown";
        await _applicationUpdateApplyService.RequestApplyAsync(requestedByUserId, cancellationToken);
        var result = await _applicationUpdateDetectionService.CheckForUpdatesAsync(allowPreviewReleases, cancellationToken);
        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        return View("Index", ToViewModel(result, staged));
    }

    private ApplicationUpdaterPageViewModel ToViewModel(ApplicationUpdateCheckResult result, ApplicationUpdateStagingState? staged)
    {
        var decoratedStaged = ApplyLatestComparison(staged, result.LatestApplicableRelease?.TagName);

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
            StagedUpdate = decoratedStaged
        };
    }

    private static ApplicationUpdateStagingState? ApplyLatestComparison(ApplicationUpdateStagingState? staged, string? latestTag)
    {
        if (staged is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(latestTag) || string.IsNullOrWhiteSpace(staged.ReleaseTag))
        {
            return staged;
        }

        var isCurrentLatest = string.Equals(staged.ReleaseTag, latestTag, StringComparison.OrdinalIgnoreCase);
        return new ApplicationUpdateStagingState
        {
            SourceRepository = staged.SourceRepository,
            AllowPreviewReleases = staged.AllowPreviewReleases,
            ReleaseTag = staged.ReleaseTag,
            ReleaseTitle = staged.ReleaseTitle,
            ReleaseIsPrerelease = staged.ReleaseIsPrerelease,
            ReleasePublishedAtUtc = staged.ReleasePublishedAtUtc,
            ReleaseUrl = staged.ReleaseUrl,
            SelectedAssetName = staged.SelectedAssetName,
            SelectedChecksumAssetName = staged.SelectedChecksumAssetName,
            StagedZipPath = staged.StagedZipPath,
            StagedChecksumPath = staged.StagedChecksumPath,
            ExpectedSha256 = staged.ExpectedSha256,
            ActualSha256 = staged.ActualSha256,
            ChecksumVerified = staged.ChecksumVerified,
            StagedAtUtc = staged.StagedAtUtc,
            StagingInProgress = staged.StagingInProgress,
            StageOperationWasNoOp = staged.StageOperationWasNoOp,
            StageOperationMessage = staged.StageOperationMessage,
            LastStageAttemptAtUtc = staged.LastStageAttemptAtUtc,
            LatestApplicableReleaseTag = latestTag,
            IsCurrentLatest = isCurrentLatest,
            IsOutdated = !isCurrentLatest,
            Status = staged.Status,
            FailureMessage = staged.FailureMessage,
            BootstrapperScriptPath = staged.BootstrapperScriptPath,
            StagedMetadataPath = staged.StagedMetadataPath,
            LaunchPowerShellExecutablePath = staged.LaunchPowerShellExecutablePath,
            LaunchInstallRootPath = staged.LaunchInstallRootPath,
            LaunchWorkingDirectory = staged.LaunchWorkingDirectory,
            LaunchResolvedSiteName = staged.LaunchResolvedSiteName,
            LaunchResolvedAppPoolName = staged.LaunchResolvedAppPoolName,
            LaunchExpectedReleaseTag = staged.LaunchExpectedReleaseTag,
            ExternalUpdaterStatusPath = staged.ExternalUpdaterStatusPath,
            ExternalUpdaterLogPath = staged.ExternalUpdaterLogPath,
            BootstrapperProcessId = staged.BootstrapperProcessId,
            BootstrapperStartedAtUtc = staged.BootstrapperStartedAtUtc,
            LastApplyRequestedByUserId = staged.LastApplyRequestedByUserId,
            ApplyRequestedAtUtc = staged.ApplyRequestedAtUtc,
            ApplyHandoffStartedAtUtc = staged.ApplyHandoffStartedAtUtc,
            ApplyCompletedAtUtc = staged.ApplyCompletedAtUtc,
            ApplyOperationMessage = staged.ApplyOperationMessage,
            LastKnownUpdaterStage = staged.LastKnownUpdaterStage,
            LastKnownUpdaterResultCode = staged.LastKnownUpdaterResultCode,
            LastUpdatedAtUtc = staged.LastUpdatedAtUtc
        };
    }
}
