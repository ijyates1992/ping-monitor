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

        var requestUri = new Uri(baseUri, $"/repos/{Uri.EscapeDataString(_options.Owner)}/{Uri.EscapeDataString(_options.Repository)}/releases/latest");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (null, $"GitHub release lookup failed with HTTP {(int)response.StatusCode}.");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<GitHubLatestReleaseResponse>(responseStream, SerializerOptions, cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.TagName))
            {
                return (null, "GitHub release response did not include a valid tag.");
            }

            if (payload.Draft)
            {
                return (null, "GitHub latest release lookup returned a draft release, which is unsupported.");
            }

            if (payload.Prerelease && !_options.IncludePrerelease)
            {
                return (null, "GitHub latest release is marked prerelease and prerelease checks are disabled.");
            }

            return (new ApplicationReleaseMetadata
            {
                TagName = payload.TagName.Trim(),
                Name = payload.Name?.Trim() ?? string.Empty,
                PublishedAtUtc = payload.PublishedAt,
                IsPrerelease = payload.Prerelease,
                HtmlUrl = payload.HtmlUrl?.Trim() ?? string.Empty
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
