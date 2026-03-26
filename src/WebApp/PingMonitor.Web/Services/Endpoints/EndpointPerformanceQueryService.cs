using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Endpoints;

namespace PingMonitor.Web.Services.Endpoints;

internal sealed class EndpointPerformanceQueryService : IEndpointPerformanceQueryService
{
    private sealed class CheckResultRow
    {
        public required DateTimeOffset CheckedAtUtc { get; init; }
        public required bool Success { get; init; }
        public int? RoundTripMs { get; init; }
    }

    private readonly PingMonitorDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserAccessScopeService _userAccessScopeService;

    public EndpointPerformanceQueryService(PingMonitorDbContext dbContext, IHttpContextAccessor httpContextAccessor, IUserAccessScopeService userAccessScopeService)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _userAccessScopeService = userAccessScopeService;
    }

    public async Task<EndpointPerformancePageViewModel?> GetPerformancePageAsync(
        string assignmentId,
        string? range,
        CancellationToken cancellationToken)
    {
        var normalizedAssignmentId = assignmentId.Trim();
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null || !await _userAccessScopeService.CanAccessAssignmentAsync(principal, normalizedAssignmentId, cancellationToken))
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(normalizedAssignmentId))
        {
            return null;
        }

        var context = await (
            from assignment in _dbContext.MonitorAssignments.AsNoTracking()
            join endpoint in _dbContext.Endpoints.AsNoTracking() on assignment.EndpointId equals endpoint.EndpointId
            join agent in _dbContext.Agents.AsNoTracking() on assignment.AgentId equals agent.AgentId
            where assignment.AssignmentId == normalizedAssignmentId
            select new
            {
                assignment.AssignmentId,
                EndpointName = endpoint.Name,
                IconKey = endpoint.IconKey,
                endpoint.Target,
                AgentDisplay = string.IsNullOrWhiteSpace(agent.Name)
                    ? agent.InstanceId
                    : $"{agent.Name} ({agent.InstanceId})"
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (context is null)
        {
            return null;
        }

        var parsedRange = EndpointPerformanceRange.Parse(range);
        var windowEnd = DateTimeOffset.UtcNow;
        var windowStart = windowEnd - parsedRange.Duration;

        var checkResults = await _dbContext.CheckResults.AsNoTracking()
            .Where(x => x.AssignmentId == normalizedAssignmentId
                        && x.CheckedAtUtc >= windowStart
                        && x.CheckedAtUtc <= windowEnd)
            .OrderBy(x => x.CheckedAtUtc)
            .Select(x => new CheckResultRow
            {
                CheckedAtUtc = x.CheckedAtUtc,
                Success = x.Success,
                RoundTripMs = x.RoundTripMs
            })
            .ToArrayAsync(cancellationToken);

        var successfulRttSamples = checkResults
            .Where(x => x.Success && x.RoundTripMs.HasValue)
            .Select(x => new TimeSeriesPointViewModel
            {
                TimestampUtc = x.CheckedAtUtc,
                Value = x.RoundTripMs!.Value
            })
            .ToArray();

        var jitterSeries = BuildJitterSeries(successfulRttSamples);
        var failureSeries = BuildFailureSeries(checkResults, windowStart, windowEnd, parsedRange.BucketSize);

        return new EndpointPerformancePageViewModel
        {
            AssignmentId = context.AssignmentId,
            EndpointName = context.EndpointName,
            IconKey = EndpointIconCatalog.Normalize(context.IconKey),
            Target = context.Target,
            AgentDisplay = context.AgentDisplay,
            SelectedRange = parsedRange.Range,
            WindowStartUtc = windowStart,
            WindowEndUtc = windowEnd,
            AvailableRanges = EndpointPerformanceRange.Options,
            RttSeries = successfulRttSamples,
            JitterSeries = jitterSeries,
            FailureSeries = failureSeries
        };
    }

    private static IReadOnlyList<JitterPointViewModel> BuildJitterSeries(IReadOnlyList<TimeSeriesPointViewModel> successfulRttSamples)
    {
        if (successfulRttSamples.Count < 2)
        {
            return [];
        }

        var jitterPoints = new List<JitterPointViewModel>(successfulRttSamples.Count - 1);
        for (var index = 1; index < successfulRttSamples.Count; index++)
        {
            var current = successfulRttSamples[index];
            var previous = successfulRttSamples[index - 1];
            jitterPoints.Add(new JitterPointViewModel
            {
                TimestampUtc = current.TimestampUtc,
                JitterMs = Math.Abs(current.Value - previous.Value)
            });
        }

        return jitterPoints;
    }

    private static IReadOnlyList<FailureBucketViewModel> BuildFailureSeries(
        IReadOnlyList<CheckResultRow> checkResults,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        TimeSpan bucketSize)
    {
        var bucketCount = (int)Math.Ceiling((windowEnd - windowStart).TotalSeconds / bucketSize.TotalSeconds);
        if (bucketCount <= 0)
        {
            return [];
        }

        var buckets = new FailureBucketViewModel[bucketCount];
        for (var index = 0; index < bucketCount; index++)
        {
            var bucketStart = windowStart + TimeSpan.FromSeconds(index * bucketSize.TotalSeconds);
            var bucketEnd = bucketStart + bucketSize;
            buckets[index] = new FailureBucketViewModel
            {
                BucketStartUtc = bucketStart,
                BucketEndUtc = bucketEnd,
                SuccessfulCount = 0,
                FailedCount = 0
            };
        }

        foreach (var checkResult in checkResults)
        {
            var checkedAtUtc = checkResult.CheckedAtUtc;
            var bucketIndex = (int)Math.Floor((checkedAtUtc - windowStart).TotalSeconds / bucketSize.TotalSeconds);
            if (bucketIndex < 0)
            {
                continue;
            }

            if (bucketIndex >= bucketCount)
            {
                bucketIndex = bucketCount - 1;
            }

            var bucket = buckets[bucketIndex];
            bucket = checkResult.Success
                ? new FailureBucketViewModel
                {
                    BucketStartUtc = bucket.BucketStartUtc,
                    BucketEndUtc = bucket.BucketEndUtc,
                    SuccessfulCount = bucket.SuccessfulCount + 1,
                    FailedCount = bucket.FailedCount
                }
                : new FailureBucketViewModel
                {
                    BucketStartUtc = bucket.BucketStartUtc,
                    BucketEndUtc = bucket.BucketEndUtc,
                    SuccessfulCount = bucket.SuccessfulCount,
                    FailedCount = bucket.FailedCount + 1
                };

            buckets[bucketIndex] = bucket;
        }

        return buckets;
    }
}
