using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationMetadata;
using PingMonitor.Web.Services.StartupGate;

namespace PingMonitor.Web.Services.ApplicationUpdater;

public interface IApplicationUpdateApplyService
{
    Task<ApplicationUpdateStagingState?> RequestApplyAsync(string requestedByUserId, CancellationToken cancellationToken);
    Task<ApplicationUpdateStagingState?> RefreshApplyStateAsync(CancellationToken cancellationToken);
}

public interface IExternalUpdaterProcessLauncher
{
    bool TryLaunch(
        string powerShellExecutablePath,
        string bootstrapperScriptPath,
        string stagedMetadataPath,
        string installRootPath,
        string statusJsonPath,
        string logPath,
        string? expectedReleaseTag,
        out string? launchErrorMessage);
}

internal sealed class ExternalUpdaterProcessLauncher : IExternalUpdaterProcessLauncher
{
    public bool TryLaunch(
        string powerShellExecutablePath,
        string bootstrapperScriptPath,
        string stagedMetadataPath,
        string installRootPath,
        string statusJsonPath,
        string logPath,
        string? expectedReleaseTag,
        out string? launchErrorMessage)
    {
        launchErrorMessage = null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = powerShellExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = installRootPath
            };

            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(bootstrapperScriptPath);
            startInfo.ArgumentList.Add("-StagedMetadataPath");
            startInfo.ArgumentList.Add(stagedMetadataPath);
            startInfo.ArgumentList.Add("-InstallRootPath");
            startInfo.ArgumentList.Add(installRootPath);
            startInfo.ArgumentList.Add("-StatusJsonPath");
            startInfo.ArgumentList.Add(statusJsonPath);
            startInfo.ArgumentList.Add("-LogPath");
            startInfo.ArgumentList.Add(logPath);

            if (!string.IsNullOrWhiteSpace(expectedReleaseTag))
            {
                startInfo.ArgumentList.Add("-ExpectedReleaseTag");
                startInfo.ArgumentList.Add(expectedReleaseTag);
            }

            var process = Process.Start(startInfo);
            if (process is null)
            {
                launchErrorMessage = "Process.Start returned null.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            launchErrorMessage = ex.Message;
            return false;
        }
    }
}

internal sealed class ApplicationUpdateApplyService : IApplicationUpdateApplyService
{
    private static readonly JsonSerializerOptions ExternalStatusSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IApplicationUpdateStagingStateStore _stagingStateStore;
    private readonly IApplicationMetadataProvider _applicationMetadataProvider;
    private readonly IStartupGateRuntimeState _startupGateRuntimeState;
    private readonly IExternalUpdaterProcessLauncher _launcher;
    private readonly IWebHostEnvironment _environment;
    private readonly ApplicationUpdaterOptions _options;

    public ApplicationUpdateApplyService(
        IApplicationUpdateStagingStateStore stagingStateStore,
        IApplicationMetadataProvider applicationMetadataProvider,
        IStartupGateRuntimeState startupGateRuntimeState,
        IExternalUpdaterProcessLauncher launcher,
        IWebHostEnvironment environment,
        IOptions<ApplicationUpdaterOptions> options)
    {
        _stagingStateStore = stagingStateStore;
        _applicationMetadataProvider = applicationMetadataProvider;
        _startupGateRuntimeState = startupGateRuntimeState;
        _launcher = launcher;
        _environment = environment;
        _options = options.Value;
    }

