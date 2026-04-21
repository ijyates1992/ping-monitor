using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.ApplicationUpdater;

public interface IApplicationUpdateStagingService
{
    Task<ApplicationUpdateStagingResult> StageLatestApplicableReleaseAsync(bool allowPreviewReleases, CancellationToken cancellationToken);
}

internal sealed partial class ApplicationUpdateStagingService : IApplicationUpdateStagingService
{
    private const string StagedCurrentFolderName = "current";
    private const string StageLockFileName = "stage.lock";

    private readonly IGitHubReleaseLookupService _gitHubReleaseLookupService;
    private readonly IApplicationUpdateStagingStateStore _stagingStateStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApplicationUpdaterOptions _options;

    public ApplicationUpdateStagingService(
        IGitHubReleaseLookupService gitHubReleaseLookupService,
        IApplicationUpdateStagingStateStore stagingStateStore,
        IHttpClientFactory httpClientFactory,
        IOptions<ApplicationUpdaterOptions> options)
    {
        _gitHubReleaseLookupService = gitHubReleaseLookupService;
        _stagingStateStore = stagingStateStore;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<ApplicationUpdateStagingResult> StageLatestApplicableReleaseAsync(bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        var repository = $"{_options.GitHubOwner}/{_options.GitHubRepository}";
        var now = DateTimeOffset.UtcNow;
        var currentState = await _stagingStateStore.ReadAsync(cancellationToken);
        var stagingRoot = _stagingStateStore.GetStagingRootPath();

        var lockPath = Path.Combine(stagingRoot, StageLockFileName);
        var lockHandle = TryAcquireStageLock(lockPath);
        if (lockHandle is null)
        {
            return await WriteAndReturnAsync(BuildState(
                repository,
                allowPreviewReleases,
                ApplicationUpdateStagingStatus.StagingBlocked,
                "A staging operation is already running. Please wait for it to complete and try again.",
                now,
                currentState), cancellationToken);
        }

        await using var _ = lockHandle;
        await WriteAndReturnAsync(BuildState(
            repository,
            allowPreviewReleases,
            ApplicationUpdateStagingStatus.StagingInProgress,
            "Staging operation started. Download and checksum verification are in progress.",
            now,
            currentState,
            stagingInProgress: true), cancellationToken);

        if (!_options.UpdateChecksEnabled)
        {
            return await WriteAndReturnAsync(BuildState(
                repository,
                allowPreviewReleases,
                ApplicationUpdateStagingStatus.StagingFailed,
                "Update checks are disabled by configuration.",
                DateTimeOffset.UtcNow,
                currentState), cancellationToken);
        }

        GitHubReleaseSummary? release;
        try
        {
            release = await _gitHubReleaseLookupService.GetLatestApplicableReleaseAsync(allowPreviewReleases, cancellationToken);
        }
        catch (Exception ex)
        {
            return await WriteAndReturnAsync(BuildState(
                repository,
                allowPreviewReleases,
                ApplicationUpdateStagingStatus.StagingFailed,
                $"Unable to resolve latest applicable release: {ex.Message}",
                DateTimeOffset.UtcNow,
                currentState), cancellationToken);
        }

        if (release is null)
        {
            return await WriteAndReturnAsync(BuildState(
                repository,
                allowPreviewReleases,
                ApplicationUpdateStagingStatus.NoApplicableRelease,
                "No applicable GitHub release was found using the current preview-release filter.",
                DateTimeOffset.UtcNow,
                currentState), cancellationToken);
        }

        if (CanReuseCurrentStage(currentState, release.TagName) &&
            await ValidateExistingStagedArtifactsAsync(currentState!, cancellationToken))
        {
            return await WriteAndReturnAsync(BuildState(
                repository,
                allowPreviewReleases,
                ApplicationUpdateStagingStatus.Ready,
                $"Release {release.TagName} is already staged and verified. No staging changes were required.",
                DateTimeOffset.UtcNow,
                currentState,
                latestApplicableReleaseTag: release.TagName,
                stageOperationWasNoOp: true), cancellationToken);
        }

        var zipName = BuildExpectedZipAssetName(release.TagName);
        var zipAsset = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, zipName, StringComparison.OrdinalIgnoreCase));
        if (zipAsset is null || string.IsNullOrWhiteSpace(zipAsset.BrowserDownloadUrl))
        {
            return await WriteAndReturnAsync(BuildAssetFailureState(repository, allowPreviewReleases, release, $"Release zip asset '{zipName}' was not found.", currentState), cancellationToken);
        }

