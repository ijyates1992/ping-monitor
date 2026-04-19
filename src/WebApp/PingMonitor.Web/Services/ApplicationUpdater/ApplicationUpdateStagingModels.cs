using System.Text.Json.Serialization;

namespace PingMonitor.Web.Services.ApplicationUpdater;

public enum ApplicationUpdateStagingStatus
{
    NotAttempted = 0,
    Ready = 1,
    DownloadFailed = 2,
    ChecksumVerificationFailed = 3,
    AssetResolutionFailed = 4,
    NoApplicableRelease = 5,
    StagingFailed = 6
}

public sealed class ApplicationUpdateStagingState
{
    public string SourceRepository { get; init; } = string.Empty;
    public bool AllowPreviewReleases { get; init; }
    public string? ReleaseTag { get; init; }
    public string? ReleaseTitle { get; init; }
    public bool? ReleaseIsPrerelease { get; init; }
    public DateTimeOffset? ReleasePublishedAtUtc { get; init; }
    public string? ReleaseUrl { get; init; }
    public string? SelectedAssetName { get; init; }
    public string? SelectedChecksumAssetName { get; init; }
    public string? StagedZipPath { get; init; }
    public string? StagedChecksumPath { get; init; }
    public string? ExpectedSha256 { get; init; }
    public string? ActualSha256 { get; init; }
    public bool ChecksumVerified { get; init; }
    public DateTimeOffset? StagedAtUtc { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ApplicationUpdateStagingStatus Status { get; init; }
    public string? FailureMessage { get; init; }
    public DateTimeOffset LastUpdatedAtUtc { get; init; }
}

public sealed class ApplicationUpdateStagingResult
{
    public required ApplicationUpdateStagingState State { get; init; }
}
