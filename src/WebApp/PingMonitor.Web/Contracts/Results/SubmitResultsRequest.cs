namespace PingMonitor.Web.Contracts.Results;

public sealed record SubmitResultsRequest(
    DateTimeOffset SentAtUtc,
    string BatchId,
    IReadOnlyList<CheckResultDto> Results);

public sealed record SubmitResultsResponse(
    bool Accepted,
    int AcceptedCount,
    bool Duplicate,
    DateTimeOffset ServerTimeUtc);
