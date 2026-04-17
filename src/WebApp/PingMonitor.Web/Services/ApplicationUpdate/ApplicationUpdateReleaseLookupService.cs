using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.ApplicationUpdate;

public interface IApplicationUpdateReleaseLookupService
{
    Task<(ApplicationReleaseMetadata? Release, string? ErrorMessage)> TryGetLatestReleaseAsync(CancellationToken cancellationToken);
}

public sealed class ApplicationUpdateReleaseLookupService : IApplicationUpdateReleaseLookupService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ApplicationUpdateOptions _options;

    public ApplicationUpdateReleaseLookupService(HttpClient httpClient, IOptions<ApplicationUpdateOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<(ApplicationReleaseMetadata? Release, string? ErrorMessage)> TryGetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Owner) || string.IsNullOrWhiteSpace(_options.Repository))
        {
            return (null, "Update check configuration is invalid. Configure ApplicationUpdate:Owner and ApplicationUpdate:Repository.");
        }

        if (!Uri.TryCreate(_options.GitHubApiBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return (null, "Update check configuration is invalid. Configure a valid ApplicationUpdate:GitHubApiBaseUrl.");
        }

        try
        {
            var latestReleasePath = $"/repos/{Uri.EscapeDataString(_options.Owner)}/{Uri.EscapeDataString(_options.Repository)}/releases/latest";
            var (latestReleasePayload, latestReleaseStatusCode) = await TryGetReleasePayloadAsync(baseUri, latestReleasePath, cancellationToken);
            if (latestReleasePayload is null && latestReleaseStatusCode == 404)
            {
                var releasesPath = $"/repos/{Uri.EscapeDataString(_options.Owner)}/{Uri.EscapeDataString(_options.Repository)}/releases?per_page=20";
                var (allReleasesPayload, allReleasesStatusCode) = await TryGetReleaseListPayloadAsync(baseUri, releasesPath, cancellationToken);
                if (allReleasesPayload is null)
                {
                    return (null, $"GitHub release lookup failed with HTTP {allReleasesStatusCode}.");
                }

                latestReleasePayload = ResolveLatestSupportedRelease(allReleasesPayload);
                if (latestReleasePayload is null)
                {
                    return (null, _options.IncludePrerelease
                        ? "GitHub release lookup did not return any non-draft releases."
                        : "GitHub release lookup did not return any non-draft, non-prerelease releases.");
                }
            }

            if (latestReleasePayload is null)
            {
                return (null, $"GitHub release lookup failed with HTTP {latestReleaseStatusCode}.");
            }

            if (string.IsNullOrWhiteSpace(latestReleasePayload.TagName))
            {
                return (null, "GitHub release response did not include a valid tag.");
            }

            if (latestReleasePayload.Draft)
            {
                return (null, "GitHub latest release lookup returned a draft release, which is unsupported.");
            }

            if (latestReleasePayload.Prerelease && !_options.IncludePrerelease)
            {
                return (null, "GitHub latest release is marked prerelease and prerelease checks are disabled.");
            }

            return (new ApplicationReleaseMetadata
            {
                TagName = latestReleasePayload.TagName.Trim(),
                Name = latestReleasePayload.Name?.Trim() ?? string.Empty,
                PublishedAtUtc = latestReleasePayload.PublishedAt,
                IsPrerelease = latestReleasePayload.Prerelease,
                HtmlUrl = latestReleasePayload.HtmlUrl?.Trim() ?? string.Empty
            }, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return (null, "GitHub release lookup failed due to a network or parsing error.");
        }
    }

    private async Task<(GitHubLatestReleaseResponse? Payload, int StatusCode)> TryGetReleasePayloadAsync(
        Uri baseUri,
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, (int)response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<GitHubLatestReleaseResponse>(responseStream, SerializerOptions, cancellationToken);
        return (payload, (int)response.StatusCode);
    }

    private async Task<(IReadOnlyList<GitHubLatestReleaseResponse>? Payload, int StatusCode)> TryGetReleaseListPayloadAsync(
        Uri baseUri,
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, (int)response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<List<GitHubLatestReleaseResponse>>(responseStream, SerializerOptions, cancellationToken);
        return (payload, (int)response.StatusCode);
    }

    private GitHubLatestReleaseResponse? ResolveLatestSupportedRelease(IReadOnlyList<GitHubLatestReleaseResponse> releases)
    {
        foreach (var release in releases)
        {
            if (release.Draft)
            {
                continue;
            }

            if (release.Prerelease && !_options.IncludePrerelease)
            {
                continue;
            }

            return release;
        }

        return null;
    }

    private sealed class GitHubLatestReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; init; }

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }
    }
}
