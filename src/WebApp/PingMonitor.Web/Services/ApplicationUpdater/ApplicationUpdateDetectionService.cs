using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationMetadata;

namespace PingMonitor.Web.Services.ApplicationUpdater;

public interface IApplicationUpdateDetectionService
{
    Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(bool allowPreviewReleases, CancellationToken cancellationToken);
}

internal sealed class ApplicationUpdateDetectionService : IApplicationUpdateDetectionService
{
    private readonly IApplicationMetadataProvider _applicationMetadataProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApplicationUpdaterOptions _options;

    public ApplicationUpdateDetectionService(
        IApplicationMetadataProvider applicationMetadataProvider,
        IHttpClientFactory httpClientFactory,
        IOptions<ApplicationUpdaterOptions> options)
    {
        _applicationMetadataProvider = applicationMetadataProvider;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        var snapshot = _applicationMetadataProvider.GetSnapshot();
        var currentVersion = ReleaseVersionParser.ParseCurrent(snapshot.Version);

        if (!_options.UpdateChecksEnabled)
        {
            return ApplicationUpdateCheckResult.Disabled(snapshot.Version, allowPreviewReleases);
        }

        if (string.IsNullOrWhiteSpace(_options.GitHubOwner)
            || string.IsNullOrWhiteSpace(_options.GitHubRepository)
            || string.IsNullOrWhiteSpace(_options.GitHubApiBaseUrl))
        {
            return ApplicationUpdateCheckResult.Failed(
                snapshot.Version,
                allowPreviewReleases,
                "Update checks are misconfigured. GitHub owner/repository/API base URL are required.");
        }

        GitHubReleaseSummary? latestRelease;
        try
        {
            latestRelease = await GetLatestApplicableReleaseAsync(allowPreviewReleases, cancellationToken);
        }
        catch (Exception ex)
        {
            return ApplicationUpdateCheckResult.Failed(
                snapshot.Version,
                allowPreviewReleases,
                $"Unable to check GitHub releases: {ex.Message}");
        }

        if (latestRelease is null)
        {
            return ApplicationUpdateCheckResult.NoApplicableRelease(snapshot.Version, allowPreviewReleases, currentVersion);
        }

        if (currentVersion.IsDevBuild)
        {
            return ApplicationUpdateCheckResult.DevBuildComparisonSkipped(snapshot.Version, allowPreviewReleases, latestRelease);
        }

        if (!currentVersion.IsReleaseVersion || currentVersion.ReleaseVersion is null)
        {
            return ApplicationUpdateCheckResult.CurrentVersionUnknown(snapshot.Version, allowPreviewReleases, latestRelease);
        }

        if (!ReleaseVersionParser.TryParseReleaseVersion(latestRelease.TagName, out var latestVersion))
        {
            return ApplicationUpdateCheckResult.LatestVersionUnknown(snapshot.Version, allowPreviewReleases, latestRelease);
        }

        var comparison = latestVersion.CompareTo(currentVersion.ReleaseVersion.Value);
        if (comparison > 0)
        {
            return ApplicationUpdateCheckResult.UpdateAvailable(snapshot.Version, allowPreviewReleases, latestRelease);
        }

        return ApplicationUpdateCheckResult.UpToDate(snapshot.Version, allowPreviewReleases, latestRelease);
    }

    private async Task<GitHubReleaseSummary?> GetLatestApplicableReleaseAsync(bool allowPreviewReleases, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(ApplicationUpdateDetectionService));

        var baseUri = _options.GitHubApiBaseUrl.TrimEnd('/');
        var owner = _options.GitHubOwner.Trim();
        var repository = _options.GitHubRepository.Trim();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUri}/repos/{owner}/{repository}/releases?per_page=30");

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("PingMonitor-UpdateCheck/1.0");

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var reason = response.ReasonPhrase ?? "Unknown error";
            throw new InvalidOperationException(
                $"GitHub API returned {(int)response.StatusCode} ({reason}).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("GitHub API response did not return an array of releases.");
        }

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

            return new GitHubReleaseSummary
            {
                TagName = tagName.Trim(),
                Name = GetString(release, "name")?.Trim(),
                IsPrerelease = isPrerelease,
                HtmlUrl = GetString(release, "html_url")?.Trim(),
                PublishedAtUtc = TryGetDateTimeOffset(release, "published_at")
            };
        }

        return null;
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