    public async Task<ApplicationUpdateStagingState?> RequestApplyAsync(string requestedByUserId, CancellationToken cancellationToken)
    {
        var current = await _stagingStateStore.ReadAsync(cancellationToken);
        if (current is null)
        {
            throw new InvalidOperationException("No staged update is available to apply.");
        }

        var stagingRoot = _stagingStateStore.GetStagingRootPath();
        var stagedMetadataPath = Path.Combine(stagingRoot, "state", "staged-update.json");
        var bootstrapperPath = ResolveBootstrapperPath();
        var installRootPath = Path.GetFullPath(_environment.ContentRootPath);
        var externalStatusPath = Path.Combine(stagingRoot, "state", "external-updater-status.json");
        var externalLogPath = Path.Combine(stagingRoot, "state", "external-updater.log");

        ValidateApplyPrerequisites(current, stagedMetadataPath, bootstrapperPath);

        var now = DateTimeOffset.UtcNow;
        var requestedState = new ApplicationUpdateStagingState
        {
            SourceRepository = current.SourceRepository,
            AllowPreviewReleases = current.AllowPreviewReleases,
            ReleaseTag = current.ReleaseTag,
            ReleaseTitle = current.ReleaseTitle,
            ReleaseIsPrerelease = current.ReleaseIsPrerelease,
            ReleasePublishedAtUtc = current.ReleasePublishedAtUtc,
            ReleaseUrl = current.ReleaseUrl,
            SelectedAssetName = current.SelectedAssetName,
            SelectedChecksumAssetName = current.SelectedChecksumAssetName,
            StagedZipPath = current.StagedZipPath,
            StagedChecksumPath = current.StagedChecksumPath,
            ExpectedSha256 = current.ExpectedSha256,
            ActualSha256 = current.ActualSha256,
            ChecksumVerified = current.ChecksumVerified,
            StagedAtUtc = current.StagedAtUtc,
            StagingInProgress = false,
            StageOperationWasNoOp = current.StageOperationWasNoOp,
            StageOperationMessage = current.StageOperationMessage,
            LastStageAttemptAtUtc = current.LastStageAttemptAtUtc,
            LatestApplicableReleaseTag = current.LatestApplicableReleaseTag,
            IsCurrentLatest = current.IsCurrentLatest,
            IsOutdated = current.IsOutdated,
            Status = ApplicationUpdateStagingStatus.ApplyRequested,
            FailureMessage = null,
            BootstrapperScriptPath = bootstrapperPath,
            StagedMetadataPath = stagedMetadataPath,
            ExternalUpdaterStatusPath = externalStatusPath,
            ExternalUpdaterLogPath = externalLogPath,
            LastApplyRequestedByUserId = requestedByUserId,
            ApplyRequestedAtUtc = now,
            ApplyHandoffStartedAtUtc = null,
            ApplyCompletedAtUtc = null,
            ApplyOperationMessage = "Apply requested. Starting external updater handoff.",
            LastKnownUpdaterStage = current.LastKnownUpdaterStage,
            LastKnownUpdaterResultCode = current.LastKnownUpdaterResultCode,
            LastUpdatedAtUtc = now
        };

        await _stagingStateStore.WriteAsync(requestedState, cancellationToken);

        var launched = _launcher.TryLaunch(
            _options.PowerShellExecutablePath,
            bootstrapperPath,
            stagedMetadataPath,
            installRootPath,
            externalStatusPath,
            externalLogPath,
            current.ReleaseTag,
            out var launchErrorMessage);

        if (!launched)
        {
            var failedAt = DateTimeOffset.UtcNow;
            var failedState = CloneState(
                requestedState,
                ApplicationUpdateStagingStatus.ApplyFailed,
                $"Failed to launch external updater process: {launchErrorMessage}",
                failedAt,
                applyCompletedAtUtc: failedAt,
                failureMessage: $"Failed to launch external updater process: {launchErrorMessage}");
            await _stagingStateStore.WriteAsync(failedState, cancellationToken);
            throw new InvalidOperationException(failedState.FailureMessage);
        }

        var handoffAt = DateTimeOffset.UtcNow;
        var handoffState = CloneState(
            requestedState,
            ApplicationUpdateStagingStatus.ApplyHandoffStarted,
            "External updater launched. The application may restart during update application.",
            handoffAt,
            applyHandoffStartedAtUtc: handoffAt,
            lastKnownUpdaterStage: "handoff_started",
            lastKnownUpdaterResultCode: "in_progress");
        await _stagingStateStore.WriteAsync(handoffState, cancellationToken);

        return handoffState;
    }