        var checksumAsset = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, _options.ChecksumAssetName, StringComparison.OrdinalIgnoreCase));
        if (checksumAsset is null || string.IsNullOrWhiteSpace(checksumAsset.BrowserDownloadUrl))
        {
            return await WriteAndReturnAsync(BuildAssetFailureState(repository, allowPreviewReleases, release, $"Checksum asset '{_options.ChecksumAssetName}' was not found.", currentState), cancellationToken);
        }

        var downloadsPath = Path.Combine(stagingRoot, "staged", StagedCurrentFolderName);
        var zipPath = Path.Combine(downloadsPath, zipAsset.Name);
        var checksumPath = Path.Combine(downloadsPath, checksumAsset.Name);

        try
        {
            if (Directory.Exists(downloadsPath))
            {
                Directory.Delete(downloadsPath, recursive: true);
            }

            Directory.CreateDirectory(downloadsPath);
            await DownloadToFileAsync(zipAsset.BrowserDownloadUrl!, zipPath, cancellationToken);
            await DownloadToFileAsync(checksumAsset.BrowserDownloadUrl!, checksumPath, cancellationToken);
        }
        catch (Exception ex)
        {
            return await WriteAndReturnAsync(BuildState(
                repository,
                allowPreviewReleases,
                ApplicationUpdateStagingStatus.DownloadFailed,
                $"Failed to download release assets: {ex.Message}",
                DateTimeOffset.UtcNow,
                currentState,
                release,
                zipAsset.Name,
                checksumAsset.Name,
                zipPath,
                checksumPath,
                latestApplicableReleaseTag: release.TagName), cancellationToken);
        }

        string expectedHash;
        string actualHash;
        try
        {
            expectedHash = await ReadExpectedSha256Async(checksumPath, zipAsset.Name, cancellationToken);
            actualHash = await ComputeSha256Async(zipPath, cancellationToken);
        }
        catch (Exception ex)
        {
            return await WriteAndReturnAsync(BuildState(
                repository,
                allowPreviewReleases,
                ApplicationUpdateStagingStatus.ChecksumVerificationFailed,
                $"Unable to verify checksum: {ex.Message}",
                DateTimeOffset.UtcNow,
                currentState,
                release,
                zipAsset.Name,
                checksumAsset.Name,
                zipPath,
                checksumPath,
                latestApplicableReleaseTag: release.TagName), cancellationToken);
        }

        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            return await WriteAndReturnAsync(BuildState(
                repository,
                allowPreviewReleases,
                ApplicationUpdateStagingStatus.ChecksumVerificationFailed,
                "Checksum verification failed. The staged package hash does not match SHA256.txt.",
                DateTimeOffset.UtcNow,
                currentState,
                release,
                zipAsset.Name,
                checksumAsset.Name,
                zipPath,
                checksumPath,
                expectedHash,
                actualHash,
                false,
                latestApplicableReleaseTag: release.TagName), cancellationToken);
        }

        return await WriteAndReturnAsync(BuildState(
            repository,
            allowPreviewReleases,
            ApplicationUpdateStagingStatus.Ready,
            $"Release {release.TagName} was downloaded, checksum-verified, and staged.",
            DateTimeOffset.UtcNow,
            currentState,
            release,
            zipAsset.Name,
            checksumAsset.Name,
            zipPath,
            checksumPath,
            expectedHash,
            actualHash,
            true,
            DateTimeOffset.UtcNow,
            latestApplicableReleaseTag: release.TagName), cancellationToken);
    }

    private static FileStream? TryAcquireStageLock(string lockPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        try
        {
            return new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool CanReuseCurrentStage(ApplicationUpdateStagingState? currentState, string releaseTag)
    {
        return currentState?.Status == ApplicationUpdateStagingStatus.Ready
               && string.Equals(currentState.ReleaseTag, releaseTag, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> ValidateExistingStagedArtifactsAsync(ApplicationUpdateStagingState currentState, CancellationToken cancellationToken)
    {
        if (!currentState.ChecksumVerified
            || string.IsNullOrWhiteSpace(currentState.StagedZipPath)
            || string.IsNullOrWhiteSpace(currentState.StagedChecksumPath)
            || string.IsNullOrWhiteSpace(currentState.ExpectedSha256))
        {
            return false;
        }

        if (!File.Exists(currentState.StagedZipPath) || !File.Exists(currentState.StagedChecksumPath))
        {
            return false;
        }

        var actual = await ComputeSha256Async(currentState.StagedZipPath, cancellationToken);
        return string.Equals(actual, currentState.ExpectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = SHA256.Create();
        var bytes = await hash.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(bytes);
    }

    private static async Task<string> ReadExpectedSha256Async(string checksumPath, string expectedFileName, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(checksumPath, cancellationToken);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var match = Sha256LineRegex().Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            var hash = match.Groups[1].Value.Trim();
            var fileName = match.Groups[2].Value.Trim();
            if (string.Equals(fileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                return hash.ToUpperInvariant();
            }
        }

        throw new InvalidOperationException($"Checksum file did not include an entry for '{expectedFileName}'.");
    }

    private string BuildExpectedZipAssetName(string releaseTag)
    {
        return $"{_options.ReleasePackagePrefix}-{releaseTag}-{_options.RuntimeIdentifier}.zip";
    }

    private async Task<ApplicationUpdateStagingResult> WriteAndReturnAsync(ApplicationUpdateStagingState state, CancellationToken cancellationToken)
    {
        await _stagingStateStore.WriteAsync(state, cancellationToken);
        return new ApplicationUpdateStagingResult { State = state };
    }

    private ApplicationUpdateStagingState BuildAssetFailureState(string repository, bool allowPreviewReleases, GitHubReleaseSummary release, string message, ApplicationUpdateStagingState? currentState)
    {
        return BuildState(
            repository,
            allowPreviewReleases,
            ApplicationUpdateStagingStatus.AssetResolutionFailed,
            message,
            DateTimeOffset.UtcNow,
            currentState,
            release,
            latestApplicableReleaseTag: release.TagName);
    }

    private ApplicationUpdateStagingState BuildState(
        string repository,
        bool allowPreviewReleases,
        ApplicationUpdateStagingStatus status,
        string? operationMessage,
        DateTimeOffset now,
        ApplicationUpdateStagingState? previous,
        GitHubReleaseSummary? release = null,
        string? selectedAssetName = null,
        string? selectedChecksumAssetName = null,
        string? stagedZipPath = null,
        string? stagedChecksumPath = null,
        string? expectedSha256 = null,
        string? actualSha256 = null,
        bool? checksumVerified = null,
        DateTimeOffset? stagedAtUtc = null,
        string? latestApplicableReleaseTag = null,
        bool stagingInProgress = false,
        bool stageOperationWasNoOp = false)
    {
        var resolvedReleaseTag = release?.TagName ?? previous?.ReleaseTag;
        var resolvedLatestTag = latestApplicableReleaseTag ?? previous?.LatestApplicableReleaseTag ?? resolvedReleaseTag;
        return new ApplicationUpdateStagingState
        {
            SourceRepository = repository,
            AllowPreviewReleases = allowPreviewReleases,
            ReleaseTag = resolvedReleaseTag,
            ReleaseTitle = release?.Name ?? previous?.ReleaseTitle,
            ReleaseIsPrerelease = release?.IsPrerelease ?? previous?.ReleaseIsPrerelease,
            ReleasePublishedAtUtc = release?.PublishedAtUtc ?? previous?.ReleasePublishedAtUtc,
            ReleaseUrl = release?.HtmlUrl ?? previous?.ReleaseUrl,
            SelectedAssetName = selectedAssetName ?? previous?.SelectedAssetName,
            SelectedChecksumAssetName = selectedChecksumAssetName ?? previous?.SelectedChecksumAssetName,
            StagedZipPath = stagedZipPath ?? previous?.StagedZipPath,
            StagedChecksumPath = stagedChecksumPath ?? previous?.StagedChecksumPath,
            ExpectedSha256 = expectedSha256 ?? previous?.ExpectedSha256,
            ActualSha256 = actualSha256 ?? previous?.ActualSha256,
            ChecksumVerified = checksumVerified ?? previous?.ChecksumVerified ?? false,
            StagedAtUtc = stagedAtUtc ?? previous?.StagedAtUtc,
            StagingInProgress = stagingInProgress,
            StageOperationWasNoOp = stageOperationWasNoOp,
            StageOperationMessage = operationMessage,
            LastStageAttemptAtUtc = now,
            LatestApplicableReleaseTag = resolvedLatestTag,
            IsCurrentLatest = resolvedReleaseTag is not null && resolvedLatestTag is not null
                ? string.Equals(resolvedReleaseTag, resolvedLatestTag, StringComparison.OrdinalIgnoreCase)
                : null,
            IsOutdated = resolvedReleaseTag is not null && resolvedLatestTag is not null
                ? !string.Equals(resolvedReleaseTag, resolvedLatestTag, StringComparison.OrdinalIgnoreCase)
                : null,
            Status = status,
            FailureMessage = status == ApplicationUpdateStagingStatus.Ready ? null : operationMessage,
            LastUpdatedAtUtc = now
        };
    }

    private async Task DownloadToFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(ApplicationUpdateStagingService));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        request.Headers.UserAgent.ParseAdd("PingMonitor-UpdaterStage2/1.0");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var tempPath = destinationPath + ".tmp";
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(tempPath, destinationPath);
    }

    [GeneratedRegex("^([A-Fa-f0-9]{64})\\s+[*]?(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256LineRegex();
}
