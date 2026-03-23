using PingMonitor.Web.ViewModels.Status;

namespace PingMonitor.Web.Services.Status;

public interface IEndpointStatusQueryService
{
    Task<EndpointStatusPageViewModel> GetStatusPageAsync(string? state, string? agent, string? search, CancellationToken cancellationToken);
}
