using PingMonitor.Web.Contracts.Hello;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

public interface IAgentHelloService
{
    Task<AgentHelloResponse> ProcessHelloAsync(Agent agent, AgentHelloRequest request, CancellationToken cancellationToken);
}
