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

        if (!_options.UpdateChecksEnabled)
        {
            return await WriteAndReturnAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = repository,
                AllowPreviewReleases = allowPreviewReleases,
                Status = ApplicationUpdateStagingStatus.StagingFailed,
                FailureMessage = "Update checks are disabled by configuration.",
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        GitHubReleaseSummary? release;
        try
        {
            release = await _gitHubReleaseLookupService.GetLatestApplicableReleaseAsync(allowPreviewReleases, cancellationToken);
        }
        catch (Exception ex)
        {
            return await WriteAndReturnAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = repository,
                AllowPreviewReleases = allowPreviewReleases,
                Status = ApplicationUpdateStagingStatus.StagingFailed,
                FailureMessage = $"Unable to resolve latest applicable release: {ex.Message}",
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        if (release is null)
        {
            return await WriteAndReturnAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = repository,
                AllowPreviewReleases = allowPreviewReleases,
                Status = ApplicationUpdateStagingStatus.NoApplicableRelease,
                FailureMessage = "No applicable GitHub release was found using the current preview-release filter.",
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        var zipName = BuildExpectedZipAssetName(release.TagName);
        var zipAsset = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, zipName, StringComparison.OrdinalIgnoreCase));
        if (zipAsset is null || string.IsNullOrWhiteSpace(zipAsset.BrowserDownloadUrl))
        {
            return await WriteAndReturnAsync(BuildAssetFailureState(repository, allowPreviewReleases, release, $"Release zip asset '{zipName}' was not found."), cancellationToken);
        }

        var checksumAsset = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, _options.ChecksumAssetName, StringComparison.OrdinalIgnoreCase));
        if (checksumAsset is null || string.IsNullOrWhiteSpace(checksumAsset.BrowserDownloadUrl))
        {
            return await WriteAndReturnAsync(BuildAssetFailureState(repository, allowPreviewReleases, release, $"Checksum asset '{_options.ChecksumAssetName}' was not found."), cancellationToken);
        }

        var stagingRoot = _stagingStateStore.GetStagingRootPath();
        var downloadsPath = Path.Combine(stagingRoot, "staged", release.TagName);
        var zipPath = Path.Combine(downloadsPath, zipAsset.Name);
        var checksumPath = Path.Combine(downloadsPath, checksumAsset.Name);

        try
        {
            Directory.CreateDirectory(downloadsPath);
            await DownloadToFileAsync(zipAsset.BrowserDownloadUrl!, zipPath, cancellationToken);
            await DownloadToFileAsync(checksumAsset.BrowserDownloadUrl!, checksumPath, cancellationToken);
        }
        catch (Exception ex)
        {
            return await WriteAndReturnAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = repository,
                AllowPreviewReleases = allowPreviewReleases,
                ReleaseTag = release.TagName,
                ReleaseTitle = release.Name,
                ReleaseIsPrerelease = release.IsPrerelease,
                ReleasePublishedAtUtc = release.PublishedAtUtc,
                ReleaseUrl = release.HtmlUrl,
                SelectedAssetName = zipAsset.Name,
                SelectedChecksumAssetName = checksumAsset.Name,
                StagedZipPath = zipPath,
                StagedChecksumPath = checksumPath,
                Status = ApplicationUpdateStagingStatus.DownloadFailed,
                FailureMessage = $"Failed to download release assets: {ex.Message}",
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken);
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
            return await WriteAndReturnAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = repository,
                AllowPreviewReleases = allowPreviewReleases,
                ReleaseTag = release.TagName,
                ReleaseTitle = release.Name,
                ReleaseIsPrerelease = release.IsPrerelease,
                ReleasePublishedAtUtc = release.PublishedAtUtc,
                ReleaseUrl = release.HtmlUrl,
                SelectedAssetName = zipAsset.Name,
                SelectedChecksumAssetName = checksumAsset.Name,
                StagedZipPath = zipPath,
                StagedChecksumPath = checksumPath,
                Status = ApplicationUpdateStagingStatus.ChecksumVerificationFailed,
                FailureMessage = $"Unable to verify checksum: {ex.Message}",
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            return await WriteAndReturnAsync(new ApplicationUpdateStagingState
            {
                SourceRepository = repository,
                AllowPreviewReleases = allowPreviewReleases,
                ReleaseTag = release.TagName,
                ReleaseTitle = release.Name,
                ReleaseIsPrerelease = release.IsPrerelease,
                ReleasePublishedAtUtc = release.PublishedAtUtc,
                ReleaseUrl = release.HtmlUrl,
                SelectedAssetName = zipAsset.Name,
                SelectedChecksumAssetName = checksumAsset.Name,
                StagedZipPath = zipPath,
                StagedChecksumPath = checksumPath,
                ExpectedSha256 = expectedHash,
                ActualSha256 = actualHash,
                ChecksumVerified = false,
                Status = ApplicationUpdateStagingStatus.ChecksumVerificationFailed,
                FailureMessage = "Checksum verification failed. The staged package hash does not match SHA256.txt.",
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        return await WriteAndReturnAsync(new ApplicationUpdateStagingState
        {
            SourceRepository = repository,
            AllowPreviewReleases = allowPreviewReleases,
            ReleaseTag = release.TagName,
            ReleaseTitle = release.Name,
            ReleaseIsPrerelease = release.IsPrerelease,
            ReleasePublishedAtUtc = release.PublishedAtUtc,
            ReleaseUrl = release.HtmlUrl,
            SelectedAssetName = zipAsset.Name,
            SelectedChecksumAssetName = checksumAsset.Name,
            StagedZipPath = zipPath,
            StagedChecksumPath = checksumPath,
            ExpectedSha256 = expectedHash,
            ActualSha256 = actualHash,
            ChecksumVerified = true,
            StagedAtUtc = DateTimeOffset.UtcNow,
            Status = ApplicationUpdateStagingStatus.Ready,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow
        }, cancellationToken);
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

    private ApplicationUpdateStagingState BuildAssetFailureState(string repository, bool allowPreviewReleases, GitHubReleaseSummary release, string message)
    {
        return new ApplicationUpdateStagingState
        {
            SourceRepository = repository,
            AllowPreviewReleases = allowPreviewReleases,
            ReleaseTag = release.TagName,
            ReleaseTitle = release.Name,
            ReleaseIsPrerelease = release.IsPrerelease,
            ReleasePublishedAtUtc = release.PublishedAtUtc,
            ReleaseUrl = release.HtmlUrl,
            Status = ApplicationUpdateStagingStatus.AssetResolutionFailed,
            FailureMessage = message,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow
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