    public async Task<ApplicationUpdateStagingState?> RefreshApplyStateAsync(CancellationToken cancellationToken)
    {
        var current = await _stagingStateStore.ReadAsync(cancellationToken);
        if (current is null)
        {
            return null;
        }

        var statusPath = current.ExternalUpdaterStatusPath;
        if (string.IsNullOrWhiteSpace(statusPath))
        {
            return current;
        }

        if (!File.Exists(statusPath))
        {
            return current;
        }

        ExternalUpdaterStatusSnapshot? externalStatus;
        await using (var stream = File.OpenRead(statusPath))
        {
            externalStatus = await JsonSerializer.DeserializeAsync<ExternalUpdaterStatusSnapshot>(stream, ExternalStatusSerializerOptions, cancellationToken);
        }

        if (externalStatus is null)
        {
            return current;
        }

        var now = DateTimeOffset.UtcNow;
        var currentVersion = _applicationMetadataProvider.GetSnapshot().Version;
        var status = current.Status;
        var applyMessage = current.ApplyOperationMessage;
        var failureMessage = current.FailureMessage;
        DateTimeOffset? applyCompletedAtUtc = current.ApplyCompletedAtUtc;

        var isCompleted = !string.IsNullOrWhiteSpace(externalStatus.CompletedAtUtc) &&
                          !string.Equals(externalStatus.ResultCode, "in_progress", StringComparison.OrdinalIgnoreCase);

        if (isCompleted)
        {
            applyCompletedAtUtc = now;
            if (externalStatus.Succeeded)
            {
                if (_startupGateRuntimeState.CurrentMode == StartupMode.Gate)
                {
                    status = ApplicationUpdateStagingStatus.ApplyStartupGateActionRequired;
                    applyMessage = "Update applied, but startup gate action is required before normal mode.";
                    failureMessage = null;
                }
                else if (!string.IsNullOrWhiteSpace(current.ReleaseTag) &&
                         string.Equals(current.ReleaseTag, currentVersion, StringComparison.OrdinalIgnoreCase))
                {
                    status = ApplicationUpdateStagingStatus.ApplySucceeded;
                    applyMessage = $"Update applied successfully. Installed version is now {currentVersion}.";
                    failureMessage = null;
                }
                else
                {
                    status = ApplicationUpdateStagingStatus.Applying;
                    applyMessage = "External updater reported success. Waiting for updated app metadata to reflect the staged release.";
                }
            }
            else
            {
                status = ApplicationUpdateStagingStatus.ApplyFailed;
                failureMessage = externalStatus.Error?.Message ?? "External updater reported failure.";
                applyMessage = "Update apply failed. Review external updater status/log for details.";
            }
        }
        else
        {
            status = ApplicationUpdateStagingStatus.Applying;
            applyMessage = "External updater is running or last reported in-progress state.";
            failureMessage = null;
        }

        var updated = CloneState(
            current,
            status,
            applyMessage,
            now,
            applyCompletedAtUtc: applyCompletedAtUtc,
            failureMessage: failureMessage,
            lastKnownUpdaterStage: externalStatus.Stage,
            lastKnownUpdaterResultCode: externalStatus.ResultCode);

        await _stagingStateStore.WriteAsync(updated, cancellationToken);
        return updated;
    }

    private static void ValidateApplyPrerequisites(ApplicationUpdateStagingState state, string stagedMetadataPath, string bootstrapperPath)
    {
        if (state.Status != ApplicationUpdateStagingStatus.Ready)
        {
            throw new InvalidOperationException("A staged update is not ready for apply. Run staging and checksum verification first.");
        }

        if (!state.ChecksumVerified)
        {
            throw new InvalidOperationException("Staged update checksum is not verified.");
        }

        if (string.IsNullOrWhiteSpace(state.StagedZipPath) || !File.Exists(state.StagedZipPath))
        {
            throw new InvalidOperationException("Staged update ZIP file is missing.");
        }

        if (!File.Exists(stagedMetadataPath))
        {
            throw new InvalidOperationException($"Staged metadata file was not found at '{stagedMetadataPath}'.");
        }

        if (!File.Exists(bootstrapperPath))
        {
            throw new InvalidOperationException($"Bundled updater bootstrapper was not found at '{bootstrapperPath}'.");
        }
    }

