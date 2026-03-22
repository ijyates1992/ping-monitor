using PingMonitor.Web.Contracts.Results;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

public interface IResultIngestionService
{
    Task<SubmitResultsResponse> IngestAsync(Agent agent, SubmitResultsRequest request, CancellationToken cancellationToken);
}
