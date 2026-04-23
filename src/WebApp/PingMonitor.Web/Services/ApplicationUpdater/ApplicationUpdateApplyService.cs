using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    bool TryResolveExecutablePath(string configuredExecutablePath, out string? resolvedExecutablePath, out string? resolutionErrorMessage);

    bool TryLaunch(
        string powerShellExecutablePath,
        string bootstrapperScriptPath,
        string stagedMetadataPath,
        string installRootPath,
        string siteName,
        string appPoolName,
        string statusJsonPath,
        string logPath,
        string? expectedReleaseTag,
        out int? processId,
        out string? launchErrorMessage);
}

internal sealed class ExternalUpdaterProcessLauncher : IExternalUpdaterProcessLauncher
{
    public bool TryResolveExecutablePath(string configuredExecutablePath, out string? resolvedExecutablePath, out string? resolutionErrorMessage)
    {
        resolvedExecutablePath = null;
        resolutionErrorMessage = null;

        if (string.IsNullOrWhiteSpace(configuredExecutablePath))
        {
            resolutionErrorMessage = "PowerShell executable path is not configured.";
            return false;
        }

        var configured = configuredExecutablePath.Trim();
        if (Path.IsPathRooted(configured))
        {
            var absolutePath = Path.GetFullPath(configured);
            if (!File.Exists(absolutePath))
            {
                resolutionErrorMessage = $"Configured PowerShell executable was not found at '{absolutePath}'.";
                return false;
            }

            resolvedExecutablePath = absolutePath;
            return true;
        }

        var candidateNames = BuildExecutableCandidates(configured);
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var directories = pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var candidateName in candidateNames)
            {
                var candidatePath = Path.Combine(directory, candidateName);
                if (!File.Exists(candidatePath))
                {
                    continue;
                }

                resolvedExecutablePath = Path.GetFullPath(candidatePath);
                return true;
            }
        }

        resolutionErrorMessage = $"Configured PowerShell executable '{configuredExecutablePath}' was not found on PATH.";
        return false;
    }

    public bool TryLaunch(
        string powerShellExecutablePath,
        string bootstrapperScriptPath,
        string stagedMetadataPath,
        string installRootPath,
        string siteName,
        string appPoolName,
        string statusJsonPath,
        string logPath,
        string? expectedReleaseTag,
        out int? processId,
        out string? launchErrorMessage)
    {
        processId = null;
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
            startInfo.ArgumentList.Add("-SiteName");
            startInfo.ArgumentList.Add(siteName);
            startInfo.ArgumentList.Add("-AppPoolName");
            startInfo.ArgumentList.Add(appPoolName);
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

            processId = process.Id;
            return true;
        }
        catch (Exception ex)
        {
            launchErrorMessage = ex.Message;
            return false;
        }
    }

    private static string[] BuildExecutableCandidates(string configuredExecutablePath)
    {
        var names = new List<string>();
        var configured = configuredExecutablePath.Trim();
        names.Add(configured);

        if (Path.GetExtension(configured).Length == 0 &&
            OperatingSystem.IsWindows())
        {
            names.Add($"{configured}.exe");
            names.Add($"{configured}.cmd");
            names.Add($"{configured}.bat");
        }

        return names.ToArray();
    }
}

internal interface IIisMetadataReader
{
    bool TryResolveSiteNameFromPhysicalPath(string normalizedInstallRootPath, out string? siteName, out string? errorMessage);
    bool TryResolveSiteNameFromAppPool(string appPoolName, out string? siteName, out string? errorMessage);
}

internal sealed class IisMetadataReader : IIisMetadataReader
{
    public bool TryResolveSiteNameFromPhysicalPath(string normalizedInstallRootPath, out string? siteName, out string? errorMessage)
    {
        return TryResolveSiteName(
            site =>
            {
                var physicalPath = GetRootPhysicalPath(site);
                if (string.IsNullOrWhiteSpace(physicalPath))
                {
                    return false;
                }

                return string.Equals(
                    NormalizePath(physicalPath),
                    normalizedInstallRootPath,
                    StringComparison.OrdinalIgnoreCase);
            },
            out siteName,
            out errorMessage);
    }

