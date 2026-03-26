using PingMonitor.Web.ViewModels.Endpoints;

namespace PingMonitor.Web.Services.Endpoints;

public interface IEndpointCreationQueryService
{
    Task<CreateEndpointPageOptions> GetOptionsAsync(CancellationToken cancellationToken);
}

public sealed class CreateEndpointPageOptions
{
    public IReadOnlyList<CreateEndpointAgentOptionViewModel> Agents { get; init; } = [];
    public IReadOnlyList<CreateEndpointDependencyOptionViewModel> DependencyEndpoints { get; init; } = [];
    public IReadOnlyList<EndpointGroupOptionViewModel> Groups { get; init; } = [];
}