public enum ApplicationUpdateCheckState
{
    CheckNotPerformed = 0,
    UpdateCheckDisabled = 1,
    NoApplicableRelease = 2,
    UpdateAvailable = 3,
    UpToDate = 4,
    CheckFailed = 5,
    DevBuildComparisonSkipped = 6,
    CurrentVersionUnknown = 7,
    LatestVersionUnknown = 8
}

public sealed class ApplicationUpdateCheckResult
{
    public ApplicationUpdateCheckState State { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public bool AllowPreviewReleases { get; init; }
    public string Message { get; init; } = string.Empty;
    public GitHubReleaseSummary? LatestApplicableRelease { get; init; }

    internal static ApplicationUpdateCheckResult NotPerformed(string currentVersion, bool allowPreviewReleases)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.CheckNotPerformed,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            Message = "No update check has been performed yet."
        };
    }

    internal static ApplicationUpdateCheckResult Disabled(string currentVersion, bool allowPreviewReleases)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.UpdateCheckDisabled,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            Message = "Update checks are disabled by configuration."
        };
    }

    internal static ApplicationUpdateCheckResult Failed(string currentVersion, bool allowPreviewReleases, string message)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.CheckFailed,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            Message = message,
        };
    }

    internal static ApplicationUpdateCheckResult NoApplicableRelease(string currentVersion, bool allowPreviewReleases, VersionParseResult parsedCurrentVersion)
    {
        var currentVersionDescriptor = parsedCurrentVersion.IsReleaseVersion
            ? "Current version parsed successfully."
            : "Current version could not be parsed as a strict release version.";

        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.NoApplicableRelease,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            Message = $"No applicable GitHub release was found for the current preview-release filter. {currentVersionDescriptor}"
        };
    }

    internal static ApplicationUpdateCheckResult UpdateAvailable(string currentVersion, bool allowPreviewReleases, GitHubReleaseSummary latestRelease)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.UpdateAvailable,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            Message = "A newer release is available.",
            LatestApplicableRelease = latestRelease
        };
    }

    internal static ApplicationUpdateCheckResult UpToDate(string currentVersion, bool allowPreviewReleases, GitHubReleaseSummary latestRelease)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.UpToDate,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            Message = "Current version is already up to date.",
            LatestApplicableRelease = latestRelease
        };
    }

    internal static ApplicationUpdateCheckResult DevBuildComparisonSkipped(string currentVersion, bool allowPreviewReleases, GitHubReleaseSummary latestRelease)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.DevBuildComparisonSkipped,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            LatestApplicableRelease = latestRelease,
            Message = "This instance is running a DEV build. Semantic comparison against release tags is skipped; latest GitHub release is shown for operator reference only."
        };
    }

    internal static ApplicationUpdateCheckResult CurrentVersionUnknown(string currentVersion, bool allowPreviewReleases, GitHubReleaseSummary latestRelease)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.CurrentVersionUnknown,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            LatestApplicableRelease = latestRelease,
            Message = "Current version format is not recognized as strict Vx.x.x and is not a DEV build. Comparison could not be performed safely."
        };
    }

    internal static ApplicationUpdateCheckResult LatestVersionUnknown(string currentVersion, bool allowPreviewReleases, GitHubReleaseSummary latestRelease)
    {
        return new ApplicationUpdateCheckResult
        {
            State = ApplicationUpdateCheckState.LatestVersionUnknown,
            CurrentVersion = currentVersion,
            AllowPreviewReleases = allowPreviewReleases,
            LatestApplicableRelease = latestRelease,
            Message = "Latest release tag is not a strict Vx.x.x version. Comparison could not be performed safely."
        };
    }
}

public sealed class GitHubReleaseSummary
{
    public string TagName { get; init; } = string.Empty;
    public string? Name { get; init; }
    public bool IsPrerelease { get; init; }
    public string? HtmlUrl { get; init; }
    public DateTimeOffset? PublishedAtUtc { get; init; }
}
