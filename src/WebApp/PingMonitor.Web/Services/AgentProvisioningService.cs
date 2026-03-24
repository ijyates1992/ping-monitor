using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services;

internal sealed class AgentProvisioningService : IAgentProvisioningService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IAgentApiKeyHasher _apiKeyHasher;
    private readonly IAgentPackageBuilder _agentPackageBuilder;
    private readonly AgentProvisioningOptions _options;
    private readonly ILogger<AgentProvisioningService> _logger;

    public AgentProvisioningService(
        PingMonitorDbContext dbContext,
        IAgentApiKeyHasher apiKeyHasher,
        IAgentPackageBuilder agentPackageBuilder,
        IOptions<AgentProvisioningOptions> options,
        ILogger<AgentProvisioningService> logger)
    {
        _dbContext = dbContext;
        _apiKeyHasher = apiKeyHasher;
        _agentPackageBuilder = agentPackageBuilder;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AgentProvisioningResult> ProvisionAsync(string agentName, CancellationToken cancellationToken)
    {
        var normalizedName = agentName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("Agent name is required.", nameof(agentName));
        }

        var serverUrl = ValidateServerUrl(_options.ServerUrl);
        var now = DateTimeOffset.UtcNow;
        var instanceId = await GenerateUniqueInstanceIdAsync(normalizedName, cancellationToken);

        var agent = new Agent
        {
            AgentId = $"agent_{Guid.NewGuid():N}",
            InstanceId = instanceId,
            Name = normalizedName,
            Site = null,
            Enabled = true,
            ApiKeyRevoked = false,
            LastHeartbeatUtc = null,
            LastSeenUtc = null,
            Status = AgentHealthStatus.Offline,
            AgentVersion = null,
            Platform = null,
            MachineName = null,
            CreatedAtUtc = now,
            ApiKeyCreatedAtUtc = now
        };

        var plainTextApiKey = GenerateApiKey();
        agent.ApiKeyHash = _apiKeyHasher.Hash(agent, plainTextApiKey);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _dbContext.Agents.Add(agent);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var packageBytes = await _agentPackageBuilder.BuildAsync(serverUrl, instanceId, plainTextApiKey, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Provisioned agent {AgentId} with instance {InstanceId}", agent.AgentId, agent.InstanceId);

            return new AgentProvisioningResult
            {
                AgentId = agent.AgentId,
                InstanceId = agent.InstanceId,
                AgentName = normalizedName,
                PackageFileName = BuildFileName(instanceId),
                PackageBytes = packageBytes
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string ValidateServerUrl(string? configuredServerUrl)
    {
        if (string.IsNullOrWhiteSpace(configuredServerUrl))
        {
            throw new InvalidOperationException("Agent provisioning is not configured. Set AgentProvisioning:ServerUrl.");
        }

        var value = configuredServerUrl.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("AgentProvisioning:ServerUrl must be an absolute URL.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("AgentProvisioning:ServerUrl must use HTTPS.");
        }

        return uri.ToString().TrimEnd('/');
    }

    private async Task<string> GenerateUniqueInstanceIdAsync(string agentName, CancellationToken cancellationToken)
    {
        var baseId = BuildInstanceIdBase(agentName);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var candidate = attempt == 0 ? baseId : $"{baseId}-{attempt + 1}";
            var exists = await _dbContext.Agents
                .AsNoTracking()
                .AnyAsync(agent => agent.InstanceId == candidate, cancellationToken);

            if (!exists)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique instance ID for this agent name.");
    }

    private static string BuildInstanceIdBase(string agentName)
    {
        var lowered = agentName.Trim().ToLowerInvariant();
        var builder = new StringBuilder(lowered.Length);
        var previousDash = false;

        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousDash = false;
                continue;
            }

            if (previousDash)
            {
                continue;
            }

            builder.Append('-');
            previousDash = true;
        }

        var normalized = builder.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "agent";
        }

        return normalized.Length <= 48
            ? normalized
            : normalized[..48].TrimEnd('-');
    }

    private static string GenerateApiKey()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string BuildFileName(string instanceId)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        return $"ping-agent-{instanceId}-{stamp}.zip";
    }
}
