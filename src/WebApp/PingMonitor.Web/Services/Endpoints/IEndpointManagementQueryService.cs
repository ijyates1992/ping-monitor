using PingMonitor.Web.ViewModels.Endpoints;

namespace PingMonitor.Web.Services.Endpoints;

public interface IEndpointManagementQueryService
{
    Task<ManageEndpointsPageViewModel> GetManagePageAsync(CancellationToken cancellationToken);
    Task<EditEndpointOptionsViewModel> GetEditOptionsAsync(string assignmentId, CancellationToken cancellationToken);
}