    public bool TryResolveSiteNameFromAppPool(string appPoolName, out string? siteName, out string? errorMessage)
    {
        return TryResolveSiteName(
            site => string.Equals(GetRootApplicationPoolName(site), appPoolName, StringComparison.OrdinalIgnoreCase),
            out siteName,
            out errorMessage);
    }

    private static bool TryResolveSiteName(Func<object, bool> matcher, out string? siteName, out string? errorMessage)
    {
        siteName = null;
        errorMessage = null;

        if (!OperatingSystem.IsWindows())
        {
            errorMessage = "IIS metadata lookup is only supported on Windows hosts.";
            return false;
        }

        try
        {
            var serverManagerType = Type.GetType("Microsoft.Web.Administration.ServerManager, Microsoft.Web.Administration");
            if (serverManagerType is null)
            {
                errorMessage = "Microsoft.Web.Administration assembly is not available.";
                return false;
            }

            using var serverManager = Activator.CreateInstance(serverManagerType) as IDisposable;
            if (serverManager is null)
            {
                errorMessage = "Failed to construct Microsoft.Web.Administration.ServerManager.";
                return false;
            }

            var sitesProperty = serverManagerType.GetProperty("Sites", BindingFlags.Public | BindingFlags.Instance);
            var sites = sitesProperty?.GetValue(serverManager);
            if (sites is not System.Collections.IEnumerable enumerableSites)
            {
                errorMessage = "Could not enumerate IIS sites from ServerManager.";
                return false;
            }

            foreach (var site in enumerableSites)
            {
                if (site is null || !matcher(site))
                {
                    continue;
                }

                var name = site.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(site) as string;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    siteName = name;
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static object? GetRootApplication(object site)
    {
        var applications = site.GetType().GetProperty("Applications", BindingFlags.Public | BindingFlags.Instance)?.GetValue(site);
        if (applications is null)
        {
            return null;
        }

        var indexer = applications.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(property =>
            {
                var indexes = property.GetIndexParameters();
                return indexes.Length == 1 && indexes[0].ParameterType == typeof(string);
            });

        return indexer?.GetValue(applications, ["/"]);
    }

    private static string? GetRootApplicationPoolName(object site)
    {
        var rootApplication = GetRootApplication(site);
        return rootApplication?.GetType().GetProperty("ApplicationPoolName", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(rootApplication) as string;
    }

    private static string? GetRootPhysicalPath(object site)
    {
        var rootApplication = GetRootApplication(site);
        if (rootApplication is null)
        {
            return null;
        }

        var virtualDirectories = rootApplication.GetType().GetProperty("VirtualDirectories", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(rootApplication);
        if (virtualDirectories is null)
        {
            return null;
        }

        var indexer = virtualDirectories.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(property =>
            {
                var indexes = property.GetIndexParameters();
                return indexes.Length == 1 && indexes[0].ParameterType == typeof(string);
            });

        var rootVirtualDirectory = indexer?.GetValue(virtualDirectories, ["/"]);
        return rootVirtualDirectory?.GetType().GetProperty("PhysicalPath", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(rootVirtualDirectory) as string;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Length == 0 ? Path.DirectorySeparatorChar.ToString() : fullPath;
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
    private readonly IPowerShellPrerequisiteDetector _powerShellPrerequisiteDetector;
    private readonly IExternalUpdaterProcessLauncher _launcher;
    private readonly IIisMetadataReader _iisMetadataReader;
    private readonly IWebHostEnvironment _environment;
    private readonly ApplicationUpdaterOptions _options;
    private readonly ILogger<ApplicationUpdateApplyService> _logger;

    public ApplicationUpdateApplyService(
        IApplicationUpdateStagingStateStore stagingStateStore,
        IApplicationMetadataProvider applicationMetadataProvider,
        IStartupGateRuntimeState startupGateRuntimeState,
        IPowerShellPrerequisiteDetector powerShellPrerequisiteDetector,
        IExternalUpdaterProcessLauncher launcher,
        IWebHostEnvironment environment,
        IOptions<ApplicationUpdaterOptions> options,
        ILogger<ApplicationUpdateApplyService> logger,
        IIisMetadataReader? iisMetadataReader = null)
    {
        _stagingStateStore = stagingStateStore;
        _applicationMetadataProvider = applicationMetadataProvider;
        _startupGateRuntimeState = startupGateRuntimeState;
        _powerShellPrerequisiteDetector = powerShellPrerequisiteDetector;
        _launcher = launcher;
        _environment = environment;
        _options = options.Value;
        _logger = logger;
        _iisMetadataReader = iisMetadataReader ?? new IisMetadataReader();
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
        var bootstrapperSelection = ResolveBootstrapperLaunchSelection(current, stagingRoot);
        var bootstrapperPath = bootstrapperSelection.BootstrapperPath;
        var installRootPath = Path.GetFullPath(_environment.ContentRootPath);
        var externalStatusPath = Path.Combine(stagingRoot, "state", "external-updater-status.json");
        var externalLogPath = Path.Combine(stagingRoot, "state", "external-updater.log");

        ValidateApplyPrerequisites(current, stagedMetadataPath);

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
            BootstrapperSource = bootstrapperSelection.BootstrapperSource,
            BootstrapperSelectionMessage = bootstrapperSelection.SelectionMessage,
            StagedMetadataPath = stagedMetadataPath,
            LaunchPowerShellExecutablePath = null,
            LaunchInstallRootPath = installRootPath,
            LaunchWorkingDirectory = installRootPath,
            LaunchResolvedSiteName = null,
            LaunchResolvedAppPoolName = null,
            LaunchExpectedReleaseTag = current.ReleaseTag,
            ExternalUpdaterStatusPath = externalStatusPath,
            ExternalUpdaterLogPath = externalLogPath,
            BootstrapperProcessId = null,
            BootstrapperStartedAtUtc = null,
            LastApplyRequestedByUserId = requestedByUserId,
            ApplyRequestedAtUtc = now,
            ApplyHandoffStartedAtUtc = null,
            ApplyCompletedAtUtc = null,
            ApplyOperationMessage = "Apply requested. Starting external updater handoff.",
            LastKnownUpdaterStage = "apply_requested",
            LastKnownUpdaterResultCode = "in_progress",
            LastUpdatedAtUtc = now
        };

        await _stagingStateStore.WriteAsync(requestedState, cancellationToken);

        var resolvingAt = DateTimeOffset.UtcNow;
        var resolvingState = CloneState(
            requestedState,
            ApplicationUpdateStagingStatus.ApplyRequested,
            "Resolving IIS identity for bootstrapper launch.",
            resolvingAt,
            lastKnownUpdaterStage: "resolving_iis_identity",
            lastKnownUpdaterResultCode: "in_progress");
        await _stagingStateStore.WriteAsync(resolvingState, cancellationToken);

        _logger.LogInformation("Attempting IIS identity resolution for application updater launch.");
        var appPoolName = ResolveAppPoolName();
        var siteName = ResolveSiteName(installRootPath, appPoolName);
        if (string.IsNullOrWhiteSpace(siteName) && string.IsNullOrWhiteSpace(appPoolName))
        {
            var resolutionFailureMessage =
                "Application updater cannot launch because IIS identity resolution failed. " +
                "Attempted strategies: APP_POOL_ID, ApplicationUpdater:IisSiteName, WEBSITE_SITE_NAME, IIS site lookup by physical path, IIS site lookup by app pool. " +
                "Configure ApplicationUpdater:IisSiteName and ApplicationUpdater:IisAppPoolName manually in appsettings when automatic detection is unavailable.";
            await PersistLaunchFailureAsync(resolvingState, resolutionFailureMessage, cancellationToken);
            throw new InvalidOperationException(resolutionFailureMessage);
        }

        var powerShellStatus = _powerShellPrerequisiteDetector.GetStatus();
        if (!powerShellStatus.IsAvailable || string.IsNullOrWhiteSpace(powerShellStatus.ResolvedExecutablePath))
        {
            var resolutionFailureMessage =
                "PowerShell 7 (`pwsh`) was not found in PATH. Update apply operations are unavailable until PowerShell 7 is installed and accessible to the application.";
            await PersistLaunchFailureAsync(
                CloneState(
                    resolvingState,
                    ApplicationUpdateStagingStatus.ApplyRequested,
                    resolvingState.ApplyOperationMessage,
                    DateTimeOffset.UtcNow,
                    launchResolvedSiteName: siteName,
                    launchResolvedAppPoolName: appPoolName),
                resolutionFailureMessage,
                cancellationToken);
            throw new InvalidOperationException(resolutionFailureMessage);
        }
        var powerShellExecutablePath = powerShellStatus.ResolvedExecutablePath!;

        var launchInputValidationError = ValidateLaunchInputs(bootstrapperPath, stagedMetadataPath, installRootPath, powerShellExecutablePath!);
        if (!string.IsNullOrWhiteSpace(launchInputValidationError))
        {
            await PersistLaunchFailureAsync(
                CloneState(
                    resolvingState,
                    ApplicationUpdateStagingStatus.ApplyRequested,
                    resolvingState.ApplyOperationMessage,
                    DateTimeOffset.UtcNow,
                    launchPowerShellExecutablePath: powerShellExecutablePath,
                    launchResolvedSiteName: siteName,
                    launchResolvedAppPoolName: appPoolName),
                launchInputValidationError,
                cancellationToken);
            throw new InvalidOperationException(launchInputValidationError);
        }

        var launchingAt = DateTimeOffset.UtcNow;
        var launchingState = CloneState(
            resolvingState,
            ApplicationUpdateStagingStatus.ApplyRequested,
            "Launching external updater bootstrapper.",
            launchingAt,
            launchPowerShellExecutablePath: powerShellExecutablePath,
            launchResolvedSiteName: siteName,
            launchResolvedAppPoolName: appPoolName,
            launchExpectedReleaseTag: current.ReleaseTag,
            lastKnownUpdaterStage: "launching_bootstrapper",
            lastKnownUpdaterResultCode: "in_progress");
        await _stagingStateStore.WriteAsync(launchingState, cancellationToken);

        _logger.LogInformation(
            "Launching updater bootstrapper with PowerShell '{PowerShellExecutablePath}', script '{BootstrapperScriptPath}', metadata '{StagedMetadataPath}', install root '{InstallRootPath}', working directory '{WorkingDirectory}', site '{SiteName}', app pool '{AppPoolName}', expected tag '{ExpectedReleaseTag}', status '{StatusPath}', log '{LogPath}'.",
            powerShellExecutablePath,
            bootstrapperPath,
            stagedMetadataPath,
            installRootPath,
            installRootPath,
            siteName,
            appPoolName,
            current.ReleaseTag ?? "(none)",
            externalStatusPath,
            externalLogPath);
        _logger.LogInformation(
            "Updater bootstrapper source: {BootstrapperSource}. {SelectionMessage}",
            bootstrapperSelection.BootstrapperSource,
            bootstrapperSelection.SelectionMessage);

        var launched = _launcher.TryLaunch(
            powerShellExecutablePath!,
            bootstrapperPath,
            stagedMetadataPath,
            installRootPath,
            siteName!,
            appPoolName!,
            externalStatusPath,
            externalLogPath,
            current.ReleaseTag,
            out var processId,
            out var launchErrorMessage);

        if (!launched)
        {
            var launchFailureMessage = $"Failed to launch external updater process: {launchErrorMessage}";
            await PersistLaunchFailureAsync(launchingState, launchFailureMessage, cancellationToken);
            throw new InvalidOperationException(launchFailureMessage);
        }

        var handoffAt = DateTimeOffset.UtcNow;
        var handoffState = CloneState(
            launchingState,
            ApplicationUpdateStagingStatus.ApplyHandoffStarted,
            processId.HasValue
                ? $"External updater launched (PID {processId.Value}). The application may restart during update application."
                : "External updater launched. The application may restart during update application.",
            handoffAt,
            applyHandoffStartedAtUtc: handoffAt,
            bootstrapperProcessId: processId,
            bootstrapperStartedAtUtc: handoffAt,
            lastKnownUpdaterStage: "bootstrapper_started",
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

    private static void ValidateApplyPrerequisites(ApplicationUpdateStagingState state, string stagedMetadataPath)
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

    }

    private string ResolveBootstrapperPath()
    {
        var configuredPath = _options.BootstrapperRelativePath.Trim();
        return Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, _environment.ContentRootPath);
    }

    private BootstrapperLaunchSelection ResolveBootstrapperLaunchSelection(ApplicationUpdateStagingState state, string stagingRoot)
    {
        var installedBootstrapperPath = ResolveBootstrapperPath();
        var stagedZipEntryPath = (_options.StagedReleaseBootstrapperPath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(stagedZipEntryPath) ||
            string.IsNullOrWhiteSpace(state.StagedZipPath) ||
            !File.Exists(state.StagedZipPath))
        {
            return BootstrapperLaunchSelection.InstalledFallback(
                installedBootstrapperPath,
                "Staged release bootstrapper could not be evaluated. Using installed bootstrapper fallback.");
        }

        try
        {
            var applySessionRoot = Path.Combine(stagingRoot, "apply-session", Guid.NewGuid().ToString("N"));
            var extractedBootstrapperPath = ExtractStagedBootstrapper(state.StagedZipPath, stagedZipEntryPath, applySessionRoot);
            return BootstrapperLaunchSelection.StagedRelease(
                extractedBootstrapperPath,
                $"Using staged release bootstrapper '{stagedZipEntryPath}'.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to extract staged release bootstrapper '{StagedReleaseBootstrapperPath}' from '{StagedZipPath}'. Falling back to installed bootstrapper '{InstalledBootstrapperPath}'.",
                stagedZipEntryPath,
                state.StagedZipPath,
                installedBootstrapperPath);
            return BootstrapperLaunchSelection.InstalledFallback(
                installedBootstrapperPath,
                $"Staged bootstrapper extraction failed. Installed fallback selected. {ex.Message}");
        }
    }

    private static string ExtractStagedBootstrapper(string stagedZipPath, string stagedZipEntryPath, string destinationRoot)
    {
        using var archive = ZipFile.OpenRead(stagedZipPath);
        var entry = archive.Entries.FirstOrDefault(candidate =>
            string.Equals(
                candidate.FullName.Replace('\\', '/').TrimStart('/'),
                stagedZipEntryPath,
                StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidOperationException($"Staged release entry '{stagedZipEntryPath}' was not found.");
        }

        Directory.CreateDirectory(destinationRoot);
        var outputPath = Path.Combine(destinationRoot, entry.Name);
        entry.ExtractToFile(outputPath, overwrite: true);
        return outputPath;
    }

    private sealed class BootstrapperLaunchSelection
    {
        public required string BootstrapperPath { get; init; }
        public required string BootstrapperSource { get; init; }
        public required string SelectionMessage { get; init; }

        public static BootstrapperLaunchSelection StagedRelease(string bootstrapperPath, string selectionMessage)
            => new()
            {
                BootstrapperPath = bootstrapperPath,
                BootstrapperSource = "staged_release",
                SelectionMessage = selectionMessage
            };

        public static BootstrapperLaunchSelection InstalledFallback(string bootstrapperPath, string selectionMessage)
            => new()
            {
                BootstrapperPath = bootstrapperPath,
                BootstrapperSource = "installed_fallback",
                SelectionMessage = selectionMessage
            };
    }

    private string ResolveAppPoolName()
    {
        if (!string.IsNullOrWhiteSpace(_options.IisAppPoolName))
        {
            var configured = _options.IisAppPoolName.Trim();
            _logger.LogInformation("Using configured IIS app pool name '{AppPoolName}' from ApplicationUpdater:IisAppPoolName.", configured);
            return configured;
        }

        var appPoolId = Environment.GetEnvironmentVariable("APP_POOL_ID");
        if (string.IsNullOrWhiteSpace(appPoolId))
        {
            _logger.LogWarning("APP_POOL_ID environment variable is not available; app pool identity remains unresolved.");
            return string.Empty;
        }

        var resolved = appPoolId.Trim();
        _logger.LogInformation("Found APP_POOL_ID = '{AppPoolName}'.", resolved);
        return resolved;
    }

    private string ResolveSiteName(string installRootPath, string appPoolName)
    {
        if (!string.IsNullOrWhiteSpace(_options.IisSiteName))
        {
            var configured = _options.IisSiteName.Trim();
            _logger.LogInformation("Using configured IIS site name '{SiteName}' from ApplicationUpdater:IisSiteName.", configured);
            return configured;
        }

        var websiteSiteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
        if (!string.IsNullOrWhiteSpace(websiteSiteName))
        {
            var resolvedFromEnvironment = websiteSiteName.Trim();
            _logger.LogInformation("Resolved IIS site name from WEBSITE_SITE_NAME = '{SiteName}'.", resolvedFromEnvironment);
            return resolvedFromEnvironment;
        }

        _logger.LogInformation("Attempting site resolution via physical path lookup.");
        var resolvedFromPath = ResolveSiteFromPhysicalPath(installRootPath);
        if (!string.IsNullOrWhiteSpace(resolvedFromPath))
        {
            return resolvedFromPath;
        }

        if (!string.IsNullOrWhiteSpace(appPoolName))
        {
            _logger.LogInformation("Attempting site resolution via IIS app-pool mapping.");
            var resolvedFromAppPool = ResolveSiteFromAppPool(appPoolName);
            if (!string.IsNullOrWhiteSpace(resolvedFromAppPool))
            {
                return resolvedFromAppPool;
            }

            _logger.LogWarning(
                "IIS site name could not be determined; using app pool name '{AppPoolName}' as fallback.",
                appPoolName);
            return appPoolName;
        }

        _logger.LogWarning("Could not resolve IIS site name from configuration, environment, or IIS metadata.");
        return string.Empty;
    }

    private string ResolveSiteFromPhysicalPath(string installRootPath)
    {
        var normalizedInstallRootPath = NormalizePath(installRootPath);
        if (_iisMetadataReader.TryResolveSiteNameFromPhysicalPath(normalizedInstallRootPath, out var siteName, out var errorMessage) &&
            !string.IsNullOrWhiteSpace(siteName))
        {
            _logger.LogInformation("Matched IIS site '{SiteName}' via physical path.", siteName);
            return siteName.Trim();
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            _logger.LogWarning("Could not resolve site via IIS physical-path lookup.");
        }
        else
        {
            _logger.LogWarning("Could not resolve site via IIS physical-path lookup: {ErrorMessage}", errorMessage);
        }

        return string.Empty;
    }

    private string ResolveSiteFromAppPool(string appPoolName)
    {
        if (_iisMetadataReader.TryResolveSiteNameFromAppPool(appPoolName, out var siteName, out var errorMessage) &&
            !string.IsNullOrWhiteSpace(siteName))
        {
            _logger.LogInformation("Matched IIS site '{SiteName}' via app pool '{AppPoolName}'.", siteName, appPoolName);
            return siteName.Trim();
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            _logger.LogWarning("Could not resolve site via IIS app-pool lookup for '{AppPoolName}'.", appPoolName);
        }
        else
        {
            _logger.LogWarning("Could not resolve site via IIS app-pool lookup for '{AppPoolName}': {ErrorMessage}", appPoolName, errorMessage);
        }

        return string.Empty;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Length == 0 ? Path.DirectorySeparatorChar.ToString() : fullPath;
    }

    private static ApplicationUpdateStagingState CloneState(
        ApplicationUpdateStagingState source,
        ApplicationUpdateStagingStatus status,
        string? applyOperationMessage,
        DateTimeOffset now,
        DateTimeOffset? applyHandoffStartedAtUtc = null,
        DateTimeOffset? applyCompletedAtUtc = null,
        string? failureMessage = null,
        string? launchPowerShellExecutablePath = null,
        string? launchInstallRootPath = null,
        string? launchWorkingDirectory = null,
        string? launchResolvedSiteName = null,
        string? launchResolvedAppPoolName = null,
        string? launchExpectedReleaseTag = null,
        int? bootstrapperProcessId = null,
        DateTimeOffset? bootstrapperStartedAtUtc = null,
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
            BootstrapperSource = source.BootstrapperSource,
            BootstrapperSelectionMessage = source.BootstrapperSelectionMessage,
            StagedMetadataPath = source.StagedMetadataPath,
            LaunchPowerShellExecutablePath = launchPowerShellExecutablePath ?? source.LaunchPowerShellExecutablePath,
            LaunchInstallRootPath = launchInstallRootPath ?? source.LaunchInstallRootPath,
            LaunchWorkingDirectory = launchWorkingDirectory ?? source.LaunchWorkingDirectory,
            LaunchResolvedSiteName = launchResolvedSiteName ?? source.LaunchResolvedSiteName,
            LaunchResolvedAppPoolName = launchResolvedAppPoolName ?? source.LaunchResolvedAppPoolName,
            LaunchExpectedReleaseTag = launchExpectedReleaseTag ?? source.LaunchExpectedReleaseTag,
            ExternalUpdaterStatusPath = source.ExternalUpdaterStatusPath,
            ExternalUpdaterLogPath = source.ExternalUpdaterLogPath,
            BootstrapperProcessId = bootstrapperProcessId ?? source.BootstrapperProcessId,
            BootstrapperStartedAtUtc = bootstrapperStartedAtUtc ?? source.BootstrapperStartedAtUtc,
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

    private async Task PersistLaunchFailureAsync(ApplicationUpdateStagingState sourceState, string failureDetail, CancellationToken cancellationToken)
    {
        var failedAt = DateTimeOffset.UtcNow;
        var operatorMessage = $"Bootstrapper launch failed: {failureDetail}";
        var failedState = CloneState(
            sourceState,
            ApplicationUpdateStagingStatus.ApplyFailed,
            operatorMessage,
            failedAt,
            applyCompletedAtUtc: failedAt,
            failureMessage: operatorMessage,
            lastKnownUpdaterStage: "bootstrapper_launch_failed",
            lastKnownUpdaterResultCode: "failed");
        await _stagingStateStore.WriteAsync(failedState, cancellationToken);
    }

    private static string? ValidateLaunchInputs(string bootstrapperPath, string stagedMetadataPath, string installRootPath, string powerShellExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(powerShellExecutablePath))
        {
            return $"PowerShell executable was not found at '{powerShellExecutablePath}'.";
        }

        if (string.IsNullOrWhiteSpace(bootstrapperPath) || !File.Exists(bootstrapperPath))
        {
            return $"Bundled updater bootstrapper was not found at '{bootstrapperPath}'.";
        }

        if (string.IsNullOrWhiteSpace(stagedMetadataPath) || !File.Exists(stagedMetadataPath))
        {
            return $"Staged metadata file was not found at '{stagedMetadataPath}'.";
        }

        if (string.IsNullOrWhiteSpace(installRootPath) || !Directory.Exists(installRootPath))
        {
            return $"Bootstrapper working directory was not found at '{installRootPath}'.";
        }

        return null;
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
