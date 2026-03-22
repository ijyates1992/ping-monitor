using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services;

internal sealed class DevelopmentAgentSeeder
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly DevelopmentSeedAgentOptions _options;
    private readonly IAgentApiKeyHasher _apiKeyHasher;
    private readonly ILogger<DevelopmentAgentSeeder> _logger;

    public DevelopmentAgentSeeder(
        PingMonitorDbContext dbContext,
        IOptions<DevelopmentSeedAgentOptions> options,
        IAgentApiKeyHasher apiKeyHasher,
        ILogger<DevelopmentAgentSeeder> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _apiKeyHasher = apiKeyHasher;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Development agent seeding is enabled, but no API key was supplied. Set DevelopmentSeedAgent__ApiKey before calling /api/v1/agent/hello.");
            return;
        }

        var existingAgent = await _dbContext.Agents.SingleOrDefaultAsync(x => x.InstanceId == _options.InstanceId);
        if (existingAgent is not null)
        {
            existingAgent.Name = _options.Name;
            existingAgent.Site = _options.Site;
            existingAgent.Enabled = true;
            existingAgent.ApiKeyRevoked = false;
            existingAgent.ApiKeyCreatedAtUtc = DateTimeOffset.UtcNow;
            existingAgent.ApiKeyHash = _apiKeyHasher.Hash(existingAgent, _options.ApiKey);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Updated development seed agent {InstanceId}.", _options.InstanceId);
            return;
        }

        var agent = new Agent
        {
            AgentId = Guid.NewGuid().ToString(),
            InstanceId = _options.InstanceId,
            Name = _options.Name,
            Site = _options.Site,
            Enabled = true,
            ApiKeyRevoked = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ApiKeyCreatedAtUtc = DateTimeOffset.UtcNow,
            Status = AgentHealthStatus.Offline
        };

        agent.ApiKeyHash = _apiKeyHasher.Hash(agent, _options.ApiKey);

        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Seeded development agent {InstanceId}.", _options.InstanceId);
    }
}
