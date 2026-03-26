using PingMonitor.Web.ViewModels.Endpoints;

namespace PingMonitor.Web.Services.Endpoints;

public interface IEndpointPerformanceQueryService
{
    Task<EndpointPerformancePageViewModel?> GetPerformancePageAsync(string assignmentId, string? range, CancellationToken cancellationToken);
}
