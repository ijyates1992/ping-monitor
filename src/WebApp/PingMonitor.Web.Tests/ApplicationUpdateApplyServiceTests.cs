using System.Text.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationMetadata;
using PingMonitor.Web.Services.ApplicationUpdater;
using PingMonitor.Web.Services.StartupGate;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class ApplicationUpdateApplyServiceTests
{
    [Fact]
    public void ExternalUpdaterProcessLauncher_TryLaunch_UsesExecutionPolicyBypassAndFile_WithArgumentList()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var tempRoot = CreateTempRoot();
        try
        {
            var argsCapturePath = Path.Combine(tempRoot, "captured-args.json");
            var executablePath = Path.Combine(tempRoot, "record-args");
            var bootstrapperPath = Path.Combine(tempRoot, "Updater", "run-staged-update-bootstrapper.ps1");
            var installRootPath = Path.Combine(tempRoot, "install root");
            Directory.CreateDirectory(installRootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(bootstrapperPath)!);
            File.WriteAllText(bootstrapperPath, "# bootstrapper");

            var recorderScript = """
                #!/usr/bin/env python3
                import json
                import pathlib
                import sys
                output = pathlib.Path(r'__ARGS_CAPTURE_PATH__')
                output.write_text(json.dumps(sys.argv[1:]))
                """;
            File.WriteAllText(executablePath, recorderScript.Replace("__ARGS_CAPTURE_PATH__", argsCapturePath.Replace("\\", "\\\\")));
            File.SetUnixFileMode(
                executablePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            var launcher = new ExternalUpdaterProcessLauncher();
            var launched = launcher.TryLaunch(
                powerShellExecutablePath: executablePath,
                bootstrapperScriptPath: bootstrapperPath,
                stagedMetadataPath: Path.Combine(tempRoot, "state folder", "staged update.json"),
                installRootPath: installRootPath,
                siteName: "Ping Monitor Site",
                appPoolName: "Ping Monitor Pool",
                statusJsonPath: argsCapturePath,
                logPath: Path.Combine(tempRoot, "logs", "external updater.log"),
                expectedReleaseTag: "V1.2.3",
                out var processId,
                out var launchErrorMessage);

            Assert.True(launched);
            Assert.Null(launchErrorMessage);
            Assert.NotNull(processId);

            var timeoutUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            while (!File.Exists(argsCapturePath) && DateTimeOffset.UtcNow < timeoutUtc)
            {
                Thread.Sleep(50);
            }

            Assert.True(File.Exists(argsCapturePath), "Argument capture file was not written by recorder process.");

            var capturedArgs = JsonSerializer.Deserialize<string[]>(File.ReadAllText(argsCapturePath));
            Assert.NotNull(capturedArgs);
            Assert.Equal("-NoProfile", capturedArgs![0]);
            Assert.Equal("-ExecutionPolicy", capturedArgs[1]);
            Assert.Equal("Bypass", capturedArgs[2]);
            Assert.Equal("-File", capturedArgs[3]);
            Assert.Equal(bootstrapperPath, capturedArgs[4]);
            Assert.Equal("-StagedMetadataPath", capturedArgs[5]);
            Assert.Equal(Path.Combine(tempRoot, "state folder", "staged update.json"), capturedArgs[6]);
            Assert.Equal("-InstallRootPath", capturedArgs[7]);
            Assert.Equal(installRootPath, capturedArgs[8]);
            Assert.Equal("-SiteName", capturedArgs[9]);
            Assert.Equal("Ping Monitor Site", capturedArgs[10]);
            Assert.Equal("-AppPoolName", capturedArgs[11]);
            Assert.Equal("Ping Monitor Pool", capturedArgs[12]);
            Assert.Equal("-StatusJsonPath", capturedArgs[13]);
            Assert.Equal(argsCapturePath, capturedArgs[14]);
            Assert.Equal("-LogPath", capturedArgs[15]);
            Assert.Equal(Path.Combine(tempRoot, "logs", "external updater.log"), capturedArgs[16]);
            Assert.Equal("-ExpectedReleaseTag", capturedArgs[17]);
            Assert.Equal("V1.2.3", capturedArgs[18]);

            if (processId is int launchedProcessId)
            {
                try
                {
                    using var process = Process.GetProcessById(launchedProcessId);
                    process.WaitForExit(5000);
                }
                catch (ArgumentException)
                {
                    // Process may already have exited between capture and lookup.
                }
            }
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RequestApplyAsync_UsesExplicitResolvedPaths_AndWritesHandoffState()
    {
        var contentRoot = CreateTempRoot();
        try
        {
            var stagingRoot = Path.Combine(contentRoot, "App_Data", "Updater");
            Directory.CreateDirectory(Path.Combine(stagingRoot, "state"));
            Directory.CreateDirectory(Path.Combine(contentRoot, "Updater"));

            var bootstrapperPath = Path.Combine(contentRoot, "Updater", "run-staged-update-bootstrapper.ps1");
            await File.WriteAllTextAsync(bootstrapperPath, "# bootstrapper");

            var zipPath = Path.Combine(stagingRoot, "staged", "current", "pkg.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            await File.WriteAllTextAsync(zipPath, "zip");

            var metadataPath = Path.Combine(stagingRoot, "state", "staged-update.json");
            await File.WriteAllTextAsync(metadataPath, "{}");

            var store = BuildStateStore(contentRoot);
            await store.WriteAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = "ijyates1992/ping-monitor",
                ReleaseTag = "V1.2.3",
                StagedZipPath = zipPath,
                ChecksumVerified = true,
                Status = ApplicationUpdateStagingStatus.Ready,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);

            var launcher = new RecordingLauncher();
            var service = BuildService(contentRoot, store, launcher);

            var result = await service.RequestApplyAsync("admin-user", CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(ApplicationUpdateStagingStatus.ApplyHandoffStarted, result!.Status);
            Assert.Equal("admin-user", result.LastApplyRequestedByUserId);
            Assert.Equal(bootstrapperPath, launcher.BootstrapperScriptPath);
            Assert.Equal(metadataPath, launcher.StagedMetadataPath);
            Assert.Equal(contentRoot, launcher.InstallRootPath);
            Assert.Equal(Path.Combine(stagingRoot, "state", "external-updater-status.json"), launcher.StatusJsonPath);
            Assert.Equal(Path.Combine(stagingRoot, "state", "external-updater.log"), launcher.LogPath);
            Assert.Equal("C:\\Program Files\\PowerShell\\7\\pwsh.exe", launcher.PowerShellExecutablePath);
            Assert.Equal("PingMonitor", launcher.SiteName);
            Assert.Equal("PingMonitorPool", launcher.AppPoolName);
            Assert.Equal("bootstrapper_started", result.LastKnownUpdaterStage);
            Assert.Equal(1234, result.BootstrapperProcessId);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshApplyStateAsync_MapsCompletedSuccessToApplySucceeded()
    {
        var contentRoot = CreateTempRoot();
        try
        {
            var stagingRoot = Path.Combine(contentRoot, "App_Data", "Updater");
            Directory.CreateDirectory(Path.Combine(stagingRoot, "state"));

            var statusPath = Path.Combine(stagingRoot, "state", "external-updater-status.json");
            await File.WriteAllTextAsync(statusPath, JsonSerializer.Serialize(new
            {
                stage = "completed",
                succeeded = true,
                resultCode = "success",
                completedAtUtc = DateTimeOffset.UtcNow.ToString("O")
            }));

            var store = BuildStateStore(contentRoot);
            await store.WriteAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = "ijyates1992/ping-monitor",
                ReleaseTag = "V1.2.3",
                ChecksumVerified = true,
                Status = ApplicationUpdateStagingStatus.ApplyHandoffStarted,
                ExternalUpdaterStatusPath = statusPath,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);

            var service = BuildService(
                contentRoot,
                store,
                new RecordingLauncher(),
                currentVersion: "V1.2.3",
                startupMode: StartupMode.Normal);

            var refreshed = await service.RefreshApplyStateAsync(CancellationToken.None);

            Assert.NotNull(refreshed);
            Assert.Equal(ApplicationUpdateStagingStatus.ApplySucceeded, refreshed!.Status);
            Assert.Equal("completed", refreshed.LastKnownUpdaterStage);
            Assert.Equal("success", refreshed.LastKnownUpdaterResultCode);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RequestApplyAsync_ThrowsWhenPowerShellHostCannotBeResolved()
    {
        var contentRoot = CreateTempRoot();
        try
        {
            var stagingRoot = Path.Combine(contentRoot, "App_Data", "Updater");
            Directory.CreateDirectory(Path.Combine(stagingRoot, "state"));
            Directory.CreateDirectory(Path.Combine(contentRoot, "Updater"));

            var bootstrapperPath = Path.Combine(contentRoot, "Updater", "run-staged-update-bootstrapper.ps1");
            await File.WriteAllTextAsync(bootstrapperPath, "# bootstrapper");

            var zipPath = Path.Combine(stagingRoot, "staged", "current", "pkg.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            await File.WriteAllTextAsync(zipPath, "zip");

            var metadataPath = Path.Combine(stagingRoot, "state", "staged-update.json");
            await File.WriteAllTextAsync(metadataPath, "{}");

            var store = BuildStateStore(contentRoot);
            await store.WriteAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = "ijyates1992/ping-monitor",
                ReleaseTag = "V1.2.3",
                StagedZipPath = zipPath,
                ChecksumVerified = true,
                Status = ApplicationUpdateStagingStatus.Ready,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);

            var launcher = new RecordingLauncher();
            var service = BuildService(
                contentRoot,
                store,
                launcher,
                powerShellPrerequisiteDetector: new StubPowerShellPrerequisiteDetector { IsAvailable = false });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestApplyAsync("admin-user", CancellationToken.None));
            Assert.Contains("PowerShell 7 (`pwsh`) was not found in PATH", exception.Message);
            var failed = await store.ReadAsync(CancellationToken.None);
            Assert.NotNull(failed);
            Assert.Equal(ApplicationUpdateStagingStatus.ApplyFailed, failed!.Status);
            Assert.Equal("bootstrapper_launch_failed", failed.LastKnownUpdaterStage);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RequestApplyAsync_UsesAppPoolFallbackForSiteWhenIisLookupsDoNotResolveSite()
    {
        var contentRoot = CreateTempRoot();
        try
        {
            var stagingRoot = Path.Combine(contentRoot, "App_Data", "Updater");
            Directory.CreateDirectory(Path.Combine(stagingRoot, "state"));
            Directory.CreateDirectory(Path.Combine(contentRoot, "Updater"));

            var bootstrapperPath = Path.Combine(contentRoot, "Updater", "run-staged-update-bootstrapper.ps1");
            await File.WriteAllTextAsync(bootstrapperPath, "# bootstrapper");

            var zipPath = Path.Combine(stagingRoot, "staged", "current", "pkg.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            await File.WriteAllTextAsync(zipPath, "zip");

            var metadataPath = Path.Combine(stagingRoot, "state", "staged-update.json");
            await File.WriteAllTextAsync(metadataPath, "{}");

            var store = BuildStateStore(contentRoot);
            await store.WriteAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = "ijyates1992/ping-monitor",
                ReleaseTag = "V1.2.3",
                StagedZipPath = zipPath,
                ChecksumVerified = true,
                Status = ApplicationUpdateStagingStatus.Ready,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);

            var launcher = new RecordingLauncher();
            var iisReader = new StubIisMetadataReader
            {
                SiteNameFromPhysicalPath = null,
                SiteNameFromAppPool = null
            };

            Environment.SetEnvironmentVariable("APP_POOL_ID", "DetectedPool");
            var service = BuildService(contentRoot, store, launcher, iisSiteName: "", iisAppPoolName: "", iisMetadataReader: iisReader);

            var result = await service.RequestApplyAsync("admin-user", CancellationToken.None);
            Assert.NotNull(result);
            Assert.Equal("DetectedPool", launcher.AppPoolName);
            Assert.Equal("DetectedPool", launcher.SiteName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("APP_POOL_ID", null);
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RequestApplyAsync_UsesCustomSiteMappedFromAppPool()
    {
        var contentRoot = CreateTempRoot();
        try
        {
            var stagingRoot = Path.Combine(contentRoot, "App_Data", "Updater");
            Directory.CreateDirectory(Path.Combine(stagingRoot, "state"));
            Directory.CreateDirectory(Path.Combine(contentRoot, "Updater"));

            var bootstrapperPath = Path.Combine(contentRoot, "Updater", "run-staged-update-bootstrapper.ps1");
            await File.WriteAllTextAsync(bootstrapperPath, "# bootstrapper");

            var zipPath = Path.Combine(stagingRoot, "staged", "current", "pkg.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            await File.WriteAllTextAsync(zipPath, "zip");

            var metadataPath = Path.Combine(stagingRoot, "state", "staged-update.json");
            await File.WriteAllTextAsync(metadataPath, "{}");

            var store = BuildStateStore(contentRoot);
            await store.WriteAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = "ijyates1992/ping-monitor",
                ReleaseTag = "V1.2.3",
                StagedZipPath = zipPath,
                ChecksumVerified = true,
                Status = ApplicationUpdateStagingStatus.Ready,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);

            var launcher = new RecordingLauncher();
            var iisReader = new StubIisMetadataReader { SiteNameFromAppPool = "CustomSiteName" };

            Environment.SetEnvironmentVariable("APP_POOL_ID", "CustomPoolName");
            var service = BuildService(contentRoot, store, launcher, iisSiteName: "", iisAppPoolName: "", iisMetadataReader: iisReader);

            var result = await service.RequestApplyAsync("admin-user", CancellationToken.None);
            Assert.NotNull(result);
            Assert.Equal("CustomPoolName", launcher.AppPoolName);
            Assert.Equal("CustomSiteName", launcher.SiteName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("APP_POOL_ID", null);
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RequestApplyAsync_ResolvesStandardIisInstallIdentityWhenConfigMissing()
    {
        var contentRoot = CreateTempRoot();
        try
        {
            var stagingRoot = Path.Combine(contentRoot, "App_Data", "Updater");
            Directory.CreateDirectory(Path.Combine(stagingRoot, "state"));
            Directory.CreateDirectory(Path.Combine(contentRoot, "Updater"));

            var bootstrapperPath = Path.Combine(contentRoot, "Updater", "run-staged-update-bootstrapper.ps1");
            await File.WriteAllTextAsync(bootstrapperPath, "# bootstrapper");

            var zipPath = Path.Combine(stagingRoot, "staged", "current", "pkg.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            await File.WriteAllTextAsync(zipPath, "zip");

            var metadataPath = Path.Combine(stagingRoot, "state", "staged-update.json");
            await File.WriteAllTextAsync(metadataPath, "{}");

            var store = BuildStateStore(contentRoot);
            await store.WriteAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = "ijyates1992/ping-monitor",
                ReleaseTag = "V1.2.3",
                StagedZipPath = zipPath,
                ChecksumVerified = true,
                Status = ApplicationUpdateStagingStatus.Ready,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);

            var launcher = new RecordingLauncher();
            var iisReader = new StubIisMetadataReader { SiteNameFromPhysicalPath = "PingMonitor" };
            Environment.SetEnvironmentVariable("APP_POOL_ID", "PingMonitor");

            var service = BuildService(contentRoot, store, launcher, iisSiteName: "", iisAppPoolName: "", iisMetadataReader: iisReader);
            var result = await service.RequestApplyAsync("admin-user", CancellationToken.None);
            Assert.NotNull(result);
            Assert.Equal("PingMonitor", launcher.SiteName);
            Assert.Equal("PingMonitor", launcher.AppPoolName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("APP_POOL_ID", null);
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RequestApplyAsync_ThrowsWhenIisIdentityCannotBeResolvedInNonIisContext()
    {
        var contentRoot = CreateTempRoot();
        try
        {
            var stagingRoot = Path.Combine(contentRoot, "App_Data", "Updater");
            Directory.CreateDirectory(Path.Combine(stagingRoot, "state"));
            Directory.CreateDirectory(Path.Combine(contentRoot, "Updater"));

            var bootstrapperPath = Path.Combine(contentRoot, "Updater", "run-staged-update-bootstrapper.ps1");
            await File.WriteAllTextAsync(bootstrapperPath, "# bootstrapper");

            var zipPath = Path.Combine(stagingRoot, "staged", "current", "pkg.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            await File.WriteAllTextAsync(zipPath, "zip");

            var metadataPath = Path.Combine(stagingRoot, "state", "staged-update.json");
            await File.WriteAllTextAsync(metadataPath, "{}");

            var store = BuildStateStore(contentRoot);
            await store.WriteAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = "ijyates1992/ping-monitor",
                ReleaseTag = "V1.2.3",
                StagedZipPath = zipPath,
                ChecksumVerified = true,
                Status = ApplicationUpdateStagingStatus.Ready,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);

            var launcher = new RecordingLauncher();
            var iisReader = new StubIisMetadataReader
            {
                SiteNameFromPhysicalPath = null,
                SiteNameFromAppPool = null,
                Error = "No IIS runtime context."
            };

            Environment.SetEnvironmentVariable("APP_POOL_ID", null);
            Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", null);

            var service = BuildService(contentRoot, store, launcher, iisSiteName: "", iisAppPoolName: "", iisMetadataReader: iisReader);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestApplyAsync("admin-user", CancellationToken.None));
            Assert.Contains("IIS identity resolution failed", exception.Message);
            Assert.Contains("ApplicationUpdater:IisSiteName", exception.Message);
            Assert.Contains("ApplicationUpdater:IisAppPoolName", exception.Message);
            var failed = await store.ReadAsync(CancellationToken.None);
            Assert.NotNull(failed);
            Assert.Equal(ApplicationUpdateStagingStatus.ApplyFailed, failed!.Status);
            Assert.Equal("bootstrapper_launch_failed", failed.LastKnownUpdaterStage);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RequestApplyAsync_PersistsLaunchFailureDetailsWhenProcessLaunchFails()
    {
        var contentRoot = CreateTempRoot();
        try
        {
            var stagingRoot = Path.Combine(contentRoot, "App_Data", "Updater");
            Directory.CreateDirectory(Path.Combine(stagingRoot, "state"));
            Directory.CreateDirectory(Path.Combine(contentRoot, "Updater"));

            var bootstrapperPath = Path.Combine(contentRoot, "Updater", "run-staged-update-bootstrapper.ps1");
            await File.WriteAllTextAsync(bootstrapperPath, "# bootstrapper");

            var zipPath = Path.Combine(stagingRoot, "staged", "current", "pkg.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            await File.WriteAllTextAsync(zipPath, "zip");

            var metadataPath = Path.Combine(stagingRoot, "state", "staged-update.json");
            await File.WriteAllTextAsync(metadataPath, "{}");

            var store = BuildStateStore(contentRoot);
            await store.WriteAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = "ijyates1992/ping-monitor",
                ReleaseTag = "V1.2.3",
                StagedZipPath = zipPath,
                ChecksumVerified = true,
                Status = ApplicationUpdateStagingStatus.Ready,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);

            var launcher = new RecordingLauncher
            {
                LaunchResult = false,
                LaunchErrorMessage = "Process.Start returned null."
            };

            var service = BuildService(contentRoot, store, launcher);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestApplyAsync("admin-user", CancellationToken.None));
            Assert.Contains("Failed to launch external updater process", exception.Message);

            var failed = await store.ReadAsync(CancellationToken.None);
            Assert.NotNull(failed);
            Assert.Equal(ApplicationUpdateStagingStatus.ApplyFailed, failed!.Status);
            Assert.Equal("bootstrapper_launch_failed", failed.LastKnownUpdaterStage);
            Assert.Contains("Process.Start returned null.", failed.FailureMessage);
            Assert.Equal("C:\\Program Files\\PowerShell\\7\\pwsh.exe", failed.LaunchPowerShellExecutablePath);
            Assert.Equal(contentRoot, failed.LaunchWorkingDirectory);
            Assert.Equal("PingMonitor", failed.LaunchResolvedSiteName);
            Assert.Equal("PingMonitorPool", failed.LaunchResolvedAppPoolName);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RequestApplyAsync_DoesNotOverwriteStateWhenApplyPrerequisitesFail()
    {
        var contentRoot = CreateTempRoot();
        try
        {
            var stagingRoot = Path.Combine(contentRoot, "App_Data", "Updater");
            Directory.CreateDirectory(Path.Combine(stagingRoot, "state"));
            Directory.CreateDirectory(Path.Combine(contentRoot, "Updater"));

            var bootstrapperPath = Path.Combine(contentRoot, "Updater", "run-staged-update-bootstrapper.ps1");
            await File.WriteAllTextAsync(bootstrapperPath, "# bootstrapper");

            var metadataPath = Path.Combine(stagingRoot, "state", "staged-update.json");
            await File.WriteAllTextAsync(metadataPath, "{}");

            var store = BuildStateStore(contentRoot);
            var previousState = new ApplicationUpdateStagingState
            {
                SourceRepository = "ijyates1992/ping-monitor",
                ReleaseTag = "V1.2.3",
                StagedZipPath = Path.Combine(stagingRoot, "staged", "current", "missing.zip"),
                ChecksumVerified = true,
                Status = ApplicationUpdateStagingStatus.ApplyFailed,
                FailureMessage = "Previous failure context",
                LastKnownUpdaterStage = "bootstrapper_launch_failed",
                LastKnownUpdaterResultCode = "error",
                LastApplyRequestedByUserId = "existing-user",
                ApplyRequestedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3),
                LastUpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            };
            await store.WriteAsync(previousState, CancellationToken.None);

            var service = BuildService(contentRoot, store, new RecordingLauncher());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestApplyAsync("admin-user", CancellationToken.None));
            Assert.Contains("A staged update is not ready for apply.", exception.Message);

            var afterFailure = await store.ReadAsync(CancellationToken.None);
            Assert.NotNull(afterFailure);
            Assert.Equal(previousState.Status, afterFailure!.Status);
            Assert.Equal(previousState.FailureMessage, afterFailure.FailureMessage);
            Assert.Equal(previousState.LastKnownUpdaterStage, afterFailure.LastKnownUpdaterStage);
            Assert.Equal(previousState.LastKnownUpdaterResultCode, afterFailure.LastKnownUpdaterResultCode);
            Assert.Equal(previousState.LastApplyRequestedByUserId, afterFailure.LastApplyRequestedByUserId);
            Assert.Equal(previousState.ApplyRequestedAtUtc, afterFailure.ApplyRequestedAtUtc);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static ApplicationUpdateApplyService BuildService(
        string contentRoot,
        IApplicationUpdateStagingStateStore store,
        IExternalUpdaterProcessLauncher launcher,
        IPowerShellPrerequisiteDetector? powerShellPrerequisiteDetector = null,
        string currentVersion = "V1.0.0",
        StartupMode startupMode = StartupMode.Normal,
        string iisSiteName = "PingMonitor",
        string iisAppPoolName = "PingMonitorPool",
        IIisMetadataReader? iisMetadataReader = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ApplicationUpdaterOptions
        {
            StagingStoragePath = "App_Data/Updater",
            BootstrapperRelativePath = "Updater/run-staged-update-bootstrapper.ps1",
            PowerShellExecutablePath = "pwsh.exe",
            IisSiteName = iisSiteName,
            IisAppPoolName = iisAppPoolName
        });

        return new ApplicationUpdateApplyService(
            store,
            new StubMetadataProvider(currentVersion),
            new StubStartupGateRuntimeState(startupMode),
            powerShellPrerequisiteDetector ?? new StubPowerShellPrerequisiteDetector(),
            launcher,
            new StubWebHostEnvironment(contentRoot),
            options,
            NullLogger<ApplicationUpdateApplyService>.Instance,
            iisMetadataReader);
    }

    private sealed class StubIisMetadataReader : IIisMetadataReader
    {
        public string? SiteNameFromPhysicalPath { get; set; }
        public string? SiteNameFromAppPool { get; set; }
        public string? Error { get; set; }

        public bool TryResolveSiteNameFromPhysicalPath(string normalizedInstallRootPath, out string? siteName, out string? errorMessage)
        {
            siteName = SiteNameFromPhysicalPath;
            errorMessage = Error;
            return !string.IsNullOrWhiteSpace(siteName);
        }

        public bool TryResolveSiteNameFromAppPool(string appPoolName, out string? siteName, out string? errorMessage)
        {
            siteName = SiteNameFromAppPool;
            errorMessage = Error;
            return !string.IsNullOrWhiteSpace(siteName);
        }
    }

    private sealed class StubPowerShellPrerequisiteDetector : IPowerShellPrerequisiteDetector
    {
        public bool IsAvailable { get; set; } = true;
        public string ResolvedPath { get; set; } = "C:\\Program Files\\PowerShell\\7\\pwsh.exe";

        public PowerShellPrerequisiteStatus GetStatus()
        {
            return new PowerShellPrerequisiteStatus
            {
                IsAvailable = IsAvailable,
                ConfiguredExecutablePath = "pwsh.exe",
                ResolvedExecutablePath = IsAvailable ? ResolvedPath : null,
                Message = IsAvailable
                    ? "PowerShell 7 prerequisite check passed."
                    : "PowerShell 7 (`pwsh`) was not found in PATH. Update apply operations are unavailable until PowerShell 7 is installed and accessible to the application."
            };
        }
    }

    private static ApplicationUpdateStagingStateStore BuildStateStore(string contentRoot)
    {
        return new ApplicationUpdateStagingStateStore(
            new StubWebHostEnvironment(contentRoot),
            Microsoft.Extensions.Options.Options.Create(new ApplicationUpdaterOptions { StagingStoragePath = "App_Data/Updater" }));
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "ping-monitor-stage4-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingLauncher : IExternalUpdaterProcessLauncher
    {
        public bool ResolveExecutablePathResult { get; set; } = true;
        public string? ResolutionErrorMessage { get; set; }
        public string? PowerShellExecutablePath { get; private set; }
        public string? BootstrapperScriptPath { get; private set; }
        public string? StagedMetadataPath { get; private set; }
        public string? InstallRootPath { get; private set; }
        public string? StatusJsonPath { get; private set; }
        public string? LogPath { get; private set; }
        public string? SiteName { get; private set; }
        public string? AppPoolName { get; private set; }
        public bool LaunchResult { get; set; } = true;
        public string? LaunchErrorMessage { get; set; }
        public int? ProcessIdToReturn { get; set; } = 1234;

        public bool TryResolveExecutablePath(string configuredExecutablePath, out string? resolvedExecutablePath, out string? resolutionErrorMessage)
        {
            resolvedExecutablePath = ResolveExecutablePathResult ? "C:\\Program Files\\PowerShell\\7\\pwsh.exe" : null;
            resolutionErrorMessage = ResolutionErrorMessage;
            return ResolveExecutablePathResult;
        }

        public bool TryLaunch(string powerShellExecutablePath, string bootstrapperScriptPath, string stagedMetadataPath, string installRootPath, string siteName, string appPoolName, string statusJsonPath, string logPath, string? expectedReleaseTag, out int? processId, out string? launchErrorMessage)
        {
            PowerShellExecutablePath = powerShellExecutablePath;
            BootstrapperScriptPath = bootstrapperScriptPath;
            StagedMetadataPath = stagedMetadataPath;
            InstallRootPath = installRootPath;
            SiteName = siteName;
            AppPoolName = appPoolName;
            StatusJsonPath = statusJsonPath;
            LogPath = logPath;
            processId = ProcessIdToReturn;
            launchErrorMessage = LaunchErrorMessage;
            return LaunchResult;
        }
    }

    private sealed class StubMetadataProvider : IApplicationMetadataProvider
    {
        private readonly string _version;

        public StubMetadataProvider(string version)
        {
            _version = version;
        }

        public ApplicationMetadataSnapshot GetSnapshot()
        {
            return new ApplicationMetadataSnapshot { Version = _version };
        }
    }

    private sealed class StubStartupGateRuntimeState : IStartupGateRuntimeState
    {
        public StubStartupGateRuntimeState(StartupMode mode)
        {
            CurrentMode = mode;
        }

        public StartupMode CurrentMode { get; }

        public bool IsOperationalMode => CurrentMode == StartupMode.Normal;

        public void Update(StartupGateStatus status)
        {
        }
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ApplicationName = "PingMonitor.Web.Tests";
            EnvironmentName = "Development";
            WebRootPath = contentRootPath;
            ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
            WebRootFileProvider = new PhysicalFileProvider(contentRootPath);
        }

        public string ApplicationName { get; set; }

        public IFileProvider WebRootFileProvider { get; set; }

        public string WebRootPath { get; set; }

        public string EnvironmentName { get; set; }

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
