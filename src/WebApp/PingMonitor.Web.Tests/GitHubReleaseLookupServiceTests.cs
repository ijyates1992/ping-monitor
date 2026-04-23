using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationUpdater;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class GitHubReleaseLookupServiceTests
{
    [Fact]
    public async Task GetLatestApplicableReleaseAsync_SkipsPrerelease_WhenPreviewDisabled()
    {
        var service = BuildService(
            """
            [
              { "tag_name": "V0.2.0", "name": "Preview", "draft": false, "prerelease": true, "published_at": "2026-04-15T00:00:00Z", "html_url": "https://example/pre", "assets": [] },
              { "tag_name": "V0.1.1", "name": "Stable", "draft": false, "prerelease": false, "published_at": "2026-04-10T00:00:00Z", "html_url": "https://example/stable", "assets": [] }
            ]
            """);

        var result = await service.GetLatestApplicableReleaseAsync(false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("V0.1.1", result!.TagName);
    }

    [Fact]
    public async Task GetLatestApplicableReleaseAsync_UsesPrerelease_WhenPreviewEnabled()
    {
        var service = BuildService(
            """
            [
              { "tag_name": "V0.2.0", "name": "Preview", "draft": false, "prerelease": true, "published_at": "2026-04-15T00:00:00Z", "html_url": "https://example/pre", "assets": [] },
              { "tag_name": "V0.1.1", "name": "Stable", "draft": false, "prerelease": false, "published_at": "2026-04-10T00:00:00Z", "html_url": "https://example/stable", "assets": [] }
            ]
            """);

        var result = await service.GetLatestApplicableReleaseAsync(true, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("V0.2.0", result!.TagName);
    }

    [Fact]
    public async Task GetApplicableReleasesAsync_ReturnsFilteredList_WithReleaseBody()
    {
        var service = BuildService(
            """
            [
              { "tag_name": "V0.2.0", "name": "Preview", "body": "Preview notes", "draft": false, "prerelease": true, "published_at": "2026-04-15T00:00:00Z", "html_url": "https://example/pre", "assets": [] },
              { "tag_name": "V0.1.1", "name": "Stable", "body": "Stable notes", "draft": false, "prerelease": false, "published_at": "2026-04-10T00:00:00Z", "html_url": "https://example/stable", "assets": [] }
            ]
            """);

        var result = await service.GetApplicableReleasesAsync(false, maxResults: 10, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("V0.1.1", result[0].TagName);
        Assert.Equal("Stable notes", result[0].Body);
    }

    private static GitHubReleaseLookupService BuildService(string body)
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        var options = Microsoft.Extensions.Options.Options.Create(new ApplicationUpdaterOptions
        {
            UpdateChecksEnabled = true,
            GitHubOwner = "ijyates1992",
            GitHubRepository = "ping-monitor",
            GitHubApiBaseUrl = "https://api.github.com"
        });

        return new GitHubReleaseLookupService(new StubHttpClientFactory(new HttpClient(handler)), options);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }
}
