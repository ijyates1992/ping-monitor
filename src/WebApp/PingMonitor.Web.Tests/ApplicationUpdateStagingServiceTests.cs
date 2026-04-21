using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationUpdater;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class ApplicationUpdateStagingServiceTests
{
    [Fact]
    public async Task StageLatestApplicableReleaseAsync_Succeeds_WhenChecksumMatches()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var zipBytes = Encoding.UTF8.GetBytes("fake-release-zip-content");
            var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zipBytes));

            var release = new GitHubReleaseSummary
            {
                TagName = "V1.2.3",
                Name = "Release",
                IsPrerelease = false,
                HtmlUrl = "https://example/release",
                PublishedAtUtc = DateTimeOffset.UtcNow,
                Assets =
                [
                    new GitHubReleaseAssetSummary { Name = "PingMonitor-V1.2.3-win-x64.zip", BrowserDownloadUrl = "https://download/release.zip" },
                    new GitHubReleaseAssetSummary { Name = "SHA256.txt", BrowserDownloadUrl = "https://download/SHA256.txt" }
                ]
            };

            var handler = new MappingHttpMessageHandler(new Dictionary<string, HttpResponseMessage>
            {
                ["https://download/release.zip"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) },
                ["https://download/SHA256.txt"] = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{checksum}  PingMonitor-V1.2.3-win-x64.zip", Encoding.UTF8, "text/plain")
                }
            });

            var service = BuildService(tempRoot, release, handler);
            var result = await service.StageLatestApplicableReleaseAsync(false, CancellationToken.None);

            Assert.Equal(ApplicationUpdateStagingStatus.Ready, result.State.Status);
            Assert.True(result.State.ChecksumVerified);
            Assert.True(File.Exists(result.State.StagedZipPath));
            Assert.True(File.Exists(result.State.StagedChecksumPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StageLatestApplicableReleaseAsync_Fails_WhenChecksumEntryMissing()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var zipBytes = Encoding.UTF8.GetBytes("fake-release-zip-content");

            var release = new GitHubReleaseSummary
            {
                TagName = "V1.2.3",
                Assets =
                [
                    new GitHubReleaseAssetSummary { Name = "PingMonitor-V1.2.3-win-x64.zip", BrowserDownloadUrl = "https://download/release.zip" },
                    new GitHubReleaseAssetSummary { Name = "SHA256.txt", BrowserDownloadUrl = "https://download/SHA256.txt" }
                ]
            };

            var handler = new MappingHttpMessageHandler(new Dictionary<string, HttpResponseMessage>
            {
                ["https://download/release.zip"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) },
                ["https://download/SHA256.txt"] = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ABCDEF  OtherPackage.zip", Encoding.UTF8, "text/plain")
                }
            });

            var service = BuildService(tempRoot, release, handler);
            var result = await service.StageLatestApplicableReleaseAsync(false, CancellationToken.None);

            Assert.Equal(ApplicationUpdateStagingStatus.ChecksumVerificationFailed, result.State.Status);
            Assert.False(result.State.ChecksumVerified);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StageLatestApplicableReleaseAsync_ReusesExistingStage_ForSameRelease()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var zipBytes = Encoding.UTF8.GetBytes("fake-release-zip-content");
            var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zipBytes));
            var release = BuildRelease("V1.2.3");

            var handler = new MappingHttpMessageHandler(new Dictionary<string, HttpResponseMessage>
            {
                ["https://download/release.zip"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) },
                ["https://download/SHA256.txt"] = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{checksum}  PingMonitor-V1.2.3-win-x64.zip", Encoding.UTF8, "text/plain")
                }
            });

            var service = BuildService(tempRoot, release, handler);
            var first = await service.StageLatestApplicableReleaseAsync(false, CancellationToken.None);
            var second = await service.StageLatestApplicableReleaseAsync(false, CancellationToken.None);

            Assert.Equal(ApplicationUpdateStagingStatus.Ready, second.State.Status);
            Assert.True(second.State.StageOperationWasNoOp);
            Assert.Equal(first.State.StagedZipPath, second.State.StagedZipPath);
            Assert.True(File.Exists(second.State.StagedZipPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StageLatestApplicableReleaseAsync_ReplacesPreviousStage_ForDifferentRelease()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var releaseA = BuildRelease("V1.2.3");
            var releaseB = BuildRelease("V1.2.4");
            var zipA = Encoding.UTF8.GetBytes("zip-a");
            var zipB = Encoding.UTF8.GetBytes("zip-b");
            var checksumA = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zipA));
            var checksumB = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zipB));

            var handler = new MappingHttpMessageHandler(new Dictionary<string, HttpResponseMessage>
            {
                ["https://download/release.zip"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipA) },
                ["https://download/SHA256.txt"] = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{checksumA}  PingMonitor-V1.2.3-win-x64.zip", Encoding.UTF8, "text/plain")
                }
            });

            var serviceA = BuildService(tempRoot, releaseA, handler);
            var first = await serviceA.StageLatestApplicableReleaseAsync(false, CancellationToken.None);

            var handlerB = new MappingHttpMessageHandler(new Dictionary<string, HttpResponseMessage>
            {
                ["https://download/release.zip"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipB) },
                ["https://download/SHA256.txt"] = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{checksumB}  PingMonitor-V1.2.4-win-x64.zip", Encoding.UTF8, "text/plain")
                }
            });

            var serviceB = BuildService(tempRoot, releaseB, handlerB);
            var second = await serviceB.StageLatestApplicableReleaseAsync(false, CancellationToken.None);

            Assert.Equal("V1.2.4", second.State.ReleaseTag);
            Assert.DoesNotContain("V1.2.3", second.State.StagedZipPath!);
            Assert.False(File.Exists(first.State.StagedZipPath!));
            Assert.True(File.Exists(second.State.StagedZipPath!));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StageLatestApplicableReleaseAsync_BlocksConcurrentRuns()
    {
        var tempRoot = CreateTempRoot();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var zipBytes = Encoding.UTF8.GetBytes("fake-release-zip-content");
            var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zipBytes));
            var release = BuildRelease("V1.2.3");
            var delayedLookup = new DelayedReleaseLookupService(release, gate.Task);
            var handler = new MappingHttpMessageHandler(new Dictionary<string, HttpResponseMessage>
            {
                ["https://download/release.zip"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) },
                ["https://download/SHA256.txt"] = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{checksum}  PingMonitor-V1.2.3-win-x64.zip", Encoding.UTF8, "text/plain")
                }
            });

            var service = BuildService(tempRoot, delayedLookup, handler);
            var firstTask = service.StageLatestApplicableReleaseAsync(false, CancellationToken.None);
            await Task.Delay(50);
            var second = await service.StageLatestApplicableReleaseAsync(false, CancellationToken.None);
            gate.SetResult(true);
            var first = await firstTask;
            var persisted = await ReadPersistedStateAsync(tempRoot, CancellationToken.None);

            Assert.Equal(ApplicationUpdateStagingStatus.StagingBlocked, second.State.Status);
            Assert.Equal(ApplicationUpdateStagingStatus.Ready, first.State.Status);
            Assert.NotNull(persisted);
            Assert.Equal(ApplicationUpdateStagingStatus.Ready, persisted!.Status);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "ping-monitor-stage2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static GitHubReleaseSummary BuildRelease(string tag)
    {
        return new GitHubReleaseSummary
        {
            TagName = tag,
            Name = "Release",
            IsPrerelease = false,
            HtmlUrl = "https://example/release",
            PublishedAtUtc = DateTimeOffset.UtcNow,
            Assets =
            [
                new GitHubReleaseAssetSummary { Name = $"PingMonitor-{tag}-win-x64.zip", BrowserDownloadUrl = "https://download/release.zip" },
                new GitHubReleaseAssetSummary { Name = "SHA256.txt", BrowserDownloadUrl = "https://download/SHA256.txt" }
            ]
        };
    }

    private static ApplicationUpdateStagingService BuildService(string contentRoot, GitHubReleaseSummary release, HttpMessageHandler handler)
    {
        return BuildService(contentRoot, new FakeReleaseLookupService(release), handler);
    }

    private static ApplicationUpdateStagingService BuildService(string contentRoot, IGitHubReleaseLookupService releaseLookupService, HttpMessageHandler handler)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ApplicationUpdaterOptions
        {
            UpdateChecksEnabled = true,
            GitHubOwner = "ijyates1992",
            GitHubRepository = "ping-monitor",
            GitHubApiBaseUrl = "https://api.github.com",
            StagingStoragePath = "App_Data/Updater",
            RuntimeIdentifier = "win-x64",
            ReleasePackagePrefix = "PingMonitor",
            ChecksumAssetName = "SHA256.txt"
        });

        var stateStore = new ApplicationUpdateStagingStateStore(
            new StubWebHostEnvironment(contentRoot),
            options);

        var httpClientFactory = new StubHttpClientFactory(new HttpClient(handler));

        return new ApplicationUpdateStagingService(
            releaseLookupService,
            stateStore,
            httpClientFactory,
            options);
    }

    private static async Task<ApplicationUpdateStagingState?> ReadPersistedStateAsync(string contentRoot, CancellationToken cancellationToken)
    {
        var statePath = Path.Combine(contentRoot, "App_Data", "Updater", "state", "staged-update.json");
        if (!File.Exists(statePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(statePath);
        return await JsonSerializer.DeserializeAsync<ApplicationUpdateStagingState>(stream, cancellationToken: cancellationToken);
    }

    private sealed class DelayedReleaseLookupService : IGitHubReleaseLookupService
    {
        private readonly GitHubReleaseSummary _release;
        private readonly Task _delayTask;

        public DelayedReleaseLookupService(GitHubReleaseSummary release, Task delayTask)
        {
            _release = release;
            _delayTask = delayTask;
        }

        public async Task<GitHubReleaseSummary?> GetLatestApplicableReleaseAsync(bool allowPreviewReleases, CancellationToken cancellationToken)
        {
            await _delayTask.WaitAsync(cancellationToken);
            return _release;
        }
    }

    private sealed class FakeReleaseLookupService : IGitHubReleaseLookupService
    {
        private readonly GitHubReleaseSummary _release;

        public FakeReleaseLookupService(GitHubReleaseSummary release)
        {
            _release = release;
        }

        public Task<GitHubReleaseSummary?> GetLatestApplicableReleaseAsync(bool allowPreviewReleases, CancellationToken cancellationToken)
        {
            return Task.FromResult<GitHubReleaseSummary?>(_release);
        }
    }

    private sealed class MappingHttpMessageHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, HttpResponseMessage> _responses;

        public MappingHttpMessageHandler(IReadOnlyDictionary<string, HttpResponseMessage> responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.RequestUri!.ToString();
            if (!_responses.TryGetValue(key, out var response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(response);
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

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
            WebRootPath = contentRootPath;
            WebRootFileProvider = new PhysicalFileProvider(contentRootPath);
            ApplicationName = "PingMonitor.Tests";
            EnvironmentName = "Development";
        }

        public string ApplicationName { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
