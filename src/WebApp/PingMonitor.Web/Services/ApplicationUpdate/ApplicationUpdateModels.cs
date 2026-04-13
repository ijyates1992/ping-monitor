namespace PingMonitor.Web.Services.ApplicationUpdate;

public enum ApplicationUpdateCheckState
{
    CheckDisabled,
    CheckNotPerformed,
    CurrentVersionUnknown,
    LatestVersionUnknown,
    UpdateAvailable,
    AlreadyUpToDate,
    CheckFailed
}

public sealed class ApplicationReleaseMetadata
{
    public string TagName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAtUtc { get; init; }
    public bool IsPrerelease { get; init; }
    public string HtmlUrl { get; init; } = string.Empty;
}

public sealed class ApplicationUpdateCheckResult
{
    public ApplicationUpdateCheckState State { get; init; } = ApplicationUpdateCheckState.CheckNotPerformed;
    public string CurrentVersionRaw { get; init; } = string.Empty;
    public string? CurrentVersionNormalized { get; init; }
    public string? LatestVersionRaw { get; init; }
    public string? LatestVersionNormalized { get; init; }
    public string? ReleaseName { get; init; }
    public DateTimeOffset? ReleasePublishedAtUtc { get; init; }
    public bool? ReleaseIsPrerelease { get; init; }
    public string? ReleaseUrl { get; init; }
    public string? ErrorMessage { get; init; }
}
