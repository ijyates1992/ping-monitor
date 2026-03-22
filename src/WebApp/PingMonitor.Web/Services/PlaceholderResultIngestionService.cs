using PingMonitor.Web.Contracts.Results;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

internal sealed class PlaceholderResultIngestionService : IResultIngestionService
{
    public Task<SubmitResultsResponse> IngestAsync(Agent agent, SubmitResultsRequest request, CancellationToken cancellationToken)
    {
        var response = new SubmitResultsResponse(
            Accepted: true,
            AcceptedCount: request.Results.Count,
            Duplicate: false,
            ServerTimeUtc: DateTimeOffset.UtcNow);

        return Task.FromResult(response);
    }
}
