using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationUpdater;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class ReleaseSchemaMetadataServiceTests
{
    [Fact]
    public async Task PopulateRequiredSchemaVersionsAsync_ReadsRequiredSchemaVersionFromManifest()
    {
        var zipBytes = BuildZipWithManifest("""{"requiredSchemaVersion":9}""");
        var service = BuildService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zipBytes)
        });

        var releases = new[]
        {
            new GitHubReleaseSummary
            {
                TagName = "V1.2.3",
                Assets = new[]
                {
                    new GitHubReleaseAssetSummary
                    {
                        Name = "PingMonitor-V1.2.3-win-x64.zip",
                        BrowserDownloadUrl = "https://example.test/download.zip"
                    }
                }
            }
        };

        var enriched = await service.PopulateRequiredSchemaVersionsAsync(releases, CancellationToken.None);

        Assert.Single(enriched);
        Assert.Equal(9, enriched[0].RequiredSchemaVersion);
    }

    [Fact]
    public async Task PopulateRequiredSchemaVersionsAsync_LeavesSchemaVersionNull_WhenManifestOmitsField()
    {
        var zipBytes = BuildZipWithManifest("""{"appName":"Ping Monitor"}""");
        var service = BuildService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zipBytes)
        });

        var releases = new[]
        {
            new GitHubReleaseSummary
            {
                TagName = "V1.2.3",
                Assets = new[]
                {
                    new GitHubReleaseAssetSummary
                    {
                        Name = "PingMonitor-V1.2.3-win-x64.zip",
                        BrowserDownloadUrl = "https://example.test/download.zip"
                    }
                }
            }
        };

        var enriched = await service.PopulateRequiredSchemaVersionsAsync(releases, CancellationToken.None);
        Assert.Null(enriched[0].RequiredSchemaVersion);
    }

    private static ReleaseSchemaMetadataService BuildService(HttpResponseMessage response)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ApplicationUpdaterOptions
        {
            ReleasePackagePrefix = "PingMonitor",
            RuntimeIdentifier = "win-x64"
        });

        return new ReleaseSchemaMetadataService(
            new StubHttpClientFactory(new HttpClient(new StubHttpMessageHandler(response))),
            options,
            NullLogger<ReleaseSchemaMetadataService>.Instance);
    }

    private static byte[] BuildZipWithManifest(string manifestJson)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(manifestJson);
        }

        return memoryStream.ToArray();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }
}