    private string ResolveBootstrapperPath()
    {
        var configuredPath = _options.BootstrapperRelativePath.Trim();
        return Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, _environment.ContentRootPath);
    }

    private static ApplicationUpdateStagingState CloneState(
        ApplicationUpdateStagingState source,
        ApplicationUpdateStagingStatus status,
        string? applyOperationMessage,
        DateTimeOffset now,
        DateTimeOffset? applyHandoffStartedAtUtc = null,
        DateTimeOffset? applyCompletedAtUtc = null,
        string? failureMessage = null,
        string? lastKnownUpdaterStage = null,
        string? lastKnownUpdaterResultCode = null)
    {
        return new ApplicationUpdateStagingState
        {
            SourceRepository = source.SourceRepository,
            AllowPreviewReleases = source.AllowPreviewReleases,
            ReleaseTag = source.ReleaseTag,
            ReleaseTitle = source.ReleaseTitle,
            ReleaseIsPrerelease = source.ReleaseIsPrerelease,
            ReleasePublishedAtUtc = source.ReleasePublishedAtUtc,
            ReleaseUrl = source.ReleaseUrl,
            SelectedAssetName = source.SelectedAssetName,
            SelectedChecksumAssetName = source.SelectedChecksumAssetName,
            StagedZipPath = source.StagedZipPath,
            StagedChecksumPath = source.StagedChecksumPath,
            ExpectedSha256 = source.ExpectedSha256,
            ActualSha256 = source.ActualSha256,
            ChecksumVerified = source.ChecksumVerified,
            StagedAtUtc = source.StagedAtUtc,
            StagingInProgress = false,
            StageOperationWasNoOp = source.StageOperationWasNoOp,
            StageOperationMessage = source.StageOperationMessage,
            LastStageAttemptAtUtc = source.LastStageAttemptAtUtc,
            LatestApplicableReleaseTag = source.LatestApplicableReleaseTag,
            IsCurrentLatest = source.IsCurrentLatest,
            IsOutdated = source.IsOutdated,
            Status = status,
            FailureMessage = failureMessage,
            BootstrapperScriptPath = source.BootstrapperScriptPath,
            StagedMetadataPath = source.StagedMetadataPath,
            ExternalUpdaterStatusPath = source.ExternalUpdaterStatusPath,
            ExternalUpdaterLogPath = source.ExternalUpdaterLogPath,
            LastApplyRequestedByUserId = source.LastApplyRequestedByUserId,
            ApplyRequestedAtUtc = source.ApplyRequestedAtUtc,
            ApplyHandoffStartedAtUtc = applyHandoffStartedAtUtc ?? source.ApplyHandoffStartedAtUtc,
            ApplyCompletedAtUtc = applyCompletedAtUtc ?? source.ApplyCompletedAtUtc,
            ApplyOperationMessage = applyOperationMessage,
            LastKnownUpdaterStage = lastKnownUpdaterStage ?? source.LastKnownUpdaterStage,
            LastKnownUpdaterResultCode = lastKnownUpdaterResultCode ?? source.LastKnownUpdaterResultCode,
            LastUpdatedAtUtc = now
        };
    }

    private sealed class ExternalUpdaterStatusSnapshot
    {
        public string? Stage { get; set; }
        public bool Succeeded { get; set; }
        public string? ResultCode { get; set; }
        public string? CompletedAtUtc { get; set; }
        public ExternalUpdaterErrorSnapshot? Error { get; set; }
    }

    private sealed class ExternalUpdaterErrorSnapshot
    {
        public string? Message { get; set; }
    }
}
