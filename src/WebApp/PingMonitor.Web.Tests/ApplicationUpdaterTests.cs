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
            new GitHubReleaseSummary
            {
                TagName = "V0.1.1",
                Name = "Stable",
                IsPrerelease = false,
                PublishedAtUtc = DateTimeOffset.Parse("2026-04-10T00:00:00Z"),
                HtmlUrl = "https://example/stable"
            });

        var result = await service.CheckForUpdatesAsync(allowPreviewReleases: false, CancellationToken.None);

        Assert.Equal(ApplicationUpdateCheckState.UpdateAvailable, result.State);
        Assert.Equal("V0.1.1", result.LatestApplicableRelease?.TagName);
        Assert.False(result.LatestApplicableRelease?.IsPrerelease);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_SkipsComparison_ForDevBuild()
    {
        var service = BuildService(
            "DEV-05.04.26-18:42",
            new GitHubReleaseSummary
            {
                TagName = "V0.1.1",
                Name = "Stable",
                IsPrerelease = false,
                PublishedAtUtc = DateTimeOffset.Parse("2026-04-10T00:00:00Z"),
                HtmlUrl = "https://example/stable"
            });

        var result = await service.CheckForUpdatesAsync(allowPreviewReleases: false, CancellationToken.None);

        Assert.Equal(ApplicationUpdateCheckState.DevBuildComparisonSkipped, result.State);
        Assert.Equal("V0.1.1", result.LatestApplicableRelease?.TagName);
        Assert.True(result.ReleaseDiscoverySucceeded);
        Assert.True(result.IsCurrentBuildDev);
        Assert.False(result.SemanticComparisonPerformed);
        Assert.Contains("Latest applicable release V0.1.1 was detected", result.Message);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_FailsSafely_WhenLatestTagMalformed()
    {
        var service = BuildService(
            "V0.1.0",
            new GitHubReleaseSummary
            {
                TagName = "release-foo",
                Name = "Bad",
                IsPrerelease = false,
                PublishedAtUtc = DateTimeOffset.Parse("2026-04-10T00:00:00Z"),
                HtmlUrl = "https://example/bad"
            });

        var result = await service.CheckForUpdatesAsync(allowPreviewReleases: false, CancellationToken.None);

        Assert.Equal(ApplicationUpdateCheckState.LatestVersionUnknown, result.State);
    }

    [Fact]
    public void DetermineAutoStagePlan_DevBuildWithoutOverride_IsSuppressed()
    {
        var plan = ApplicationUpdateBackgroundService.DetermineAutoStagePlan(
            ApplicationUpdateCheckState.DevBuildComparisonSkipped,
            releaseFound: true,
            automaticStageEnabled: true,
            allowDevBuildAutoStageWithoutVersionComparison: false,
            previousAttemptedTag: null,
            latestTag: "V0.2.0-preview");

        Assert.Equal(ApplicationUpdateBackgroundService.AutoStagePlan.SuppressDevBuildWithoutOverride, plan);
    }

    [Fact]
    public void DetermineAutoStagePlan_DevBuildWithOverride_AllowsStage()
    {
        var plan = ApplicationUpdateBackgroundService.DetermineAutoStagePlan(
            ApplicationUpdateCheckState.DevBuildComparisonSkipped,
            releaseFound: true,
            automaticStageEnabled: true,
            allowDevBuildAutoStageWithoutVersionComparison: true,
            previousAttemptedTag: null,
            latestTag: "V0.2.0-preview");

        Assert.Equal(ApplicationUpdateBackgroundService.AutoStagePlan.StageDevBuildWithOverride, plan);
    }

    [Fact]
    public void DetermineAutoStagePlan_ReleaseBuildBehavior_Unchanged()
    {
        var plan = ApplicationUpdateBackgroundService.DetermineAutoStagePlan(
            ApplicationUpdateCheckState.UpdateAvailable,
            releaseFound: true,
            automaticStageEnabled: true,
            allowDevBuildAutoStageWithoutVersionComparison: false,
            previousAttemptedTag: null,
            latestTag: "V0.2.0");

        Assert.Equal(ApplicationUpdateBackgroundService.AutoStagePlan.StageReleaseBuild, plan);
    }

    private static ApplicationUpdateDetectionService BuildService(string currentVersion, GitHubReleaseSummary? release)
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

        return new ApplicationUpdateDetectionService(metadataProvider, new FakeReleaseLookupService(release), options);
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

    private sealed class FakeReleaseLookupService : IGitHubReleaseLookupService
    {
        private readonly GitHubReleaseSummary? _release;

        public FakeReleaseLookupService(GitHubReleaseSummary? release)
        {
            _release = release;
        }

        public Task<GitHubReleaseSummary?> GetLatestApplicableReleaseAsync(bool allowPreviewReleases, CancellationToken cancellationToken)
        {
            return Task.FromResult(_release);
        }
    }
}
