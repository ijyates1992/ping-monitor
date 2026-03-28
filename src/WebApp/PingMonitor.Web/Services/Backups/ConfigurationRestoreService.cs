using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
using EndpointModel = PingMonitor.Web.Models.Endpoint;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationRestoreService
{
    Task<RestoreConfigurationResponse> RestoreMergeAsync(RestoreConfigurationRequest request, CancellationToken cancellationToken);
}

public sealed class ConfigurationRestoreService : IConfigurationRestoreService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IConfigurationBackupDocumentLoader _documentLoader;
    private readonly ILogger<ConfigurationRestoreService> _logger;

    public ConfigurationRestoreService(
        PingMonitorDbContext dbContext,
        IConfigurationBackupDocumentLoader documentLoader,
        ILogger<ConfigurationRestoreService> logger)
    {
        _dbContext = dbContext;
        _documentLoader = documentLoader;
        _logger = logger;
    }

    public async Task<RestoreConfigurationResponse> RestoreMergeAsync(RestoreConfigurationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FileId))
        {
            throw new InvalidOperationException("Backup file is required.");
        }

        var selectedSections = request.SelectedSections
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => ConfigurationBackupSections.All.Contains(x, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (selectedSections.Length == 0)
        {
            throw new InvalidOperationException("Select at least one restore section.");
        }

        var backup = await _documentLoader.LoadValidatedDocumentAsync(request.FileId, cancellationToken);
        EnsureSelectedSectionsExist(selectedSections, backup);

        _logger.LogInformation(
            "Starting configuration restore in merge mode for {FileId}. Sections: {Sections}.",
            request.FileId,
            string.Join(",", selectedSections));

        var sectionResults = new List<RestoreSectionResult>();
        var endpointIdMapping = new Dictionary<string, string>(StringComparer.Ordinal);
        var agentIdMapping = new Dictionary<string, string>(StringComparer.Ordinal);

        if (selectedSections.Contains(ConfigurationBackupSections.Agents, StringComparer.Ordinal))
        {
            sectionResults.Add(await RestoreAgentsAsync(backup, agentIdMapping, cancellationToken));
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Endpoints, StringComparer.Ordinal))
        {
            sectionResults.Add(await RestoreEndpointsAsync(backup, endpointIdMapping, cancellationToken));
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Assignments, StringComparer.Ordinal))
        {
            sectionResults.Add(await RestoreAssignmentsAsync(backup, endpointIdMapping, agentIdMapping, cancellationToken));
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Identity, StringComparer.Ordinal))
        {
            sectionResults.Add(await RestoreIdentityAsync(backup, cancellationToken));
        }

        _logger.LogInformation(
            "Completed configuration restore in merge mode for {FileId}.",
            request.FileId);

        return new RestoreConfigurationResponse
        {
            FileId = request.FileId,
            BackupName = backup.BackupName,
            SelectedSections = selectedSections,
            SectionResults = sectionResults
        };
    }

    private static void EnsureSelectedSectionsExist(IReadOnlyCollection<string> selectedSections, ConfigurationBackupDocument backup)
    {
        foreach (var section in selectedSections)
        {
            var present = section switch
            {
                ConfigurationBackupSections.Agents => backup.Sections.Agents is not null,
                ConfigurationBackupSections.Endpoints => backup.Sections.Endpoints is not null,
                ConfigurationBackupSections.Assignments => backup.Sections.Assignments is not null,
                ConfigurationBackupSections.Identity => backup.Sections.Identity is not null,
                _ => false
            };

            if (!present)
            {
                throw new InvalidOperationException($"Section '{section}' is not present in the selected backup.");
            }
        }
    }

    private async Task<RestoreSectionResult> RestoreAgentsAsync(
        ConfigurationBackupDocument backup,
        IDictionary<string, string> agentIdMapping,
        CancellationToken cancellationToken)
    {
        var result = new RestoreSectionResult { Section = ConfigurationBackupSections.Agents };
        var sourceAgents = backup.Sections.Agents ?? [];

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingAgents = await _dbContext.Agents.ToListAsync(cancellationToken);
        foreach (var source in sourceAgents)
        {
            var target = existingAgents.FirstOrDefault(x => string.Equals(x.InstanceId, source.InstanceId, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                var createdAgentId = string.IsNullOrWhiteSpace(source.AgentId) ? Guid.NewGuid().ToString() : source.AgentId;
                var newAgent = new Agent
                {
                    AgentId = createdAgentId,
                    InstanceId = source.InstanceId,
                    Name = source.Name,
                    Site = source.Site,
                    Enabled = source.Enabled,
                    AgentVersion = source.AgentVersion,
                    Platform = source.Platform,
                    MachineName = source.MachineName,
                    CreatedAtUtc = source.CreatedAtUtc == default ? DateTimeOffset.UtcNow : source.CreatedAtUtc,
                    ApiKeyHash = "RESTORE_REQUIRED",
                    ApiKeyCreatedAtUtc = DateTimeOffset.UtcNow,
                    ApiKeyRevoked = true,
                    Status = AgentHealthStatus.Offline
                };

                _dbContext.Agents.Add(newAgent);
                existingAgents.Add(newAgent);
                result.InsertedCount++;
                agentIdMapping[source.AgentId] = newAgent.AgentId;
            }
            else
            {
                target.Name = source.Name;
                target.Site = source.Site;
                target.Enabled = source.Enabled;
                target.AgentVersion = source.AgentVersion;
                target.Platform = source.Platform;
                target.MachineName = source.MachineName;
                result.UpdatedCount++;
                agentIdMapping[source.AgentId] = target.AgentId;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Restored section {Section}. Inserted {InsertedCount}, updated {UpdatedCount}, skipped {SkippedCount}, errors {ErrorCount}.",
            result.Section,
            result.InsertedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);

        return result;
    }

    private async Task<RestoreSectionResult> RestoreEndpointsAsync(
        ConfigurationBackupDocument backup,
        IDictionary<string, string> endpointIdMapping,
        CancellationToken cancellationToken)
    {
        var result = new RestoreSectionResult { Section = ConfigurationBackupSections.Endpoints };
        var sourceEndpoints = backup.Sections.Endpoints ?? [];

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingEndpoints = await _dbContext.Endpoints.ToListAsync(cancellationToken);
        var existingDependencies = await _dbContext.EndpointDependencies.ToListAsync(cancellationToken);

        foreach (var source in sourceEndpoints)
        {
            var target = existingEndpoints.FirstOrDefault(x =>
                string.Equals(x.Name, source.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Target, source.Target, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                var createdEndpointId = string.IsNullOrWhiteSpace(source.EndpointId) ? Guid.NewGuid().ToString() : source.EndpointId;
                var newEndpoint = new EndpointModel
                {
                    EndpointId = createdEndpointId,
                    Name = source.Name,
                    Target = source.Target,
                    IconKey = string.IsNullOrWhiteSpace(source.IconKey) ? "generic" : source.IconKey,
                    Enabled = source.Enabled,
                    Tags = source.Tags.ToList(),
                    Notes = source.Notes,
                    CreatedAtUtc = source.CreatedAtUtc == default ? DateTimeOffset.UtcNow : source.CreatedAtUtc
                };

                _dbContext.Endpoints.Add(newEndpoint);
                existingEndpoints.Add(newEndpoint);
                endpointIdMapping[source.EndpointId] = newEndpoint.EndpointId;
                result.InsertedCount++;
            }
            else
            {
                target.IconKey = string.IsNullOrWhiteSpace(source.IconKey) ? "generic" : source.IconKey;
                target.Enabled = source.Enabled;
                target.Tags = source.Tags.ToList();
                target.Notes = source.Notes;
                endpointIdMapping[source.EndpointId] = target.EndpointId;
                result.UpdatedCount++;
            }
        }

        foreach (var source in sourceEndpoints)
        {
            if (!endpointIdMapping.TryGetValue(source.EndpointId, out var endpointId))
            {
                continue;
            }

            foreach (var sourceDependencyId in source.DependsOnEndpointIds)
            {
                if (!endpointIdMapping.TryGetValue(sourceDependencyId, out var dependsOnEndpointId))
                {
                    result.SkippedCount++;
                    result.Warnings.Add($"Skipped dependency mapping for endpoint '{source.Name}' because dependency endpoint id '{sourceDependencyId}' was not available in restore scope.");
                    continue;
                }

                if (string.Equals(endpointId, dependsOnEndpointId, StringComparison.Ordinal))
                {
                    result.SkippedCount++;
                    result.Warnings.Add($"Skipped self-dependency for endpoint '{source.Name}'.");
                    continue;
                }

                var exists = existingDependencies.Any(x => string.Equals(x.EndpointId, endpointId, StringComparison.Ordinal)
                    && string.Equals(x.DependsOnEndpointId, dependsOnEndpointId, StringComparison.Ordinal));
                if (exists)
                {
                    continue;
                }

                var dependency = new EndpointDependency
                {
                    EndpointDependencyId = Guid.NewGuid().ToString(),
                    EndpointId = endpointId,
                    DependsOnEndpointId = dependsOnEndpointId,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };

                _dbContext.EndpointDependencies.Add(dependency);
                existingDependencies.Add(dependency);
                result.InsertedCount++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Restored section {Section}. Inserted {InsertedCount}, updated {UpdatedCount}, skipped {SkippedCount}, errors {ErrorCount}.",
            result.Section,
            result.InsertedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);

        return result;
    }

    private async Task<RestoreSectionResult> RestoreAssignmentsAsync(
        ConfigurationBackupDocument backup,
        IDictionary<string, string> endpointIdMapping,
        IDictionary<string, string> agentIdMapping,
        CancellationToken cancellationToken)
    {
        var result = new RestoreSectionResult { Section = ConfigurationBackupSections.Assignments };
        var sourceAssignments = backup.Sections.Assignments ?? [];

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingAssignments = await _dbContext.MonitorAssignments.ToListAsync(cancellationToken);
        var existingAgentIds = await _dbContext.Agents.AsNoTracking().Select(x => x.AgentId).ToListAsync(cancellationToken);
        var existingEndpointIds = await _dbContext.Endpoints.AsNoTracking().Select(x => x.EndpointId).ToListAsync(cancellationToken);
        var existingAgentIdSet = existingAgentIds.ToHashSet(StringComparer.Ordinal);
        var existingEndpointIdSet = existingEndpointIds.ToHashSet(StringComparer.Ordinal);

        foreach (var source in sourceAssignments)
        {
            var resolvedAgentId = ResolveMappedOrExistingId(source.AgentId, agentIdMapping, existingAgentIdSet);
            var resolvedEndpointId = ResolveMappedOrExistingId(source.EndpointId, endpointIdMapping, existingEndpointIdSet);
            if (resolvedAgentId is null || resolvedEndpointId is null)
            {
                result.SkippedCount++;
                result.Warnings.Add($"Skipped assignment '{source.AssignmentId}' because referenced agent/endpoint could not be resolved in merge scope.");
                continue;
            }

            if (!TryParseCheckType(source.CheckType, out var checkType))
            {
                result.SkippedCount++;
                result.Warnings.Add($"Skipped assignment '{source.AssignmentId}' due to unsupported checkType '{source.CheckType}'.");
                continue;
            }

            var target = existingAssignments.FirstOrDefault(x =>
                string.Equals(x.AgentId, resolvedAgentId, StringComparison.Ordinal)
                && string.Equals(x.EndpointId, resolvedEndpointId, StringComparison.Ordinal)
                && x.CheckType == checkType);

            if (target is null)
            {
                var assignment = new MonitorAssignment
                {
                    AssignmentId = string.IsNullOrWhiteSpace(source.AssignmentId) ? Guid.NewGuid().ToString() : source.AssignmentId,
                    AgentId = resolvedAgentId,
                    EndpointId = resolvedEndpointId,
                    CheckType = checkType,
                    Enabled = source.Enabled,
                    PingIntervalSeconds = source.PingIntervalSeconds,
                    RetryIntervalSeconds = source.RetryIntervalSeconds,
                    TimeoutMs = source.TimeoutMs,
                    FailureThreshold = source.FailureThreshold,
                    RecoveryThreshold = source.RecoveryThreshold,
                    CreatedAtUtc = source.CreatedAtUtc == default ? DateTimeOffset.UtcNow : source.CreatedAtUtc,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };

                _dbContext.MonitorAssignments.Add(assignment);
                existingAssignments.Add(assignment);
                result.InsertedCount++;
            }
            else
            {
                target.Enabled = source.Enabled;
                target.PingIntervalSeconds = source.PingIntervalSeconds;
                target.RetryIntervalSeconds = source.RetryIntervalSeconds;
                target.TimeoutMs = source.TimeoutMs;
                target.FailureThreshold = source.FailureThreshold;
                target.RecoveryThreshold = source.RecoveryThreshold;
                target.UpdatedAtUtc = DateTimeOffset.UtcNow;
                result.UpdatedCount++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Restored section {Section}. Inserted {InsertedCount}, updated {UpdatedCount}, skipped {SkippedCount}, errors {ErrorCount}.",
            result.Section,
            result.InsertedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);

        return result;
    }

    private async Task<RestoreSectionResult> RestoreIdentityAsync(ConfigurationBackupDocument backup, CancellationToken cancellationToken)
    {
        var result = new RestoreSectionResult { Section = ConfigurationBackupSections.Identity };
        var sourceIdentity = backup.Sections.Identity;
        if (sourceIdentity is null)
        {
            throw new InvalidOperationException("Identity section is missing in selected backup.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingRoles = await _dbContext.Roles.ToListAsync(cancellationToken);
        var existingUsers = await _dbContext.Users.ToListAsync(cancellationToken);
        var existingUserRoles = await _dbContext.UserRoles.ToListAsync(cancellationToken);

        var roleIdMapping = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sourceRole in sourceIdentity.Roles)
        {
            var normalizedRoleName = sourceRole.NormalizedName ?? sourceRole.Name?.ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedRoleName))
            {
                result.SkippedCount++;
                result.Warnings.Add("Skipped role with no normalized name.");
                continue;
            }

            var targetRole = existingRoles.FirstOrDefault(x => string.Equals(x.NormalizedName, normalizedRoleName, StringComparison.Ordinal));
            if (targetRole is null)
            {
                targetRole = new ApplicationRole
                {
                    Id = string.IsNullOrWhiteSpace(sourceRole.Id) ? Guid.NewGuid().ToString("N") : sourceRole.Id,
                    Name = sourceRole.Name ?? normalizedRoleName,
                    NormalizedName = normalizedRoleName
                };

                _dbContext.Roles.Add(targetRole);
                existingRoles.Add(targetRole);
                result.InsertedCount++;
            }
            else
            {
                targetRole.Name = sourceRole.Name ?? targetRole.Name;
                result.UpdatedCount++;
            }

            roleIdMapping[sourceRole.Id] = targetRole.Id;
        }

        var userIdMapping = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sourceUser in sourceIdentity.Users)
        {
            var normalizedUserName = sourceUser.NormalizedUserName;
            var normalizedEmail = sourceUser.NormalizedEmail;

            var targetUser = existingUsers.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(normalizedUserName) && string.Equals(x.NormalizedUserName, normalizedUserName, StringComparison.Ordinal))
                || (!string.IsNullOrWhiteSpace(normalizedEmail) && string.Equals(x.NormalizedEmail, normalizedEmail, StringComparison.Ordinal)));

            if (targetUser is null)
            {
                result.SkippedCount++;
                result.Warnings.Add($"Skipped creating identity user '{sourceUser.UserName ?? sourceUser.Email ?? sourceUser.Id}' because password hash is not part of configuration backups.");
                continue;
            }

            targetUser.UserName = sourceUser.UserName ?? targetUser.UserName;
            targetUser.NormalizedUserName = sourceUser.NormalizedUserName ?? targetUser.NormalizedUserName;
            targetUser.Email = sourceUser.Email ?? targetUser.Email;
            targetUser.NormalizedEmail = sourceUser.NormalizedEmail ?? targetUser.NormalizedEmail;
            targetUser.EmailConfirmed = sourceUser.EmailConfirmed;
            targetUser.LockoutEnabled = sourceUser.LockoutEnabled;
            targetUser.LockoutEnd = sourceUser.LockoutEnd;
            targetUser.AccessFailedCount = sourceUser.AccessFailedCount;
            result.UpdatedCount++;
            userIdMapping[sourceUser.Id] = targetUser.Id;
        }

        foreach (var sourceUserRole in sourceIdentity.UserRoles)
        {
            var hasUser = userIdMapping.TryGetValue(sourceUserRole.UserId, out var resolvedUserId);
            var hasRole = roleIdMapping.TryGetValue(sourceUserRole.RoleId, out var resolvedRoleId);
            if (!hasUser || !hasRole)
            {
                result.SkippedCount++;
                result.Warnings.Add($"Skipped user-role mapping for user '{sourceUserRole.UserId}' and role '{sourceUserRole.RoleId}'.");
                continue;
            }

            var exists = existingUserRoles.Any(x => string.Equals(x.UserId, resolvedUserId, StringComparison.Ordinal)
                && string.Equals(x.RoleId, resolvedRoleId, StringComparison.Ordinal));
            if (exists)
            {
                continue;
            }

            var userRole = new Microsoft.AspNetCore.Identity.IdentityUserRole<string>
            {
                UserId = resolvedUserId!,
                RoleId = resolvedRoleId!
            };

            _dbContext.UserRoles.Add(userRole);
            existingUserRoles.Add(userRole);
            result.InsertedCount++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Restored section {Section}. Inserted {InsertedCount}, updated {UpdatedCount}, skipped {SkippedCount}, errors {ErrorCount}.",
            result.Section,
            result.InsertedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);

        return result;
    }

    private static string? ResolveMappedOrExistingId(
        string sourceId,
        IDictionary<string, string> mapping,
        IReadOnlySet<string> existingIds)
    {
        if (mapping.TryGetValue(sourceId, out var mappedId))
        {
            return mappedId;
        }

        return existingIds.Contains(sourceId) ? sourceId : null;
    }

    private static bool TryParseCheckType(string rawCheckType, out CheckType checkType)
    {
        if (string.Equals(rawCheckType, "icmp", StringComparison.OrdinalIgnoreCase))
        {
            checkType = CheckType.Icmp;
            return true;
        }

        checkType = default;
        return false;
    }
}
