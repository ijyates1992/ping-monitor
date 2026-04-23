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
    private readonly IApplicationUpdaterRuntimeStateStore _runtimeStateStore;
    private readonly IApplicationUpdaterOperationalSettingsService _operationalSettingsService;
    private readonly IPowerShellPrerequisiteDetector _powerShellPrerequisiteDetector;
    private readonly ApplicationUpdaterOptions _updaterOptions;

    public AdminApplicationUpdaterController(
        IApplicationMetadataProvider applicationMetadataProvider,
        IApplicationUpdateDetectionService applicationUpdateDetectionService,
        IApplicationUpdateStagingService applicationUpdateStagingService,
        IApplicationUpdateApplyService applicationUpdateApplyService,
        IApplicationUpdaterRuntimeStateStore runtimeStateStore,
        IApplicationUpdaterOperationalSettingsService operationalSettingsService,
        IPowerShellPrerequisiteDetector powerShellPrerequisiteDetector,
        IOptions<ApplicationUpdaterOptions> updaterOptions)
    {
        _applicationMetadataProvider = applicationMetadataProvider;
        _applicationUpdateDetectionService = applicationUpdateDetectionService;
        _applicationUpdateStagingService = applicationUpdateStagingService;
        _applicationUpdateApplyService = applicationUpdateApplyService;
        _runtimeStateStore = runtimeStateStore;
        _operationalSettingsService = operationalSettingsService;
        _powerShellPrerequisiteDetector = powerShellPrerequisiteDetector;
        _updaterOptions = updaterOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var operationalSettings = await _operationalSettingsService.GetCurrentAsync(cancellationToken);
        var currentVersion = _applicationMetadataProvider.GetSnapshot().Version;
        var result = _updaterOptions.UpdateChecksEnabled
            ? ApplicationUpdateCheckResult.NotPerformed(currentVersion, operationalSettings.AllowPreviewReleases)
            : ApplicationUpdateCheckResult.Disabled(currentVersion, operationalSettings.AllowPreviewReleases);

        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        var runtimeState = await _runtimeStateStore.ReadAsync(cancellationToken);
        return View("Index", ToViewModel(result, staged, runtimeState, operationalSettings, saved: false));
    }

    [HttpPost("check")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Check([FromForm] bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        var result = await _applicationUpdateDetectionService.CheckForUpdatesAsync(allowPreviewReleases, cancellationToken);
        var operationalSettings = await _operationalSettingsService.GetCurrentAsync(cancellationToken);
        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        var runtimeState = await _runtimeStateStore.ReadAsync(cancellationToken);
        return View("Index", ToViewModel(result, staged, runtimeState, operationalSettings, saved: false));
    }

    [HttpPost("stage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stage([FromForm] bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        await _applicationUpdateStagingService.StageLatestApplicableReleaseAsync(allowPreviewReleases, cancellationToken);
        var result = await _applicationUpdateDetectionService.CheckForUpdatesAsync(allowPreviewReleases, cancellationToken);
        var operationalSettings = await _operationalSettingsService.GetCurrentAsync(cancellationToken);
        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        var runtimeState = await _runtimeStateStore.ReadAsync(cancellationToken);
        return View("Index", ToViewModel(result, staged, runtimeState, operationalSettings, saved: false));
    }

    [HttpPost("apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply([FromForm] bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        var requestedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "unknown";
        await _applicationUpdateApplyService.RequestApplyAsync(requestedByUserId, cancellationToken);
        var result = await _applicationUpdateDetectionService.CheckForUpdatesAsync(allowPreviewReleases, cancellationToken);
        var operationalSettings = await _operationalSettingsService.GetCurrentAsync(cancellationToken);
        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        var runtimeState = await _runtimeStateStore.ReadAsync(cancellationToken);
        return View("Index", ToViewModel(result, staged, runtimeState, operationalSettings, saved: false));
    }

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings([FromForm] ApplicationUpdaterPageViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var currentVersion = _applicationMetadataProvider.GetSnapshot().Version;
            var result = _updaterOptions.UpdateChecksEnabled
                ? ApplicationUpdateCheckResult.NotPerformed(currentVersion, model.AllowPreviewReleases)
                : ApplicationUpdateCheckResult.Disabled(currentVersion, model.AllowPreviewReleases);
            var stagedState = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
            var runtimeState = await _runtimeStateStore.ReadAsync(cancellationToken);
            var currentSettings = await _operationalSettingsService.GetCurrentAsync(cancellationToken);
            return View("Index", ToViewModel(result, stagedState, runtimeState, currentSettings, saved: false));
        }

        var updatedSettings = await _operationalSettingsService.UpdateAsync(
            new UpdateApplicationUpdaterOperationalSettingsCommand
            {
                EnableAutomaticUpdateChecks = model.EnableAutomaticUpdateChecks,
                AutomaticUpdateCheckIntervalMinutes = model.AutomaticUpdateCheckIntervalMinutes,
                AutomaticallyDownloadAndStageUpdates = model.AutomaticallyDownloadAndStageUpdates,
                AllowPreviewReleases = model.AllowPreviewReleases
            },
            cancellationToken);

        var checkResult = _updaterOptions.UpdateChecksEnabled
            ? ApplicationUpdateCheckResult.NotPerformed(_applicationMetadataProvider.GetSnapshot().Version, updatedSettings.AllowPreviewReleases)
            : ApplicationUpdateCheckResult.Disabled(_applicationMetadataProvider.GetSnapshot().Version, updatedSettings.AllowPreviewReleases);

        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        var runtime = await _runtimeStateStore.ReadAsync(cancellationToken);
        return View("Index", ToViewModel(checkResult, staged, runtime, updatedSettings, saved: true));
    }

    private ApplicationUpdaterPageViewModel ToViewModel(
        ApplicationUpdateCheckResult result,
        ApplicationUpdateStagingState? staged,
        ApplicationUpdaterRuntimeState? runtimeState,
        ApplicationUpdaterOperationalSettingsDto operationalSettings,
        bool saved)
    {
        var decoratedStaged = ApplyLatestComparison(staged, result.LatestApplicableRelease?.TagName);
        var powerShellStatus = _powerShellPrerequisiteDetector.GetStatus();

        return new ApplicationUpdaterPageViewModel
        {
            CurrentVersion = result.CurrentVersion,
            AllowPreviewReleases = result.AllowPreviewReleases,
            UpdateChecksEnabled = _updaterOptions.UpdateChecksEnabled,
            EnableAutomaticUpdateChecks = operationalSettings.EnableAutomaticUpdateChecks,
            AutomaticallyDownloadAndStageUpdates = operationalSettings.AutomaticallyDownloadAndStageUpdates,
            AutomaticUpdateCheckIntervalMinutes = operationalSettings.AutomaticUpdateCheckIntervalMinutes,
            RepositoryOwner = _updaterOptions.GitHubOwner,
            RepositoryName = _updaterOptions.GitHubRepository,
            SettingsSaved = saved,
            PowerShellPrerequisiteAvailable = powerShellStatus.IsAvailable,
            PowerShellPrerequisiteMessage = powerShellStatus.Message,
            PowerShellResolvedPath = powerShellStatus.ResolvedExecutablePath,
            State = result.State,
            Message = result.Message,
            LatestVersion = result.LatestApplicableRelease?.TagName,
            LatestReleaseName = result.LatestApplicableRelease?.Name,
            LatestIsPrerelease = result.LatestApplicableRelease?.IsPrerelease,
            LatestReleaseUrl = result.LatestApplicableRelease?.HtmlUrl,
            LatestPublishedAtUtc = result.LatestApplicableRelease?.PublishedAtUtc,
            RuntimeState = runtimeState,
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
            BootstrapperSource = staged.BootstrapperSource,
            BootstrapperSelectionMessage = staged.BootstrapperSelectionMessage,
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
