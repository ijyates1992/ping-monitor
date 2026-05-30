using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.ApplicationUpdater;

public interface IReleaseSchemaMetadataService
{
    Task<IReadOnlyList<GitHubReleaseSummary>> PopulateRequiredSchemaVersionsAsync(
        IReadOnlyList<GitHubReleaseSummary> releases,
        string? selectedReleaseTag,
        CancellationToken cancellationToken);
}

internal sealed class ReleaseSchemaMetadataService : IReleaseSchemaMetadataService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApplicationUpdaterOptions _options;
    private readonly ILogger<ReleaseSchemaMetadataService> _logger;

    public ReleaseSchemaMetadataService(
        IHttpClientFactory httpClientFactory,
        IOptions<ApplicationUpdaterOptions> options,
        ILogger<ReleaseSchemaMetadataService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GitHubReleaseSummary>> PopulateRequiredSchemaVersionsAsync(
        IReadOnlyList<GitHubReleaseSummary> releases,
        string? selectedReleaseTag,
        CancellationToken cancellationToken)
    {
        if (releases.Count == 0)
        {
            return releases;
        }

        var targetTag = string.IsNullOrWhiteSpace(selectedReleaseTag)
            ? releases[0].TagName
            : selectedReleaseTag;

        var resolvedMetadata = await ResolveStandaloneManifestMetadataAsync(releases, targetTag, cancellationToken);
        if (resolvedMetadata is null)
        {
            return releases;
        }

        return releases
            .Select(release => string.Equals(release.TagName, targetTag, StringComparison.OrdinalIgnoreCase)
                ? CloneWithSchemaMetadata(release, resolvedMetadata)
                : release)
            .ToArray();
    }

    private async Task<ReleaseManifestMetadata?> ResolveStandaloneManifestMetadataAsync(
        IReadOnlyList<GitHubReleaseSummary> releases,
        string selectedTag,
        CancellationToken cancellationToken)
    {
        var selectedRelease = releases.FirstOrDefault(release =>
            string.Equals(release.TagName, selectedTag, StringComparison.OrdinalIgnoreCase));
        if (selectedRelease is null)
        {
            return null;
        }

        var zipName = BuildExpectedZipAssetName(selectedRelease.TagName);
        var manifestName = BuildExpectedManifestAssetName(selectedRelease.TagName);
        var zipAsset = selectedRelease.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, zipName, StringComparison.OrdinalIgnoreCase));
        if (zipAsset is null)
        {
            _logger.LogInformation(
                "Schema metadata lookup skipped for release {ReleaseTag}. Could not find zip asset {ZipAssetName}.",
                selectedRelease.TagName,
                zipName);
            return null;
        }

        var manifestAsset = selectedRelease.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, manifestName, StringComparison.OrdinalIgnoreCase));
        if (manifestAsset is null || string.IsNullOrWhiteSpace(manifestAsset.BrowserDownloadUrl))
        {
            _logger.LogInformation(
                "Standalone schema manifest asset {ManifestAssetName} was not found for release {ReleaseTag}; selected release will use missing-metadata warning until staging fallback can inspect the package.",
                manifestName,
                selectedRelease.TagName);
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(ReleaseSchemaMetadataService));
            await using var manifestStream = await client.GetStreamAsync(manifestAsset.BrowserDownloadUrl, cancellationToken);
            var metadata = await ReleaseManifestMetadataReader.ReadStandaloneManifestAsync(manifestStream, manifestAsset.Name, cancellationToken);
            ReleaseManifestMetadataReader.ValidateForRelease(metadata, selectedRelease.TagName, zipAsset.Name, _options.RuntimeIdentifier);
            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to resolve standalone schema manifest metadata for release {ReleaseTag} from asset {ManifestAssetName}.",
                selectedRelease.TagName,
                manifestAsset.Name);
            return null;
        }
    }

    private string BuildExpectedZipAssetName(string releaseTag)
    {
        return $"{_options.ReleasePackagePrefix}-{releaseTag}-{_options.RuntimeIdentifier}.zip";
    }

    private string BuildExpectedManifestAssetName(string releaseTag)
    {
        return $"{_options.ReleasePackagePrefix}-{releaseTag}-{_options.RuntimeIdentifier}.manifest.json";
    }

    private static GitHubReleaseSummary CloneWithSchemaMetadata(GitHubReleaseSummary source, ReleaseManifestMetadata metadata)
    {
        return new GitHubReleaseSummary
        {
            TagName = source.TagName,
            Name = source.Name,
            Body = source.Body,
            IsPrerelease = source.IsPrerelease,
            HtmlUrl = source.HtmlUrl,
            PublishedAtUtc = source.PublishedAtUtc,
            AssetsApiUrl = source.AssetsApiUrl,
            Assets = source.Assets,
            RequiredSchemaVersion = metadata.RequiredSchemaVersion,
            SchemaMetadataSource = metadata.Source,
            SchemaMetadataAssetName = metadata.SourceName
        };
    }
}
