using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.ApplicationUpdater;

public interface IReleaseSchemaMetadataService
{
    Task<IReadOnlyList<GitHubReleaseSummary>> PopulateRequiredSchemaVersionsAsync(
        IReadOnlyList<GitHubReleaseSummary> releases,
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
        CancellationToken cancellationToken)
    {
        if (releases.Count == 0)
        {
            return releases;
        }

        var targetTag = releases[0].TagName;
        for (var i = 0; i < releases.Count; i++)
        {
            if (releases[i].RequiredSchemaVersion is null && !string.IsNullOrWhiteSpace(releases[i].TagName))
            {
                targetTag = releases[i].TagName;
                break;
            }
        }

        var resolvedSchemaVersion = await ResolveRequiredSchemaVersionAsync(releases, targetTag, cancellationToken);
        if (resolvedSchemaVersion is null)
        {
            return releases;
        }

        return releases
            .Select(release => string.Equals(release.TagName, targetTag, StringComparison.OrdinalIgnoreCase)
                ? CloneWithSchemaVersion(release, resolvedSchemaVersion)
                : release)
            .ToArray();
    }

    private async Task<int?> ResolveRequiredSchemaVersionAsync(
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

        var zipName = $"{_options.ReleasePackagePrefix}-{selectedRelease.TagName}-{_options.RuntimeIdentifier}.zip";
        var zipAsset = selectedRelease.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, zipName, StringComparison.OrdinalIgnoreCase));

        if (zipAsset is null || string.IsNullOrWhiteSpace(zipAsset.BrowserDownloadUrl))
        {
            _logger.LogInformation(
                "Schema metadata lookup skipped for release {ReleaseTag}. Could not find zip asset {ZipAssetName}.",
                selectedRelease.TagName,
                zipName);
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(ReleaseSchemaMetadataService));
            await using var releaseStream = await client.GetStreamAsync(zipAsset.BrowserDownloadUrl, cancellationToken);
            using var archive = new ZipArchive(releaseStream, ZipArchiveMode.Read, leaveOpen: false);
            var manifest = archive.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase));

            if (manifest is null)
            {
                _logger.LogInformation("Release {ReleaseTag} zip did not include manifest.json.", selectedRelease.TagName);
                return null;
            }

            await using var manifestStream = manifest.Open();
            using var document = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("requiredSchemaVersion", out var schemaVersionElement))
            {
                _logger.LogWarning("Release {ReleaseTag} manifest does not contain requiredSchemaVersion.", selectedRelease.TagName);
                return null;
            }

            if (schemaVersionElement.ValueKind != JsonValueKind.Number
                || !schemaVersionElement.TryGetInt32(out var parsed))
            {
                _logger.LogWarning("Release {ReleaseTag} manifest contains invalid requiredSchemaVersion.", selectedRelease.TagName);
                return null;
            }

            return parsed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve required schema version metadata for release {ReleaseTag}.", selectedRelease.TagName);
            return null;
        }
    }

    private static GitHubReleaseSummary CloneWithSchemaVersion(GitHubReleaseSummary source, int? schemaVersion)
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
            RequiredSchemaVersion = schemaVersion
        };
    }
}
