using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

public interface IAgentApiKeyHasher
{
    string Hash(Agent agent, string apiKey);
    bool Verify(Agent agent, string apiKey);
}
