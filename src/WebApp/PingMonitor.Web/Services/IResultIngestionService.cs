using PingMonitor.Web.Contracts.Results;

namespace PingMonitor.Web.Services;

public interface IResultIngestionService
{
    Task<SubmitResultsResponse> IngestAsync(string instanceId, SubmitResultsRequest request, CancellationToken cancellationToken);
}
