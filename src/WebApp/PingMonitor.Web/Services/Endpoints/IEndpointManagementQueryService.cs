using PingMonitor.Web.ViewModels.Endpoints;

namespace PingMonitor.Web.Services.Endpoints;

public interface IEndpointManagementQueryService
{
    Task<ManageEndpointsPageViewModel> GetManagePageAsync(string? groupId, CancellationToken cancellationToken);
    Task<EditEndpointOptionsViewModel> GetEditOptionsAsync(string assignmentId, CancellationToken cancellationToken);
    Task<RemoveEndpointDetails?> GetRemoveDetailsAsync(string assignmentId, CancellationToken cancellationToken);
}

public sealed class RemoveEndpointDetails
{
    public string AssignmentId { get; init; } = string.Empty;
    public string EndpointId { get; init; } = string.Empty;
    public string EndpointName { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string AgentDisplay { get; init; } = string.Empty;
}
