using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.ApplicationUpdater;

public interface IGitHubReleaseLookupService
{
    Task<GitHubReleaseSummary?> GetLatestApplicableReleaseAsync(bool allowPreviewReleases, CancellationToken cancellationToken);
    Task<IReadOnlyList<GitHubReleaseSummary>> GetApplicableReleasesAsync(bool allowPreviewReleases, int maxResults, CancellationToken cancellationToken);
}

internal sealed class GitHubReleaseLookupService : IGitHubReleaseLookupService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApplicationUpdaterOptions _options;

    public GitHubReleaseLookupService(
        IHttpClientFactory httpClientFactory,
        IOptions<ApplicationUpdaterOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<GitHubReleaseSummary?> GetLatestApplicableReleaseAsync(bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        var releases = await GetApplicableReleasesAsync(allowPreviewReleases, maxResults: 1, cancellationToken);
        return releases.FirstOrDefault();
    }

    public async Task<IReadOnlyList<GitHubReleaseSummary>> GetApplicableReleasesAsync(bool allowPreviewReleases, int maxResults, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(GitHubReleaseLookupService));
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildReleasesUrl());
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("PingMonitor-UpdateCheck/1.0");

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var reason = response.ReasonPhrase ?? "Unknown error";
            throw new InvalidOperationException($"GitHub API returned {(int)response.StatusCode} ({reason}).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("GitHub API response did not return an array of releases.");
        }

        var results = new List<GitHubReleaseSummary>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            var isDraft = GetBoolean(release, "draft");
            var isPrerelease = GetBoolean(release, "prerelease");
            if (isDraft)
            {
                continue;
            }

            if (isPrerelease && !allowPreviewReleases)
            {
                continue;
            }

            var tagName = GetString(release, "tag_name");
            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            results.Add(new GitHubReleaseSummary
            {
                TagName = tagName.Trim(),
                Name = GetString(release, "name")?.Trim(),
                IsPrerelease = isPrerelease,
                HtmlUrl = GetString(release, "html_url")?.Trim(),
                PublishedAtUtc = TryGetDateTimeOffset(release, "published_at"),
                Body = GetString(release, "body"),
                AssetsApiUrl = GetString(release, "assets_url")?.Trim(),
                Assets = ParseAssets(release)
            });

            if (maxResults > 0 && results.Count >= maxResults)
            {
                break;
            }
        }

        return results;
    }

    private string BuildReleasesUrl()
    {
        var baseUri = _options.GitHubApiBaseUrl.TrimEnd('/');
        var owner = _options.GitHubOwner.Trim();
        var repository = _options.GitHubRepository.Trim();
        return $"{baseUri}/repos/{owner}/{repository}/releases?per_page=30";
    }

    private static IReadOnlyList<GitHubReleaseAssetSummary> ParseAssets(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assetsElement)
            || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<GitHubReleaseAssetSummary>();
        foreach (var asset in assetsElement.EnumerateArray())
        {
            var name = GetString(asset, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            results.Add(new GitHubReleaseAssetSummary
            {
                Name = name.Trim(),
                BrowserDownloadUrl = GetString(asset, "browser_download_url")?.Trim(),
                SizeBytes = TryGetInt64(asset, "size")
            });
        }

        return results;
    }

    private static bool GetBoolean(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value)
               && value.ValueKind is JsonValueKind.True or JsonValueKind.False
               && value.GetBoolean();
    }

    private static string? GetString(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? TryGetInt64(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt64(out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement parent, string propertyName)
    {
        var text = GetString(parent, propertyName);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}
