using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.ViewModels.Endpoints;

namespace PingMonitor.Web.Services.Endpoints;

internal sealed class EndpointCreationQueryService : IEndpointCreationQueryService
{
    private readonly PingMonitorDbContext _dbContext;

    public EndpointCreationQueryService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CreateEndpointPageOptions> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var agentOptions = await _dbContext.Agents.AsNoTracking()
            .Where(x => x.Enabled && !x.ApiKeyRevoked)
            .OrderBy(x => x.InstanceId)
            .Select(x => new CreateEndpointAgentOptionViewModel
            {
                AgentId = x.AgentId,
                DisplayName = string.IsNullOrWhiteSpace(x.Name)
                    ? x.InstanceId
                    : $"{x.Name} ({x.InstanceId})"
            })
            .ToArrayAsync(cancellationToken);

        var endpointOptions = await _dbContext.Endpoints.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new CreateEndpointDependencyOptionViewModel
            {
                EndpointId = x.EndpointId,
                EndpointName = x.Name
            })
            .ToArrayAsync(cancellationToken);

        return new CreateEndpointPageOptions
        {
            Agents = agentOptions,
            DependencyEndpoints = endpointOptions
        };
    }
}
