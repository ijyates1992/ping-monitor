using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationMetadata;
using PingMonitor.Web.Services.ApplicationUpdater;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class ApplicationUpdaterTests
{
    [Theory]
    [InlineData("V0.0.2", true, false)]
    [InlineData("V0.1.0", true, false)]
    [InlineData("V1.2.10", true, false)]
    [InlineData("DEV-05.04.26-18:42", false, true)]
    [InlineData("v1.2.3", false, false)]
    [InlineData("garbage", false, false)]
    public void ParseCurrentVersion_HandlesExpectedFormats(string value, bool expectRelease, bool expectDev)
    {
        var parsed = ReleaseVersionParser.ParseCurrent(value);

        Assert.Equal(expectRelease, parsed.IsReleaseVersion);
        Assert.Equal(expectDev, parsed.IsDevBuild);
    }

    [Fact]
    public void ReleaseVersionComparison_UsesNumericComparison()
    {
        Assert.True(ReleaseVersionParser.TryParseReleaseVersion("V1.2.10", out var v1210));
        Assert.True(ReleaseVersionParser.TryParseReleaseVersion("V1.2.3", out var v123));

        Assert.True(v1210.CompareTo(v123) > 0);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_IgnoresPrerelease_WhenPreviewDisabled()
    {
        var service = BuildService(
            "V0.1.0",
            """
            [
              { "tag_name": "V0.2.0", "name": "Preview", "draft": false, "prerelease": true, "published_at": "2026-04-15T00:00:00Z", "html_url": "https://example/pre" },
              { "tag_name": "V0.1.1", "name": "Stable", "draft": false, "prerelease": false, "published_at": "2026-04-10T00:00:00Z", "html_url": "https://example/stable" }
            ]
            """);

        var result = await service.CheckForUpdatesAsync(allowPreviewReleases: false, CancellationToken.None);

        Assert.Equal(ApplicationUpdateCheckState.UpdateAvailable, result.State);
        Assert.Equal("V0.1.1", result.LatestApplicableRelease?.TagName);
        Assert.False(result.LatestApplicableRelease?.IsPrerelease);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_UsesPrerelease_WhenPreviewEnabled()
    {
        var service = BuildService(
            "V0.1.0",
            """
            [
              { "tag_name": "V0.2.0", "name": "Preview", "draft": false, "prerelease": true, "published_at": "2026-04-15T00:00:00Z", "html_url": "https://example/pre" },
              { "tag_name": "V0.1.1", "name": "Stable", "draft": false, "prerelease": false, "published_at": "2026-04-10T00:00:00Z", "html_url": "https://example/stable" }
            ]
            """);

        var result = await service.CheckForUpdatesAsync(allowPreviewReleases: true, CancellationToken.None);

        Assert.Equal(ApplicationUpdateCheckState.UpdateAvailable, result.State);
        Assert.Equal("V0.2.0", result.LatestApplicableRelease?.TagName);
        Assert.True(result.LatestApplicableRelease?.IsPrerelease);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_SkipsComparison_ForDevBuild()
    {
        var service = BuildService(
            "DEV-05.04.26-18:42",
            """
            [
              { "tag_name": "V0.1.1", "name": "Stable", "draft": false, "prerelease": false, "published_at": "2026-04-10T00:00:00Z", "html_url": "https://example/stable" }
            ]
            """);

        var result = await service.CheckForUpdatesAsync(allowPreviewReleases: false, CancellationToken.None);

        Assert.Equal(ApplicationUpdateCheckState.DevBuildComparisonSkipped, result.State);
        Assert.Equal("V0.1.1", result.LatestApplicableRelease?.TagName);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_FailsSafely_WhenLatestTagMalformed()
    {
        var service = BuildService(
            "V0.1.0",
            """
            [
              { "tag_name": "release-foo", "name": "Bad", "draft": false, "prerelease": false, "published_at": "2026-04-10T00:00:00Z", "html_url": "https://example/bad" }
            ]
            """);

        var result = await service.CheckForUpdatesAsync(allowPreviewReleases: false, CancellationToken.None);

        Assert.Equal(ApplicationUpdateCheckState.LatestVersionUnknown, result.State);
    }

    private static ApplicationUpdateDetectionService BuildService(string currentVersion, string responseBody)
    {
        var metadataProvider = new FakeMetadataProvider(currentVersion);
        var options = Microsoft.Extensions.Options.Options.Create(new ApplicationUpdaterOptions
        {
            UpdateChecksEnabled = true,
            GitHubOwner = "ijyates1992",
            GitHubRepository = "ping-monitor",
            GitHubApiBaseUrl = "https://api.github.com",
            AllowPreviewReleases = false
        });

        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });

        var httpClientFactory = new StubHttpClientFactory(new HttpClient(handler));
        return new ApplicationUpdateDetectionService(metadataProvider, httpClientFactory, options);
    }

    private sealed class FakeMetadataProvider : IApplicationMetadataProvider
    {
        private readonly string _version;

        public FakeMetadataProvider(string version)
        {
            _version = version;
        }

        public ApplicationMetadataSnapshot GetSnapshot()
        {
            return new ApplicationMetadataSnapshot
            {
                Version = _version
            };
        }
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
