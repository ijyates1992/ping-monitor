using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationMetadata;
using PingMonitor.Web.Services.ApplicationUpdater;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.StartupGate;
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
    private readonly IGitHubReleaseLookupService _gitHubReleaseLookupService;
    private readonly IReleaseSchemaMetadataService _releaseSchemaMetadataService;
    private readonly IStartupSchemaService _startupSchemaService;
    private readonly ApplicationUpdaterOptions _updaterOptions;

    public AdminApplicationUpdaterController(
        IApplicationMetadataProvider applicationMetadataProvider,
        IApplicationUpdateDetectionService applicationUpdateDetectionService,
        IApplicationUpdateStagingService applicationUpdateStagingService,
        IApplicationUpdateApplyService applicationUpdateApplyService,
        IApplicationUpdaterRuntimeStateStore runtimeStateStore,
        IApplicationUpdaterOperationalSettingsService operationalSettingsService,
        IPowerShellPrerequisiteDetector powerShellPrerequisiteDetector,
        IGitHubReleaseLookupService gitHubReleaseLookupService,
        IReleaseSchemaMetadataService releaseSchemaMetadataService,
        IStartupSchemaService startupSchemaService,
        IOptions<ApplicationUpdaterOptions> updaterOptions)
    {
        _applicationMetadataProvider = applicationMetadataProvider;
        _applicationUpdateDetectionService = applicationUpdateDetectionService;
        _applicationUpdateStagingService = applicationUpdateStagingService;
        _applicationUpdateApplyService = applicationUpdateApplyService;
        _runtimeStateStore = runtimeStateStore;
        _operationalSettingsService = operationalSettingsService;
        _powerShellPrerequisiteDetector = powerShellPrerequisiteDetector;
        _gitHubReleaseLookupService = gitHubReleaseLookupService;
        _releaseSchemaMetadataService = releaseSchemaMetadataService;
        _startupSchemaService = startupSchemaService;
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
        var releaseSelection = await ResolveReleaseSelectionAsync(operationalSettings.AllowPreviewReleases, selectedReleaseTag: null, cancellationToken);
        return View("Index", ToViewModel(result, staged, runtimeState, operationalSettings, releaseSelection, saved: false));
    }

    [HttpPost("check")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Check([FromForm] bool allowPreviewReleases, [FromForm] string? selectedReleaseTag, CancellationToken cancellationToken)
    {
        var result = await _applicationUpdateDetectionService.CheckForUpdatesAsync(allowPreviewReleases, cancellationToken);
        var operationalSettings = await _operationalSettingsService.GetCurrentAsync(cancellationToken);
        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        var runtimeState = await _runtimeStateStore.ReadAsync(cancellationToken);
        var releaseSelection = await ResolveReleaseSelectionAsync(allowPreviewReleases, selectedReleaseTag, cancellationToken);
        return View("Index", ToViewModel(result, staged, runtimeState, operationalSettings, releaseSelection, saved: false));
    }

    [HttpPost("stage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stage([FromForm] bool allowPreviewReleases, [FromForm] string? selectedReleaseTag, CancellationToken cancellationToken)
    {
        await _applicationUpdateStagingService.StageSelectedApplicableReleaseAsync(allowPreviewReleases, selectedReleaseTag, cancellationToken);
        var result = await _applicationUpdateDetectionService.CheckForUpdatesAsync(allowPreviewReleases, cancellationToken);
        var operationalSettings = await _operationalSettingsService.GetCurrentAsync(cancellationToken);
        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        var runtimeState = await _runtimeStateStore.ReadAsync(cancellationToken);
        var releaseSelection = await ResolveReleaseSelectionAsync(allowPreviewReleases, selectedReleaseTag, cancellationToken);
        return View("Index", ToViewModel(result, staged, runtimeState, operationalSettings, releaseSelection, saved: false));
    }

    [HttpPost("apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply([FromForm] bool allowPreviewReleases, [FromForm] string? selectedReleaseTag, CancellationToken cancellationToken)
    {
        var requestedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "unknown";
        await _applicationUpdateApplyService.RequestApplyAsync(requestedByUserId, cancellationToken);
        var result = await _applicationUpdateDetectionService.CheckForUpdatesAsync(allowPreviewReleases, cancellationToken);
        var operationalSettings = await _operationalSettingsService.GetCurrentAsync(cancellationToken);
        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        var runtimeState = await _runtimeStateStore.ReadAsync(cancellationToken);
        var releaseSelection = await ResolveReleaseSelectionAsync(allowPreviewReleases, selectedReleaseTag, cancellationToken);
        return View("Index", ToViewModel(result, staged, runtimeState, operationalSettings, releaseSelection, saved: false));
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
            var releaseSelection = await ResolveReleaseSelectionAsync(model.AllowPreviewReleases, model.SelectedReleaseTag, cancellationToken);
            return View("Index", ToViewModel(result, stagedState, runtimeState, currentSettings, releaseSelection, saved: false));
        }

        var updatedSettings = await _operationalSettingsService.UpdateAsync(
            new UpdateApplicationUpdaterOperationalSettingsCommand
            {
                EnableAutomaticUpdateChecks = model.EnableAutomaticUpdateChecks,
                AutomaticUpdateCheckIntervalMinutes = model.AutomaticUpdateCheckIntervalMinutes,
                AutomaticallyDownloadAndStageUpdates = model.AutomaticallyDownloadAndStageUpdates,
                AllowDevBuildAutoStageWithoutVersionComparison = model.AllowDevBuildAutoStageWithoutVersionComparison,
                AllowPreviewReleases = model.AllowPreviewReleases
            },
            cancellationToken);

        var checkResult = _updaterOptions.UpdateChecksEnabled
            ? ApplicationUpdateCheckResult.NotPerformed(_applicationMetadataProvider.GetSnapshot().Version, updatedSettings.AllowPreviewReleases)
            : ApplicationUpdateCheckResult.Disabled(_applicationMetadataProvider.GetSnapshot().Version, updatedSettings.AllowPreviewReleases);

        var staged = await _applicationUpdateApplyService.RefreshApplyStateAsync(cancellationToken);
        var runtime = await _runtimeStateStore.ReadAsync(cancellationToken);
        var releaseSelectionForSaved = await ResolveReleaseSelectionAsync(updatedSettings.AllowPreviewReleases, model.SelectedReleaseTag, cancellationToken);
        return View("Index", ToViewModel(checkResult, staged, runtime, updatedSettings, releaseSelectionForSaved, saved: true));
    }

    private ApplicationUpdaterPageViewModel ToViewModel(
        ApplicationUpdateCheckResult result,
        ApplicationUpdateStagingState? staged,
        ApplicationUpdaterRuntimeState? runtimeState,
        ApplicationUpdaterOperationalSettingsDto operationalSettings,
        ReleaseSelectionResult releaseSelection,
        bool saved)
    {
        var latestApplicableRelease = releaseSelection.LatestRelease ?? result.LatestApplicableRelease;
        var decoratedStaged = ApplyLatestComparison(staged, latestApplicableRelease?.TagName);
        var powerShellStatus = _powerShellPrerequisiteDetector.GetStatus();
        var schemaCompatibility = BuildSchemaCompatibility(
            releaseSelection.CurrentDatabaseSchemaVersion,
            releaseSelection.SelectedRelease?.RequiredSchemaVersion);

        return new ApplicationUpdaterPageViewModel
        {
            CurrentVersion = result.CurrentVersion,
            AllowPreviewReleases = result.AllowPreviewReleases,
            UpdateChecksEnabled = _updaterOptions.UpdateChecksEnabled,
            EnableAutomaticUpdateChecks = operationalSettings.EnableAutomaticUpdateChecks,
            AutomaticallyDownloadAndStageUpdates = operationalSettings.AutomaticallyDownloadAndStageUpdates,
            AllowDevBuildAutoStageWithoutVersionComparison = operationalSettings.AllowDevBuildAutoStageWithoutVersionComparison,
            AutomaticUpdateCheckIntervalMinutes = operationalSettings.AutomaticUpdateCheckIntervalMinutes,
            RepositoryOwner = _updaterOptions.GitHubOwner,
            RepositoryName = _updaterOptions.GitHubRepository,
            SettingsSaved = saved,
            PowerShellPrerequisiteAvailable = powerShellStatus.IsAvailable,
            PowerShellPrerequisiteMessage = powerShellStatus.Message,
            PowerShellResolvedPath = powerShellStatus.ResolvedExecutablePath,
            State = result.State,
            Message = result.Message,
            IsCurrentBuildDev = result.IsCurrentBuildDev,
            SemanticComparisonPerformed = result.SemanticComparisonPerformed,
            SelectedReleaseTag = releaseSelection.SelectedRelease?.TagName,
            SelectedRelease = MapRelease(releaseSelection.SelectedRelease),
            SelectableReleases = releaseSelection.Releases.Select(MapRelease).OfType<ApplicationUpdaterSelectableReleaseViewModel>().ToArray(),
            LatestVersion = latestApplicableRelease?.TagName,
            LatestReleaseName = latestApplicableRelease?.Name,
            LatestIsPrerelease = latestApplicableRelease?.IsPrerelease,
            LatestReleaseUrl = latestApplicableRelease?.HtmlUrl,
            LatestPublishedAtUtc = latestApplicableRelease?.PublishedAtUtc,
            RuntimeState = runtimeState,
            StagedUpdate = decoratedStaged,
            CurrentDatabaseSchemaVersion = releaseSelection.CurrentDatabaseSchemaVersion,
            TargetRequiredSchemaVersion = releaseSelection.SelectedRelease?.RequiredSchemaVersion,
            SchemaCompatibilityState = schemaCompatibility.State,
            SchemaCompatibilityWarningMessage = schemaCompatibility.WarningMessage
        };
    }

    private async Task<ReleaseSelectionResult> ResolveReleaseSelectionAsync(bool allowPreviewReleases, string? selectedReleaseTag, CancellationToken cancellationToken)
    {
        if (!_updaterOptions.UpdateChecksEnabled)
        {
            return new ReleaseSelectionResult([], null, null, null);
        }

        try
        {
            var releases = await _gitHubReleaseLookupService.GetApplicableReleasesAsync(allowPreviewReleases, maxResults: 20, cancellationToken);
            var latest = releases.FirstOrDefault();
            var selected = string.IsNullOrWhiteSpace(selectedReleaseTag)
                ? latest
                : releases.FirstOrDefault(release => string.Equals(release.TagName, selectedReleaseTag, StringComparison.OrdinalIgnoreCase)) ?? latest;
            var hydratedReleases = await _releaseSchemaMetadataService.PopulateRequiredSchemaVersionsAsync(releases, cancellationToken);
            var hydratedLatest = hydratedReleases.FirstOrDefault();
            var hydratedSelected = string.IsNullOrWhiteSpace(selectedReleaseTag)
                ? hydratedLatest
                : hydratedReleases.FirstOrDefault(release => string.Equals(release.TagName, selectedReleaseTag, StringComparison.OrdinalIgnoreCase)) ?? hydratedLatest;
            var schemaStatus = await _startupSchemaService.GetStatusAsync(cancellationToken);

            return new ReleaseSelectionResult(hydratedReleases, hydratedSelected, hydratedLatest, schemaStatus.CurrentSchemaVersion);
        }
        catch
        {
            return new ReleaseSelectionResult([], null, null, null);
        }
    }

    private static ApplicationUpdaterSelectableReleaseViewModel? MapRelease(GitHubReleaseSummary? release)
    {
        if (release is null)
        {
            return null;
        }

        return new ApplicationUpdaterSelectableReleaseViewModel
        {
            TagName = release.TagName,
            Name = release.Name,
            Body = release.Body,
            IsPrerelease = release.IsPrerelease,
            HtmlUrl = release.HtmlUrl,
            PublishedAtUtc = release.PublishedAtUtc,
            RequiredSchemaVersion = release.RequiredSchemaVersion
        };
    }

    private static SchemaCompatibilityAssessment BuildSchemaCompatibility(int? currentSchemaVersion, int? targetRequiredSchemaVersion)
    {
        if (currentSchemaVersion is null)
        {
            return new SchemaCompatibilityAssessment(
                ApplicationUpdaterSchemaCompatibilityState.CurrentSchemaUnknown,
                "Current database schema version could not be determined. Schema compatibility for this release cannot be assessed safely.");
        }

        if (targetRequiredSchemaVersion is null)
        {
            return new SchemaCompatibilityAssessment(
                ApplicationUpdaterSchemaCompatibilityState.TargetSchemaInfoMissing,
                "Selected release metadata does not declare a required schema version. Schema compatibility cannot be assessed safely for this release.");
        }

        if (targetRequiredSchemaVersion > currentSchemaVersion)
        {
            return new SchemaCompatibilityAssessment(
                ApplicationUpdaterSchemaCompatibilityState.UpgradeRequiredAfterUpdate,
                $"Selected release requires schema version {targetRequiredSchemaVersion}, but current database schema version is {currentSchemaVersion}. This update will likely require a schema upgrade after restart, and Startup Gate may require explicit schema apply before normal operation resumes.");
        }

        if (targetRequiredSchemaVersion < currentSchemaVersion)
        {
            return new SchemaCompatibilityAssessment(
                ApplicationUpdaterSchemaCompatibilityState.TargetOlderThanCurrentSchema,
                $"Current database schema version is {currentSchemaVersion}, but selected release expects schema version {targetRequiredSchemaVersion}. Downgrading application files to this release may be incompatible with the current database and could fail or behave unpredictably.");
        }

        return new SchemaCompatibilityAssessment(
            ApplicationUpdaterSchemaCompatibilityState.CompatibleNoUpgradeNeeded,
            null);
    }

    private readonly record struct SchemaCompatibilityAssessment(
        ApplicationUpdaterSchemaCompatibilityState State,
        string? WarningMessage);

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

private sealed record ReleaseSelectionResult(
    IReadOnlyList<GitHubReleaseSummary> Releases,
    GitHubReleaseSummary? SelectedRelease,
    GitHubReleaseSummary? LatestRelease,
    int? CurrentDatabaseSchemaVersion);
}
