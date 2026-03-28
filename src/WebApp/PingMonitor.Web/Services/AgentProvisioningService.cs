using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.Backups;

namespace PingMonitor.Web.Services;

internal sealed class AgentProvisioningService : IAgentProvisioningService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IAgentApiKeyHasher _apiKeyHasher;
    private readonly IAgentPackageBuilder _agentPackageBuilder;
    private readonly IApplicationSettingsService _applicationSettingsService;
    private readonly IConfigurationChangeBackupSignal _configurationChangeBackupSignal;
    private readonly ILogger<AgentProvisioningService> _logger;

    public AgentProvisioningService(
        PingMonitorDbContext dbContext,
        IAgentApiKeyHasher apiKeyHasher,
        IAgentPackageBuilder agentPackageBuilder,
        IApplicationSettingsService applicationSettingsService,
        IConfigurationChangeBackupSignal configurationChangeBackupSignal,
        ILogger<AgentProvisioningService> logger)
    {
        _dbContext = dbContext;
        _apiKeyHasher = apiKeyHasher;
        _agentPackageBuilder = agentPackageBuilder;
        _applicationSettingsService = applicationSettingsService;
        _configurationChangeBackupSignal = configurationChangeBackupSignal;
        _logger = logger;
    }

    public async Task<AgentProvisioningResult> ProvisionAsync(string agentName, CancellationToken cancellationToken)
    {
        var normalizedName = agentName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("Agent name is required.", nameof(agentName));
        }

        var now = DateTimeOffset.UtcNow;
        var settings = await _applicationSettingsService.GetCurrentAsync(cancellationToken);
        var serverUrl = ValidateServerUrl(settings.SiteUrl);
        var instanceId = await GenerateUniqueInstanceIdAsync(normalizedName, cancellationToken);
        var plainTextApiKey = GenerateApiKey();

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
            ApiKeyCreatedAtUtc = now,
            ApiKeyHash = string.Empty
        };

        agent.ApiKeyHash = _apiKeyHasher.Hash(agent, plainTextApiKey);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _dbContext.Agents.Add(agent);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var packageBytes = await _agentPackageBuilder.BuildAsync(serverUrl, instanceId, plainTextApiKey, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _configurationChangeBackupSignal.NotifyConfigurationChanged("agent-provisioned");
            _logger.LogInformation("Provisioned agent {AgentId} with instance {InstanceId}", agent.AgentId, agent.InstanceId);

            return BuildResult(agent.AgentId, agent.InstanceId, normalizedName, packageBytes);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> SetEnabledAsync(string agentId, bool enabled, CancellationToken cancellationToken)
    {
        var normalizedAgentId = NormalizeAgentId(agentId);
        var agent = await _dbContext.Agents.SingleOrDefaultAsync(x => x.AgentId == normalizedAgentId, cancellationToken);
        if (agent is null)
        {
            throw new InvalidOperationException("Agent not found.");
        }

        if (agent.Enabled == enabled)
        {
            return false;
        }

        agent.Enabled = enabled;
        await _dbContext.SaveChangesAsync(cancellationToken);
        _configurationChangeBackupSignal.NotifyConfigurationChanged("agent-enabled-state-changed");
        _logger.LogInformation("Set enabled={Enabled} for agent {AgentId}", enabled, agent.AgentId);
        return true;
    }

    public async Task<bool> RemoveAsync(string agentId, CancellationToken cancellationToken)
    {
        var normalizedAgentId = NormalizeAgentId(agentId);
        var agent = await _dbContext.Agents.SingleOrDefaultAsync(x => x.AgentId == normalizedAgentId, cancellationToken);
        if (agent is null)
        {
            throw new InvalidOperationException("Agent not found.");
        }

        var assignments = await _dbContext.MonitorAssignments
            .Where(x => x.AgentId == normalizedAgentId && x.Enabled)
            .ToListAsync(cancellationToken);

        var changed = false;
        if (agent.Enabled)
        {
            agent.Enabled = false;
            changed = true;
        }

        if (!agent.ApiKeyRevoked)
        {
            agent.ApiKeyRevoked = true;
            changed = true;
        }

        if (assignments.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var assignment in assignments)
            {
                assignment.Enabled = false;
                assignment.UpdatedAtUtc = now;
            }

            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _configurationChangeBackupSignal.NotifyConfigurationChanged("agent-removed");
        _logger.LogInformation("Removed agent {AgentId}. Agent disabled, API key revoked, and active assignments disabled.", agent.AgentId);
        return true;
    }

    public async Task<AgentProvisioningResult> RotatePackageAsync(string agentId, CancellationToken cancellationToken)
    {
        var normalizedAgentId = NormalizeAgentId(agentId);
        var agent = await _dbContext.Agents.SingleOrDefaultAsync(x => x.AgentId == normalizedAgentId, cancellationToken);
        if (agent is null)
        {
            throw new InvalidOperationException("Agent not found.");
        }

        var settings = await _applicationSettingsService.GetCurrentAsync(cancellationToken);
        var serverUrl = ValidateServerUrl(settings.SiteUrl);
        var now = DateTimeOffset.UtcNow;
        var plainTextApiKey = GenerateApiKey();
        var apiKeyHash = _apiKeyHasher.Hash(agent, plainTextApiKey);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            agent.ApiKeyHash = apiKeyHash;
            agent.ApiKeyCreatedAtUtc = now;
            agent.ApiKeyRevoked = false;
            await _dbContext.SaveChangesAsync(cancellationToken);

            var packageBytes = await _agentPackageBuilder.BuildAsync(serverUrl, agent.InstanceId, plainTextApiKey, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Rotated API key and package for agent {AgentId} ({InstanceId})", agent.AgentId, agent.InstanceId);

            return BuildResult(agent.AgentId, agent.InstanceId, agent.Name ?? agent.InstanceId, packageBytes);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string NormalizeAgentId(string agentId)
    {
        var normalizedAgentId = agentId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedAgentId))
        {
            throw new InvalidOperationException("Agent ID is required.");
        }

        return normalizedAgentId;
    }

    private static AgentProvisioningResult BuildResult(string agentId, string instanceId, string agentName, byte[] packageBytes)
    {
        return new AgentProvisioningResult
        {
            AgentId = agentId,
            InstanceId = instanceId,
            AgentName = agentName,
            PackageFileName = BuildFileName(instanceId),
            PackageBytes = packageBytes
        };
    }

    private static string ValidateServerUrl(string? configuredServerUrl)
    {
        if (string.IsNullOrWhiteSpace(configuredServerUrl))
        {
            throw new InvalidOperationException("Agent provisioning Site URL is not configured. Set it on /admin.");
        }

        var value = configuredServerUrl.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Admin Site URL must be an absolute URL.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Admin Site URL must use HTTPS.");
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
