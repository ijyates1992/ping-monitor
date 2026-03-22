using Microsoft.AspNetCore.Identity;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

internal sealed class AgentApiKeyHasher : IAgentApiKeyHasher
{
    private readonly PasswordHasher<Agent> _passwordHasher = new();

    public string Hash(Agent agent, string apiKey)
    {
        return _passwordHasher.HashPassword(agent, apiKey);
    }

    public bool Verify(Agent agent, string apiKey)
    {
        var result = _passwordHasher.VerifyHashedPassword(agent, agent.ApiKeyHash, apiKey);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
