using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationUpdater;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class ReleaseSchemaMetadataServiceTests
{
    [Fact]
    public async Task PopulateRequiredSchemaVersionsAsync_ReadsRequiredSchemaVersionFromStandaloneManifestWithoutDownloadingZip()
    {
        var handler = new RecordingHttpMessageHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["https://example.test/manifest.json"] = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildManifestJson(requiredSchemaVersion: 9), Encoding.UTF8, "application/json")
            }
        });
        var service = BuildService(handler);
        var releases = new[] { BuildRelease() };

        var enriched = await service.PopulateRequiredSchemaVersionsAsync(releases, "V1.2.3", CancellationToken.None);

        Assert.Single(enriched);
        Assert.Equal(9, enriched[0].RequiredSchemaVersion);
        Assert.Equal(ReleaseSchemaMetadataSource.StandaloneManifestAsset, enriched[0].SchemaMetadataSource);
        Assert.Equal(["https://example.test/manifest.json"], handler.RequestedUrls);
    }

    [Fact]
    public async Task PopulateRequiredSchemaVersionsAsync_LeavesSchemaVersionNull_WhenStandaloneManifestMissing()
    {
        var service = BuildService(new RecordingHttpMessageHandler(new Dictionary<string, HttpResponseMessage>()));
        var release = BuildRelease(includeManifestAsset: false);

        var enriched = await service.PopulateRequiredSchemaVersionsAsync([release], "V1.2.3", CancellationToken.None);

        Assert.Null(enriched[0].RequiredSchemaVersion);
        Assert.Equal(ReleaseSchemaMetadataSource.MissingOrUnknown, enriched[0].SchemaMetadataSource);
    }

    [Fact]
    public async Task PopulateRequiredSchemaVersionsAsync_LeavesSchemaVersionNull_WhenManifestVersionDoesNotMatchRelease()
    {
        var handler = new RecordingHttpMessageHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["https://example.test/manifest.json"] = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildManifestJson(version: "V9.9.9", requiredSchemaVersion: 9), Encoding.UTF8, "application/json")
            }
        });
        var service = BuildService(handler);

        var enriched = await service.PopulateRequiredSchemaVersionsAsync([BuildRelease()], "V1.2.3", CancellationToken.None);

        Assert.Null(enriched[0].RequiredSchemaVersion);
    }

    [Fact]
    public async Task PopulateRequiredSchemaVersionsAsync_LeavesSchemaVersionNull_WhenManifestPackageFileNameDoesNotMatchZip()
    {
        var handler = new RecordingHttpMessageHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["https://example.test/manifest.json"] = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildManifestJson(packageFileName: "Wrong.zip", requiredSchemaVersion: 9), Encoding.UTF8, "application/json")
            }
        });
        var service = BuildService(handler);

        var enriched = await service.PopulateRequiredSchemaVersionsAsync([BuildRelease()], "V1.2.3", CancellationToken.None);

        Assert.Null(enriched[0].RequiredSchemaVersion);
    }

    [Fact]
    public async Task ReadPackageManifestFromZipAsync_FindsManifestInsideTopLevelPackageFolder()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"ping-monitor-manifest-{Guid.NewGuid():N}.zip");
        try
        {
            await File.WriteAllBytesAsync(
                zipPath,
                BuildZipWithManifest("PingMonitor-V1.2.3-win-x64/manifest.json", BuildManifestJson(requiredSchemaVersion: 11)));

            var metadata = await ReleaseManifestMetadataReader.ReadPackageManifestFromZipAsync(
                zipPath,
                "PingMonitor-V1.2.3-win-x64.zip",
                CancellationToken.None);

            Assert.Equal(11, metadata.RequiredSchemaVersion);
            Assert.Equal(ReleaseSchemaMetadataSource.StagedPackageManifest, metadata.Source);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public async Task ReadPackageManifestFromZipAsync_FailsSafely_WhenManifestLookupIsAmbiguous()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"ping-monitor-manifest-{Guid.NewGuid():N}.zip");
        try
        {
            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(archive, "UnexpectedA/manifest.json", BuildManifestJson(requiredSchemaVersion: 11));
                WriteEntry(archive, "UnexpectedB/manifest.json", BuildManifestJson(requiredSchemaVersion: 12));
            }

            await File.WriteAllBytesAsync(zipPath, memoryStream.ToArray());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReleaseManifestMetadataReader.ReadPackageManifestFromZipAsync(
                    zipPath,
                    "PingMonitor-V1.2.3-win-x64.zip",
                    CancellationToken.None));
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void ValidateNoConflict_FailsSafely_WhenStandaloneAndPackageManifestDiffer()
    {
        var standalone = new ReleaseManifestMetadata(null, "V1.2.3", null, "PingMonitor-V1.2.3-win-x64.zip", "win-x64", "abc", 9, ReleaseSchemaMetadataSource.StandaloneManifestAsset, "asset");
        var package = standalone with { RequiredSchemaVersion = 10, Source = ReleaseSchemaMetadataSource.StagedPackageManifest, SourceName = "manifest.json" };

        Assert.Throws<InvalidOperationException>(() => ReleaseManifestMetadataReader.ValidateNoConflict(standalone, package));
    }

    private static ReleaseSchemaMetadataService BuildService(HttpMessageHandler handler)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ApplicationUpdaterOptions
        {
            ReleasePackagePrefix = "PingMonitor",
            RuntimeIdentifier = "win-x64"
        });

        return new ReleaseSchemaMetadataService(
            new StubHttpClientFactory(new HttpClient(handler)),
            options,
            NullLogger<ReleaseSchemaMetadataService>.Instance);
    }

    private static GitHubReleaseSummary BuildRelease(bool includeManifestAsset = true)
    {
        var assets = new List<GitHubReleaseAssetSummary>
        {
            new() { Name = "PingMonitor-V1.2.3-win-x64.zip", BrowserDownloadUrl = "https://example.test/download.zip" }
        };
        if (includeManifestAsset)
        {
            assets.Add(new GitHubReleaseAssetSummary { Name = "PingMonitor-V1.2.3-win-x64.manifest.json", BrowserDownloadUrl = "https://example.test/manifest.json" });
        }

        return new GitHubReleaseSummary { TagName = "V1.2.3", Assets = assets };
    }

    private static string BuildManifestJson(string version = "V1.2.3", string packageFileName = "PingMonitor-V1.2.3-win-x64.zip", int requiredSchemaVersion = 9)
    {
        return $$"""
               {
                 "appName":"Ping Monitor",
                 "version":"{{version}}",
                 "buildTimestampUtc":"2026-05-30T00:00:00.0000000+00:00",
                 "packageFileName":"{{packageFileName}}",
                 "runtime":"win-x64",
                 "commitHash":"abc123",
                 "requiredSchemaVersion":{{requiredSchemaVersion}}
               }
               """;
    }

    private static byte[] BuildZipWithManifest(string entryName, string manifestJson)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, entryName, manifestJson);
        }

        return memoryStream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string contents)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(contents);
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

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpResponseMessage> _responses;
        private readonly List<string> _requestedUrls = [];

        public RecordingHttpMessageHandler(Dictionary<string, HttpResponseMessage> responses)
        {
            _responses = responses;
        }

        public IReadOnlyList<string> RequestedUrls => _requestedUrls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            _requestedUrls.Add(url);
            if (_responses.TryGetValue(url, out var response))
            {
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
