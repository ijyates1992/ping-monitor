using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.NetworkDiagrams;
using PingMonitor.Web.Support;
using EndpointModel = PingMonitor.Web.Models.Endpoint;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationRestoreService
{
    Task<RestoreConfigurationResponse> RestoreAsync(RestoreConfigurationRequest request, CancellationToken cancellationToken);
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

    public async Task<RestoreConfigurationResponse> RestoreAsync(RestoreConfigurationRequest request, CancellationToken cancellationToken)
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

        var restoreMode = NormalizeRestoreMode(request.RestoreMode);
        ValidateConfirmationForMode(restoreMode, request.ConfirmationText);
        ValidateReplaceSelectionRules(restoreMode, selectedSections);

        var backup = await _documentLoader.LoadValidatedDocumentAsync(request.FileId, cancellationToken);
        EnsureSelectedSectionsExist(selectedSections, backup);

        _logger.LogInformation(
            "Starting configuration restore in {RestoreMode} mode for {FileId}. Sections: {Sections}.",
            restoreMode,
            LogValueSanitizer.ForLog(request.FileId),
            string.Join(",", selectedSections));

        var sectionResults = new List<RestoreSectionResult>();
        var endpointIdMapping = new Dictionary<string, string>(StringComparer.Ordinal);
        var agentIdMapping = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var deletedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.Equals(restoreMode, ConfigurationRestoreModes.Replace, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Destructive replace restore confirmed for {FileId}. Sections: {Sections}.",
                LogValueSanitizer.ForLog(request.FileId),
                string.Join(",", selectedSections));

            await ApplyReplaceDeletesAsync(selectedSections, deletedCounts, cancellationToken);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Agents, StringComparer.Ordinal))
        {
            var section = await RestoreAgentsAsync(backup, agentIdMapping, cancellationToken);
            section.DeletedCount = GetDeletedCount(deletedCounts, section.Section);
            sectionResults.Add(section);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Endpoints, StringComparer.Ordinal))
        {
            var section = await RestoreEndpointsAsync(backup, endpointIdMapping, cancellationToken);
            section.DeletedCount = GetDeletedCount(deletedCounts, section.Section);
            sectionResults.Add(section);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Groups, StringComparer.Ordinal))
        {
            var section = await RestoreGroupsAsync(backup, endpointIdMapping, cancellationToken);
            section.DeletedCount = GetDeletedCount(deletedCounts, section.Section);
            sectionResults.Add(section);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Dependencies, StringComparer.Ordinal))
        {
            var section = await RestoreDependenciesAsync(backup, endpointIdMapping, cancellationToken);
            section.DeletedCount = GetDeletedCount(deletedCounts, section.Section);
            sectionResults.Add(section);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Assignments, StringComparer.Ordinal))
        {
            var section = await RestoreAssignmentsAsync(backup, endpointIdMapping, agentIdMapping, cancellationToken);
            section.DeletedCount = GetDeletedCount(deletedCounts, section.Section);
            sectionResults.Add(section);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.SecuritySettings, StringComparer.Ordinal))
        {
            var section = await RestoreSecuritySettingsAsync(backup, cancellationToken);
            section.DeletedCount = GetDeletedCount(deletedCounts, section.Section);
            sectionResults.Add(section);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.NotificationSettings, StringComparer.Ordinal))
        {
            var section = await RestoreNotificationSettingsAsync(backup, cancellationToken);
            section.DeletedCount = GetDeletedCount(deletedCounts, section.Section);
            sectionResults.Add(section);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.UserNotificationSettings, StringComparer.Ordinal))
        {
            var section = await RestoreUserNotificationSettingsAsync(backup, cancellationToken);
            section.DeletedCount = GetDeletedCount(deletedCounts, section.Section);
            sectionResults.Add(section);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.AiAssistantSettings, StringComparer.Ordinal))
        {
            var section = await RestoreAiAssistantSettingsAsync(backup, cancellationToken);
            section.DeletedCount = GetDeletedCount(deletedCounts, section.Section);
            sectionResults.Add(section);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Identity, StringComparer.Ordinal))
        {
            var section = await RestoreIdentityAsync(backup, cancellationToken);
            section.DeletedCount = GetDeletedCount(deletedCounts, section.Section);
            sectionResults.Add(section);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.NetworkDiagrams, StringComparer.Ordinal))
        {
            var section = await RestoreNetworkDiagramsAsync(backup, endpointIdMapping, cancellationToken);
            section.DeletedCount = GetDeletedCount(deletedCounts, section.Section);
            sectionResults.Add(section);
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Completed configuration restore in {RestoreMode} mode for {FileId}.",
            restoreMode,
            request.FileId);

        return new RestoreConfigurationResponse
        {
            FileId = request.FileId,
            BackupName = backup.BackupName,
            RestoreMode = restoreMode,
            SelectedSections = selectedSections,
            SectionResults = sectionResults
        };
    }

    private static string NormalizeRestoreMode(string? restoreMode)
    {
        if (string.IsNullOrWhiteSpace(restoreMode))
        {
            return ConfigurationRestoreModes.Merge;
        }

        var normalized = restoreMode.Trim().ToLowerInvariant();
        if (!ConfigurationRestoreModes.All.Contains(normalized, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported restore mode '{restoreMode}'.");
        }

        return normalized;
    }

    private void ValidateConfirmationForMode(string restoreMode, string? confirmationText)
    {
        if (!string.Equals(restoreMode, ConfigurationRestoreModes.Replace, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(confirmationText, ConfigurationRestoreModes.ReplaceConfirmationText, StringComparison.Ordinal))
        {
            _logger.LogWarning("Replace restore confirmation failed due to invalid confirmation text.");
            throw new InvalidOperationException($"Replace restore requires typed confirmation text '{ConfigurationRestoreModes.ReplaceConfirmationText}'.");
        }

        _logger.LogInformation("Replace restore confirmation validated.");
    }

    private static void ValidateReplaceSelectionRules(string restoreMode, IReadOnlyCollection<string> selectedSections)
    {
        if (!string.Equals(restoreMode, ConfigurationRestoreModes.Replace, StringComparison.Ordinal))
        {
            return;
        }

        var includesAgents = selectedSections.Contains(ConfigurationBackupSections.Agents, StringComparer.Ordinal);
        var includesEndpoints = selectedSections.Contains(ConfigurationBackupSections.Endpoints, StringComparer.Ordinal);
        var includesDependencies = selectedSections.Contains(ConfigurationBackupSections.Dependencies, StringComparer.Ordinal);
        var includesAssignments = selectedSections.Contains(ConfigurationBackupSections.Assignments, StringComparer.Ordinal);

        if ((includesAgents || includesEndpoints || includesDependencies) && !includesAssignments)
        {
            throw new InvalidOperationException("Replace mode for agents/endpoints/dependencies requires selecting assignments so relationship rows are replaced deterministically.");
        }
    }

    private async Task ApplyReplaceDeletesAsync(
        IReadOnlyCollection<string> selectedSections,
        IDictionary<string, int> deletedCounts,
        CancellationToken cancellationToken)
    {
        if (selectedSections.Contains(ConfigurationBackupSections.Identity, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Replace restore for identity section is not supported in this phase because configuration backups do not contain password hashes.");
        }

        // Explicit destructive ordering:
        // 1) MonitorAssignments are deleted before Agents/Endpoints.
        // 2) EndpointDependencies and endpoint-scoped access/membership rows are deleted before Endpoints.
        // This keeps referential dependencies deterministic in replace mode.
        if (selectedSections.Contains(ConfigurationBackupSections.Assignments, StringComparer.Ordinal))
        {
            var deletedAssignments = await _dbContext.MonitorAssignments.ExecuteDeleteAsync(cancellationToken);
            deletedCounts[ConfigurationBackupSections.Assignments] = deletedAssignments;
            _logger.LogInformation("Replace delete completed for section {Section}. Deleted {DeletedCount} records.", ConfigurationBackupSections.Assignments, deletedAssignments);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Endpoints, StringComparer.Ordinal))
        {
            var deletedEndpointMemberships = await _dbContext.EndpointGroupMemberships.ExecuteDeleteAsync(cancellationToken);
            var deletedEndpointAccess = await _dbContext.UserEndpointAccesses.ExecuteDeleteAsync(cancellationToken);
            var deletedEndpoints = await _dbContext.Endpoints.ExecuteDeleteAsync(cancellationToken);

            deletedCounts[ConfigurationBackupSections.Endpoints] = deletedEndpoints + deletedEndpointMemberships + deletedEndpointAccess;
            _logger.LogInformation(
                "Replace delete completed for section {Section}. Deleted endpoints {EndpointCount}, memberships {MembershipCount}, endpoint access {AccessCount}.",
                ConfigurationBackupSections.Endpoints,
                deletedEndpoints,
                deletedEndpointMemberships,
                deletedEndpointAccess);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Dependencies, StringComparer.Ordinal))
        {
            var deletedDependencies = await _dbContext.EndpointDependencies.ExecuteDeleteAsync(cancellationToken);
            deletedCounts[ConfigurationBackupSections.Dependencies] = deletedDependencies;
            _logger.LogInformation("Replace delete completed for section {Section}. Deleted {DeletedCount} records.", ConfigurationBackupSections.Dependencies, deletedDependencies);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Groups, StringComparer.Ordinal))
        {
            var deletedMemberships = await _dbContext.EndpointGroupMemberships.ExecuteDeleteAsync(cancellationToken);
            var deletedGroups = await _dbContext.Groups.ExecuteDeleteAsync(cancellationToken);
            deletedCounts[ConfigurationBackupSections.Groups] = deletedMemberships + deletedGroups;
            _logger.LogInformation(
                "Replace delete completed for section {Section}. Deleted groups {GroupCount}, memberships {MembershipCount}.",
                ConfigurationBackupSections.Groups,
                deletedGroups,
                deletedMemberships);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.UserNotificationSettings, StringComparer.Ordinal))
        {
            var deleted = await _dbContext.UserNotificationSettings.ExecuteDeleteAsync(cancellationToken);
            deletedCounts[ConfigurationBackupSections.UserNotificationSettings] = deleted;
            _logger.LogInformation("Replace delete completed for section {Section}. Deleted {DeletedCount} records.", ConfigurationBackupSections.UserNotificationSettings, deleted);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.NetworkDiagrams, StringComparer.Ordinal))
        {
            var deletedVlans = await _dbContext.NetworkDiagramLinkVlans.ExecuteDeleteAsync(cancellationToken);
            var deletedLinks = await _dbContext.NetworkDiagramLinks.ExecuteDeleteAsync(cancellationToken);
            var deletedNodes = await _dbContext.NetworkDiagramNodes.ExecuteDeleteAsync(cancellationToken);
            var deletedAreas = await _dbContext.NetworkDiagramAreas.ExecuteDeleteAsync(cancellationToken);
            var deletedDiagrams = await _dbContext.NetworkDiagrams.ExecuteDeleteAsync(cancellationToken);
            deletedCounts[ConfigurationBackupSections.NetworkDiagrams] = deletedVlans + deletedLinks + deletedNodes + deletedAreas + deletedDiagrams;
            _logger.LogInformation(
                "Replace delete completed for section {Section}. Deleted diagrams {DiagramCount}, areas {AreaCount}, nodes {NodeCount}, links {LinkCount}, VLAN metadata {VlanCount}.",
                ConfigurationBackupSections.NetworkDiagrams,
                deletedDiagrams,
                deletedAreas,
                deletedNodes,
                deletedLinks,
                deletedVlans);
        }

        if (selectedSections.Contains(ConfigurationBackupSections.Agents, StringComparer.Ordinal))
        {
            var deletedAgents = await _dbContext.Agents.ExecuteDeleteAsync(cancellationToken);
            deletedCounts[ConfigurationBackupSections.Agents] = deletedAgents;
            _logger.LogInformation("Replace delete completed for section {Section}. Deleted {DeletedCount} records.", ConfigurationBackupSections.Agents, deletedAgents);
        }
    }

    private static int GetDeletedCount(IReadOnlyDictionary<string, int> deletedCounts, string section)
    {
        return deletedCounts.TryGetValue(section, out var deletedCount) ? deletedCount : 0;
    }

    private static void EnsureSelectedSectionsExist(IReadOnlyCollection<string> selectedSections, ConfigurationBackupDocument backup)
    {
        foreach (var section in selectedSections)
        {
            var present = section switch
            {
                ConfigurationBackupSections.Agents => backup.Sections.Agents is not null,
                ConfigurationBackupSections.Endpoints => backup.Sections.Endpoints is not null,
                ConfigurationBackupSections.Groups => backup.Sections.Groups is not null,
                ConfigurationBackupSections.Dependencies => backup.Sections.Dependencies is not null
                    || backup.Sections.Endpoints?.Any(x => x.DependsOnEndpointIds is { Count: > 0 }) == true,
                ConfigurationBackupSections.Assignments => backup.Sections.Assignments is not null,
                ConfigurationBackupSections.SecuritySettings => backup.Sections.SecuritySettings is not null,
                ConfigurationBackupSections.NotificationSettings => backup.Sections.NotificationSettings is not null,
                ConfigurationBackupSections.UserNotificationSettings => backup.Sections.UserNotificationSettings is not null,
                ConfigurationBackupSections.AiAssistantSettings => backup.Sections.AiAssistantSettings is not null,
                ConfigurationBackupSections.Identity => backup.Sections.Identity is not null,
                ConfigurationBackupSections.NetworkDiagrams => backup.Sections.NetworkDiagrams is not null,
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

        var existingEndpoints = await _dbContext.Endpoints.ToListAsync(cancellationToken);

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

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Restored section {Section}. Inserted {InsertedCount}, updated {UpdatedCount}, skipped {SkippedCount}, errors {ErrorCount}.",
            result.Section,
            result.InsertedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);

        return result;
    }

    private async Task<RestoreSectionResult> RestoreGroupsAsync(
        ConfigurationBackupDocument backup,
        IDictionary<string, string> endpointIdMapping,
        CancellationToken cancellationToken)
    {
        var result = new RestoreSectionResult { Section = ConfigurationBackupSections.Groups };
        var sourceGroups = backup.Sections.Groups;
        if (sourceGroups is null)
        {
            throw new InvalidOperationException("Groups section is missing in selected backup.");
        }

        var existingGroups = await _dbContext.Groups.ToListAsync(cancellationToken);
        var existingMemberships = await _dbContext.EndpointGroupMemberships.ToListAsync(cancellationToken);
        var existingEndpointIds = await _dbContext.Endpoints.AsNoTracking().Select(x => x.EndpointId).ToListAsync(cancellationToken);
        var existingEndpointIdSet = existingEndpointIds.ToHashSet(StringComparer.Ordinal);

        var groupIdMapping = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in sourceGroups.Groups)
        {
            var target = existingGroups.FirstOrDefault(x => string.Equals(x.Name, source.Name, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                target = new Group
                {
                    GroupId = string.IsNullOrWhiteSpace(source.GroupId) ? Guid.NewGuid().ToString() : source.GroupId,
                    Name = source.Name,
                    Description = source.Description,
                    CreatedAtUtc = source.CreatedAtUtc == default ? DateTimeOffset.UtcNow : source.CreatedAtUtc
                };

                _dbContext.Groups.Add(target);
                existingGroups.Add(target);
                result.InsertedCount++;
            }
            else
            {
                target.Description = source.Description;
                result.UpdatedCount++;
            }

            groupIdMapping[source.GroupId] = target.GroupId;
        }

        foreach (var sourceMembership in sourceGroups.EndpointMemberships)
        {
            if (!groupIdMapping.TryGetValue(sourceMembership.GroupId, out var resolvedGroupId))
            {
                result.SkippedCount++;
                result.Warnings.Add($"Skipped group membership for group id '{sourceMembership.GroupId}' because the group was not restored.");
                continue;
            }

            var resolvedEndpointId = ResolveMappedOrExistingId(sourceMembership.EndpointId, endpointIdMapping, existingEndpointIdSet);
            if (resolvedEndpointId is null)
            {
                result.SkippedCount++;
                result.Warnings.Add($"Skipped group membership for endpoint id '{sourceMembership.EndpointId}' because the endpoint was not available.");
                continue;
            }

            var exists = existingMemberships.Any(x => string.Equals(x.GroupId, resolvedGroupId, StringComparison.Ordinal)
                && string.Equals(x.EndpointId, resolvedEndpointId, StringComparison.Ordinal));
            if (exists)
            {
                continue;
            }

            var membership = new EndpointGroupMembership
            {
                EndpointGroupMembershipId = Guid.NewGuid().ToString(),
                GroupId = resolvedGroupId,
                EndpointId = resolvedEndpointId,
                CreatedAtUtc = sourceMembership.CreatedAtUtc == default ? DateTimeOffset.UtcNow : sourceMembership.CreatedAtUtc
            };

            _dbContext.EndpointGroupMemberships.Add(membership);
            existingMemberships.Add(membership);
            result.InsertedCount++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Restored section {Section}. Inserted {InsertedCount}, updated {UpdatedCount}, skipped {SkippedCount}, errors {ErrorCount}.",
            result.Section,
            result.InsertedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);

        return result;
    }

    private async Task<RestoreSectionResult> RestoreDependenciesAsync(
        ConfigurationBackupDocument backup,
        IDictionary<string, string> endpointIdMapping,
        CancellationToken cancellationToken)
    {
        var result = new RestoreSectionResult { Section = ConfigurationBackupSections.Dependencies };
        var sourceDependencies = backup.Sections.Dependencies?.ToList()
            ?? BuildLegacyDependencyRecords(backup.Sections.Endpoints);
        if (sourceDependencies.Count == 0)
        {
            return result;
        }

        var existingDependencies = await _dbContext.EndpointDependencies.ToListAsync(cancellationToken);
        var existingEndpointIds = await _dbContext.Endpoints.AsNoTracking().Select(x => x.EndpointId).ToListAsync(cancellationToken);
        var existingEndpointIdSet = existingEndpointIds.ToHashSet(StringComparer.Ordinal);

        foreach (var source in sourceDependencies)
        {
            var resolvedEndpointId = ResolveMappedOrExistingId(source.EndpointId, endpointIdMapping, existingEndpointIdSet);
            var resolvedDependsOnId = ResolveMappedOrExistingId(source.DependsOnEndpointId, endpointIdMapping, existingEndpointIdSet);
            if (resolvedEndpointId is null || resolvedDependsOnId is null)
            {
                result.SkippedCount++;
                result.Warnings.Add($"Skipped dependency '{source.EndpointId}' -> '{source.DependsOnEndpointId}' because one or both endpoints were not available.");
                continue;
            }

            if (string.Equals(resolvedEndpointId, resolvedDependsOnId, StringComparison.Ordinal))
            {
                result.SkippedCount++;
                result.Warnings.Add($"Skipped self-dependency '{resolvedEndpointId}'.");
                continue;
            }

            var exists = existingDependencies.Any(x => string.Equals(x.EndpointId, resolvedEndpointId, StringComparison.Ordinal)
                && string.Equals(x.DependsOnEndpointId, resolvedDependsOnId, StringComparison.Ordinal));
            if (exists)
            {
                continue;
            }

            var dependency = new EndpointDependency
            {
                EndpointDependencyId = Guid.NewGuid().ToString(),
                EndpointId = resolvedEndpointId,
                DependsOnEndpointId = resolvedDependsOnId,
                CreatedAtUtc = source.CreatedAtUtc == default ? DateTimeOffset.UtcNow : source.CreatedAtUtc
            };

            _dbContext.EndpointDependencies.Add(dependency);
            existingDependencies.Add(dependency);
            result.InsertedCount++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

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

        _logger.LogInformation(
            "Restored section {Section}. Inserted {InsertedCount}, updated {UpdatedCount}, skipped {SkippedCount}, errors {ErrorCount}.",
            result.Section,
            result.InsertedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);

        return result;
    }

    private async Task<RestoreSectionResult> RestoreSecuritySettingsAsync(ConfigurationBackupDocument backup, CancellationToken cancellationToken)
    {
        var result = new RestoreSectionResult { Section = ConfigurationBackupSections.SecuritySettings };
        var source = backup.Sections.SecuritySettings;
        if (source is null)
        {
            throw new InvalidOperationException("Security settings section is missing in selected backup.");
        }

        var target = await _dbContext.SecuritySettings
            .SingleOrDefaultAsync(x => x.SecuritySettingsId == SecuritySettings.SingletonId, cancellationToken);
        if (target is null)
        {
            target = new SecuritySettings { SecuritySettingsId = SecuritySettings.SingletonId };
            _dbContext.SecuritySettings.Add(target);
            result.InsertedCount++;
        }
        else
        {
            result.UpdatedCount++;
        }

        target.AgentFailedAttemptsBeforeTemporaryIpBlock = source.AgentFailedAttemptsBeforeTemporaryIpBlock;
        target.AgentTemporaryIpBlockDurationMinutes = source.AgentTemporaryIpBlockDurationMinutes;
        target.AgentFailedAttemptsBeforePermanentIpBlock = source.AgentFailedAttemptsBeforePermanentIpBlock;
        target.UserFailedAttemptsBeforeTemporaryIpBlock = source.UserFailedAttemptsBeforeTemporaryIpBlock;
        target.UserTemporaryIpBlockDurationMinutes = source.UserTemporaryIpBlockDurationMinutes;
        target.UserFailedAttemptsBeforePermanentIpBlock = source.UserFailedAttemptsBeforePermanentIpBlock;
        target.UserFailedAttemptsBeforeTemporaryAccountLockout = source.UserFailedAttemptsBeforeTemporaryAccountLockout;
        target.UserTemporaryAccountLockoutDurationMinutes = source.UserTemporaryAccountLockoutDurationMinutes;
        target.SecurityLogRetentionEnabled = source.SecurityLogRetentionEnabled;
        target.SecurityLogRetentionDays = source.SecurityLogRetentionDays;
        target.SecurityLogAutoPruneEnabled = source.SecurityLogAutoPruneEnabled;
        target.UpdatedAtUtc = source.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : source.UpdatedAtUtc;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Restored section {Section}. Inserted {InsertedCount}, updated {UpdatedCount}, skipped {SkippedCount}, errors {ErrorCount}.",
            result.Section,
            result.InsertedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);
        return result;
    }

    private async Task<RestoreSectionResult> RestoreNotificationSettingsAsync(ConfigurationBackupDocument backup, CancellationToken cancellationToken)
    {
        var result = new RestoreSectionResult { Section = ConfigurationBackupSections.NotificationSettings };
        var source = backup.Sections.NotificationSettings;
        if (source is null)
        {
            throw new InvalidOperationException("Notification settings section is missing in selected backup.");
        }

        var target = await _dbContext.NotificationSettings
            .SingleOrDefaultAsync(x => x.NotificationSettingsId == NotificationSettings.SingletonId, cancellationToken);
        if (target is null)
        {
            target = new NotificationSettings { NotificationSettingsId = NotificationSettings.SingletonId };
            _dbContext.NotificationSettings.Add(target);
            result.InsertedCount++;
        }
        else
        {
            result.UpdatedCount++;
        }

        if (!TryParseTelegramInboundMode(source.TelegramInboundMode, out var telegramInboundMode))
        {
            result.SkippedCount++;
            result.Warnings.Add($"Notification settings contained unsupported telegram inbound mode '{source.TelegramInboundMode}'. Defaulted to polling.");
            telegramInboundMode = TelegramInboundMode.Polling;
        }

        target.BrowserNotificationsEnabled = source.BrowserNotificationsEnabled;
        target.BrowserNotifyEndpointDown = source.BrowserNotifyEndpointDown;
        target.BrowserNotifyEndpointRecovered = source.BrowserNotifyEndpointRecovered;
        target.BrowserNotifyAgentOffline = source.BrowserNotifyAgentOffline;
        target.BrowserNotifyAgentOnline = source.BrowserNotifyAgentOnline;
        target.BrowserNotificationsPermissionState = source.BrowserNotificationsPermissionState;
        target.TelegramEnabled = source.TelegramEnabled;
        target.TelegramBotTokenProtected = source.TelegramBotTokenProtected;
        target.TelegramInboundMode = telegramInboundMode;
        target.TelegramPollIntervalSeconds = source.TelegramPollIntervalSeconds;
        target.TelegramLastProcessedUpdateId = source.TelegramLastProcessedUpdateId;
        target.TelegramWebhookUrl = source.TelegramWebhookUrl;
        target.TelegramWebhookSecretToken = source.TelegramWebhookSecretToken;
        target.QuietHoursEnabled = source.QuietHoursEnabled;
        target.QuietHoursStartLocalTime = source.QuietHoursStartLocalTime;
        target.QuietHoursEndLocalTime = source.QuietHoursEndLocalTime;
        target.QuietHoursTimeZoneId = source.QuietHoursTimeZoneId;
        target.QuietHoursSuppressBrowserNotifications = source.QuietHoursSuppressBrowserNotifications;
        target.QuietHoursSuppressSmtpNotifications = source.QuietHoursSuppressSmtpNotifications;
        target.SmtpNotificationsEnabled = source.SmtpNotificationsEnabled;
        target.SmtpHost = source.SmtpHost;
        target.SmtpPort = source.SmtpPort;
        target.SmtpUseTls = source.SmtpUseTls;
        target.SmtpUsername = source.SmtpUsername;
        target.SmtpPasswordProtected = source.SmtpPasswordProtected;
        target.SmtpFromAddress = source.SmtpFromAddress;
        target.SmtpFromDisplayName = source.SmtpFromDisplayName;
        target.SmtpRecipientAddresses = source.SmtpRecipientAddresses;
        target.SmtpNotifyEndpointDown = source.SmtpNotifyEndpointDown;
        target.SmtpNotifyEndpointRecovered = source.SmtpNotifyEndpointRecovered;
        target.SmtpNotifyAgentOffline = source.SmtpNotifyAgentOffline;
        target.SmtpNotifyAgentOnline = source.SmtpNotifyAgentOnline;
        target.UpdatedAtUtc = source.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : source.UpdatedAtUtc;
        target.UpdatedByUserId = source.UpdatedByUserId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Restored section {Section}. Inserted {InsertedCount}, updated {UpdatedCount}, skipped {SkippedCount}, errors {ErrorCount}.",
            result.Section,
            result.InsertedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);
        return result;
    }


    private async Task<RestoreSectionResult> RestoreAiAssistantSettingsAsync(ConfigurationBackupDocument backup, CancellationToken cancellationToken)
    {
        var result = new RestoreSectionResult { Section = ConfigurationBackupSections.AiAssistantSettings };
        var source = backup.Sections.AiAssistantSettings;
        if (source is null)
        {
            result.SkippedCount++;
            result.Warnings.Add("Backup does not include AI assistant settings.");
            return result;
        }

        var target = await _dbContext.AiAssistantSettings
            .SingleOrDefaultAsync(x => x.AiAssistantSettingsId == AiAssistantSettings.SingletonId, cancellationToken);
        if (target is null)
        {
            target = new AiAssistantSettings { AiAssistantSettingsId = AiAssistantSettings.SingletonId };
            _dbContext.AiAssistantSettings.Add(target);
            result.InsertedCount++;
        }
        else
        {
            result.UpdatedCount++;
        }

        target.AssistantEnabled = source.AssistantEnabled;
        target.WebChatEnabled = source.WebChatEnabled;
        target.TelegramChatEnabled = source.TelegramChatEnabled;
        target.MemoryEnabled = source.MemoryEnabled;
        target.DebugLoggingEnabled = source.DebugLoggingEnabled;
        target.ProviderDisplayName = string.IsNullOrWhiteSpace(source.ProviderDisplayName) ? "Local Ollama" : source.ProviderDisplayName;
        target.ProviderType = source.ProviderType == AiAssistantSettings.OpenAICompatibleProviderType ? source.ProviderType : AiAssistantSettings.OpenAICompatibleProviderType;
        target.BaseUrl = source.BaseUrl ?? string.Empty;
        target.ModelName = source.ModelName ?? string.Empty;
        target.ApiKeyProtected = source.ApiKeyProtected;
        target.RequestTimeoutSeconds = source.RequestTimeoutSeconds <= 0 ? 60 : source.RequestTimeoutSeconds;
        target.MaxOutputTokens = source.MaxOutputTokens <= 0 ? 2048 : source.MaxOutputTokens;
        target.Temperature = source.Temperature;
        target.ToolCallingEnabled = source.ToolCallingEnabled;
        target.GlobalSystemPrompt = source.GlobalSystemPrompt ?? string.Empty;
        target.UpdatedAtUtc = source.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : source.UpdatedAtUtc;
        target.UpdatedByUserId = source.UpdatedByUserId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<RestoreSectionResult> RestoreUserNotificationSettingsAsync(ConfigurationBackupDocument backup, CancellationToken cancellationToken)
    {
        var result = new RestoreSectionResult { Section = ConfigurationBackupSections.UserNotificationSettings };
        var sourceRows = backup.Sections.UserNotificationSettings ?? [];
        var existingRows = await _dbContext.UserNotificationSettings.ToListAsync(cancellationToken);

        foreach (var source in sourceRows)
        {
            var target = existingRows.FirstOrDefault(x => string.Equals(x.UserId, source.UserId, StringComparison.Ordinal));
            if (target is null)
            {
                target = new UserNotificationSettings
                {
                    UserId = source.UserId
                };

                _dbContext.UserNotificationSettings.Add(target);
                existingRows.Add(target);
                result.InsertedCount++;
            }
            else
            {
                result.UpdatedCount++;
            }

            target.BrowserNotificationsEnabled = source.BrowserNotificationsEnabled;
            target.BrowserNotifyEndpointDown = source.BrowserNotifyEndpointDown;
            target.BrowserNotifyEndpointRecovered = source.BrowserNotifyEndpointRecovered;
            target.BrowserNotifyAgentOffline = source.BrowserNotifyAgentOffline;
            target.BrowserNotifyAgentOnline = source.BrowserNotifyAgentOnline;
            target.BrowserNotificationsPermissionState = source.BrowserNotificationsPermissionState;
            target.SmtpNotificationsEnabled = source.SmtpNotificationsEnabled;
            target.SmtpNotifyEndpointDown = source.SmtpNotifyEndpointDown;
            target.SmtpNotifyEndpointRecovered = source.SmtpNotifyEndpointRecovered;
            target.SmtpNotifyAgentOffline = source.SmtpNotifyAgentOffline;
            target.SmtpNotifyAgentOnline = source.SmtpNotifyAgentOnline;
            target.TelegramNotificationsEnabled = source.TelegramNotificationsEnabled;
            target.TelegramNotifyEndpointDown = source.TelegramNotifyEndpointDown;
            target.TelegramNotifyEndpointRecovered = source.TelegramNotifyEndpointRecovered;
            target.TelegramNotifyAgentOffline = source.TelegramNotifyAgentOffline;
            target.TelegramNotifyAgentOnline = source.TelegramNotifyAgentOnline;
            target.QuietHoursSuppressTelegramNotifications = source.QuietHoursSuppressTelegramNotifications;
            target.QuietHoursEnabled = source.QuietHoursEnabled;
            target.QuietHoursStartLocalTime = source.QuietHoursStartLocalTime;
            target.QuietHoursEndLocalTime = source.QuietHoursEndLocalTime;
            target.QuietHoursTimeZoneId = source.QuietHoursTimeZoneId;
            target.QuietHoursSuppressBrowserNotifications = source.QuietHoursSuppressBrowserNotifications;
            target.QuietHoursSuppressSmtpNotifications = source.QuietHoursSuppressSmtpNotifications;
            target.UpdatedAtUtc = source.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : source.UpdatedAtUtc;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
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

        _logger.LogInformation(
            "Restored section {Section}. Inserted {InsertedCount}, updated {UpdatedCount}, skipped {SkippedCount}, errors {ErrorCount}.",
            result.Section,
            result.InsertedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);

        return result;
    }


    private async Task<RestoreSectionResult> RestoreNetworkDiagramsAsync(
        ConfigurationBackupDocument backup,
        IDictionary<string, string> endpointIdMapping,
        CancellationToken cancellationToken)
    {
        var result = new RestoreSectionResult { Section = ConfigurationBackupSections.NetworkDiagrams };
        var sourceSection = backup.Sections.NetworkDiagrams;
        if (sourceSection is null)
        {
            throw new InvalidOperationException("Network diagrams section is missing in selected backup.");
        }

        var existingEndpointIds = await _dbContext.Endpoints.AsNoTracking().Select(x => x.EndpointId).ToListAsync(cancellationToken);
        var existingEndpointIdSet = existingEndpointIds.ToHashSet(StringComparer.Ordinal);
        var existingDiagrams = await _dbContext.NetworkDiagrams
            .Include(x => x.Areas)
            .Include(x => x.Nodes)
            .Include(x => x.Links)
                .ThenInclude(x => x.Vlans)
            .ToListAsync(cancellationToken);

        foreach (var sourceDiagram in sourceSection.Diagrams)
        {
            if (!ValidateBackupDiagramShell(sourceDiagram, result))
            {
                result.SkippedCount++;
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var target = existingDiagrams.FirstOrDefault(x => string.Equals(x.DiagramId, sourceDiagram.DiagramId, StringComparison.Ordinal))
                ?? existingDiagrams.FirstOrDefault(x => string.Equals(x.Name, sourceDiagram.Name, StringComparison.OrdinalIgnoreCase));
            var isInsert = target is null;
            if (target is null)
            {
                target = new NetworkDiagram
                {
                    DiagramId = string.IsNullOrWhiteSpace(sourceDiagram.DiagramId) ? Guid.NewGuid().ToString("N") : sourceDiagram.DiagramId,
                    CreatedAtUtc = sourceDiagram.CreatedAtUtc == default ? now : sourceDiagram.CreatedAtUtc
                };
                _dbContext.NetworkDiagrams.Add(target);
                existingDiagrams.Add(target);
                result.InsertedCount++;
            }
            else
            {
                result.UpdatedCount++;
            }

            target.Name = TrimOrDefault(sourceDiagram.Name, 255, "Restored diagram");
            target.Description = TrimOptional(sourceDiagram.Description, 2048);
            target.CanvasWidth = ClampFinite(sourceDiagram.CanvasWidth <= 0 ? 4000 : sourceDiagram.CanvasWidth, 1000, 20000);
            target.CanvasHeight = ClampFinite(sourceDiagram.CanvasHeight <= 0 ? 2828 : sourceDiagram.CanvasHeight, 1000, 20000);
            target.ViewportPanX = ClampFinite(sourceDiagram.ViewportPanX, -100000, 100000);
            target.ViewportPanY = ClampFinite(sourceDiagram.ViewportPanY, -100000, 100000);
            target.ViewportZoom = ClampFinite(sourceDiagram.ViewportZoom <= 0 ? 1 : sourceDiagram.ViewportZoom, 0.1, 5);
            target.UpdatedAtUtc = sourceDiagram.UpdatedAtUtc == default ? now : sourceDiagram.UpdatedAtUtc;
            target.CreatedByUserId = TrimOptional(sourceDiagram.CreatedByUserId, 255);
            target.UpdatedByUserId = TrimOptional(sourceDiagram.UpdatedByUserId, 255);

            _dbContext.NetworkDiagramLinkVlans.RemoveRange(target.Links.SelectMany(x => x.Vlans));
            _dbContext.NetworkDiagramLinks.RemoveRange(target.Links);
            _dbContext.NetworkDiagramNodes.RemoveRange(target.Nodes);
            _dbContext.NetworkDiagramAreas.RemoveRange(target.Areas);
            target.Links.Clear();
            target.Nodes.Clear();
            target.Areas.Clear();
            await _dbContext.SaveChangesAsync(cancellationToken);


            foreach (var sourceArea in sourceDiagram.Areas.OrderBy(x => x.SortOrder).ThenBy(x => x.AreaId))
            {
                if (!TryBuildNetworkDiagramArea(sourceDiagram, sourceArea, target.DiagramId, result, out var area))
                {
                    result.SkippedCount++;
                    continue;
                }

                target.Areas.Add(area);
                if (!isInsert)
                {
                    result.UpdatedCount++;
                }
                else
                {
                    result.InsertedCount++;
                }
            }

            var restoredNodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sourceNode in sourceDiagram.Nodes)
            {
                if (!TryBuildNetworkDiagramNode(sourceDiagram, sourceNode, target.DiagramId, endpointIdMapping, existingEndpointIdSet, result, out var node))
                {
                    result.SkippedCount++;
                    continue;
                }

                target.Nodes.Add(node);
                restoredNodeIds.Add(node.NodeId);
                if (!isInsert)
                {
                    result.UpdatedCount++;
                }
                else
                {
                    result.InsertedCount++;
                }
            }

            var restoredLinkIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sourceLink in sourceDiagram.Links)
            {
                if (!TryBuildNetworkDiagramLink(sourceDiagram, sourceLink, target.DiagramId, restoredNodeIds, result, out var link))
                {
                    result.SkippedCount++;
                    continue;
                }

                foreach (var sourceVlan in sourceLink.Vlans.OrderBy(x => x.SortOrder).ThenBy(x => x.VlanId))
                {
                    if (!TryBuildNetworkDiagramLinkVlan(sourceDiagram, sourceLink, sourceVlan, target.DiagramId, link.LinkId, result, out var vlan))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    link.Vlans.Add(vlan);
                    if (!isInsert)
                    {
                        result.UpdatedCount++;
                    }
                    else
                    {
                        result.InsertedCount++;
                    }
                }

                target.Links.Add(link);
                restoredLinkIds.Add(link.LinkId);
                if (!isInsert)
                {
                    result.UpdatedCount++;
                }
                else
                {
                    result.InsertedCount++;
                }
            }

            _ = restoredLinkIds;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Restored section {Section}. Inserted {InsertedCount}, updated {UpdatedCount}, skipped {SkippedCount}, errors {ErrorCount}.",
            result.Section,
            result.InsertedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);

        return result;
    }

    private static bool ValidateBackupDiagramShell(BackupNetworkDiagramRecord diagram, RestoreSectionResult result)
    {
        if (string.IsNullOrWhiteSpace(diagram.Name))
        {
            result.Warnings.Add($"Skipped network diagram '{diagram.DiagramId}' because the diagram name is missing.");
            return false;
        }

        if (!IsFinite(diagram.CanvasWidth) || !IsFinite(diagram.CanvasHeight) || !IsFinite(diagram.ViewportPanX) || !IsFinite(diagram.ViewportPanY) || !IsFinite(diagram.ViewportZoom))
        {
            result.Warnings.Add($"Skipped network diagram '{diagram.Name}' because canvas or viewport values are invalid.");
            return false;
        }

        return true;
    }


    private static bool TryBuildNetworkDiagramArea(
        BackupNetworkDiagramRecord sourceDiagram,
        BackupNetworkDiagramAreaRecord sourceArea,
        string targetDiagramId,
        RestoreSectionResult result,
        out NetworkDiagramArea area)
    {
        area = new NetworkDiagramArea();
        if (string.IsNullOrWhiteSpace(sourceArea.AreaId)
            || string.IsNullOrWhiteSpace(sourceArea.Label)
            || !IsFinite(sourceArea.X)
            || !IsFinite(sourceArea.Y)
            || !IsFinite(sourceArea.Width)
            || !IsFinite(sourceArea.Height))
        {
            result.Warnings.Add($"Skipped area '{sourceArea.AreaId}' in network diagram '{sourceDiagram.Name}' because required area data is invalid.");
            return false;
        }

        var styleKey = NormalizeAreaStyle(sourceArea.StyleKey);
        if (styleKey == "__invalid")
        {
            result.Warnings.Add($"Skipped area '{sourceArea.AreaId}' in network diagram '{sourceDiagram.Name}' because area style '{sourceArea.StyleKey}' is unsupported.");
            return false;
        }

        area = new NetworkDiagramArea
        {
            AreaId = TrimOrDefault(sourceArea.AreaId, 64, Guid.NewGuid().ToString("N")),
            DiagramId = targetDiagramId,
            Label = TrimOrDefault(sourceArea.Label, 255, "Restored area"),
            Notes = TrimOptional(sourceArea.Notes, 2048),
            X = ClampFinite(sourceArea.X, -1000, 21000),
            Y = ClampFinite(sourceArea.Y, -1000, 21000),
            Width = ClampFinite(sourceArea.Width <= 0 ? 600 : sourceArea.Width, 80, 20000),
            Height = ClampFinite(sourceArea.Height <= 0 ? 350 : sourceArea.Height, 60, 20000),
            StyleKey = styleKey,
            SortOrder = sourceArea.SortOrder,
            CreatedAtUtc = sourceArea.CreatedAtUtc == default ? DateTimeOffset.UtcNow : sourceArea.CreatedAtUtc,
            UpdatedAtUtc = sourceArea.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : sourceArea.UpdatedAtUtc
        };
        return true;
    }

    private static string? NormalizeAreaStyle(string? styleKey)
    {
        if (string.IsNullOrWhiteSpace(styleKey))
        {
            return null;
        }

        var normalized = styleKey.Trim().ToLowerInvariant();
        return normalized is "neutral" or "blue" or "green" or "amber" or "red" or "purple" ? normalized : "__invalid";
    }

    private static bool TryBuildNetworkDiagramNode(
        BackupNetworkDiagramRecord sourceDiagram,
        BackupNetworkDiagramNodeRecord sourceNode,
        string targetDiagramId,
        IDictionary<string, string> endpointIdMapping,
        IReadOnlySet<string> existingEndpointIds,
        RestoreSectionResult result,
        out NetworkDiagramNode node)
    {
        node = new NetworkDiagramNode();
        if (string.IsNullOrWhiteSpace(sourceNode.NodeId)
            || string.IsNullOrWhiteSpace(sourceNode.DisplayLabel)
            || string.IsNullOrWhiteSpace(sourceNode.IconKey)
            || !IsFinite(sourceNode.X)
            || !IsFinite(sourceNode.Y)
            || !IsFinite(sourceNode.Width)
            || !IsFinite(sourceNode.Height))
        {
            result.Warnings.Add($"Skipped node '{sourceNode.NodeId}' in network diagram '{sourceDiagram.Name}' because required node data is invalid.");
            return false;
        }

        if (!Enum.TryParse<NetworkDiagramNodeType>(sourceNode.NodeType, ignoreCase: true, out var nodeType) || !Enum.IsDefined(nodeType))
        {
            result.Warnings.Add($"Skipped node '{sourceNode.NodeId}' in network diagram '{sourceDiagram.Name}' because node type '{sourceNode.NodeType}' is unsupported.");
            return false;
        }

        string? endpointId = null;
        if (nodeType == NetworkDiagramNodeType.MonitoredEndpoint)
        {
            if (string.IsNullOrWhiteSpace(sourceNode.EndpointId))
            {
                result.Warnings.Add($"Restored monitored endpoint node '{sourceNode.NodeId}' in network diagram '{sourceDiagram.Name}' without an endpoint reference because the backup endpoint id is empty.");
            }
            else
            {
                endpointId = ResolveMappedOrExistingId(sourceNode.EndpointId, endpointIdMapping, existingEndpointIds);
                if (endpointId is null)
                {
                    result.Warnings.Add($"Restored monitored endpoint node '{sourceNode.NodeId}' in network diagram '{sourceDiagram.Name}' with EndpointId cleared because endpoint '{sourceNode.EndpointId}' could not be resolved. Restore did not create an endpoint for this diagram reference.");
                }
                else if (!string.Equals(endpointId, sourceNode.EndpointId, StringComparison.Ordinal))
                {
                    result.Warnings.Add($"Remapped monitored endpoint node '{sourceNode.NodeId}' in network diagram '{sourceDiagram.Name}' from endpoint '{sourceNode.EndpointId}' to '{endpointId}'.");
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(sourceNode.EndpointId))
        {
            result.Warnings.Add($"Cleared endpoint reference on non-monitored node '{sourceNode.NodeId}' in network diagram '{sourceDiagram.Name}'.");
        }

        node = new NetworkDiagramNode
        {
            NodeId = TrimOrDefault(sourceNode.NodeId, 64, Guid.NewGuid().ToString("N")),
            DiagramId = targetDiagramId,
            NodeType = nodeType,
            EndpointId = endpointId,
            DisplayLabel = TrimOrDefault(sourceNode.DisplayLabel, 255, "Restored node"),
            IconKey = TrimOrDefault(sourceNode.IconKey, 64, "generic"),
            X = ClampFinite(sourceNode.X, -1000, 21000),
            Y = ClampFinite(sourceNode.Y, -1000, 21000),
            Width = ClampFinite(sourceNode.Width <= 0 ? 178 : sourceNode.Width, 40, 2000),
            Height = ClampFinite(sourceNode.Height <= 0 ? 78 : sourceNode.Height, 30, 2000),
            Notes = TrimOptional(sourceNode.Notes, 4096),
            MetadataJson = TrimOptional(sourceNode.MetadataJson, 65535),
            CreatedAtUtc = sourceNode.CreatedAtUtc == default ? DateTimeOffset.UtcNow : sourceNode.CreatedAtUtc,
            UpdatedAtUtc = sourceNode.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : sourceNode.UpdatedAtUtc
        };
        return true;
    }

    private static bool TryBuildNetworkDiagramLink(
        BackupNetworkDiagramRecord sourceDiagram,
        BackupNetworkDiagramLinkRecord sourceLink,
        string targetDiagramId,
        IReadOnlySet<string> restoredNodeIds,
        RestoreSectionResult result,
        out NetworkDiagramLink link)
    {
        link = new NetworkDiagramLink();
        if (string.IsNullOrWhiteSpace(sourceLink.LinkId)
            || string.IsNullOrWhiteSpace(sourceLink.SourceNodeId)
            || string.IsNullOrWhiteSpace(sourceLink.TargetNodeId))
        {
            result.Warnings.Add($"Skipped link '{sourceLink.LinkId}' in network diagram '{sourceDiagram.Name}' because required link references are missing.");
            return false;
        }

        if (string.Equals(sourceLink.SourceNodeId, sourceLink.TargetNodeId, StringComparison.Ordinal))
        {
            result.Warnings.Add($"Skipped self-link '{sourceLink.LinkId}' in network diagram '{sourceDiagram.Name}'.");
            return false;
        }

        if (!restoredNodeIds.Contains(sourceLink.SourceNodeId) || !restoredNodeIds.Contains(sourceLink.TargetNodeId))
        {
            result.Warnings.Add($"Skipped link '{sourceLink.LinkId}' in network diagram '{sourceDiagram.Name}' because one or both endpoint nodes were not restored.");
            return false;
        }

        var mediaType = NetworkDiagramLinkMediaTypes.Normalize(sourceLink.MediaType);
        var mediaSubtype = NetworkDiagramMediaSubtypes.Normalize(sourceLink.MediaSubtype ?? sourceLink.FibreSubtype, mediaType);
        var linkType = NetworkDiagramLinkTypes.Normalize(sourceLink.LinkType);
        var speedUnit = NetworkDiagramLinkSpeedUnits.Normalize(sourceLink.LinkSpeedUnit);
        if (!NetworkDiagramLinkMediaTypes.IsAllowed(mediaType)
            || !NetworkDiagramMediaSubtypes.IsAllowed(mediaSubtype, mediaType)
            || !NetworkDiagramLinkTypes.IsAllowed(linkType)
            || !NetworkDiagramLinkSpeedUnits.IsAllowed(speedUnit)
            || sourceLink.LinkSpeedValue is <= 0 or > 1000000
            || (sourceLink.LinkSpeedValue is null && speedUnit is not null)
            || (sourceLink.LinkSpeedValue is not null && speedUnit is null))
        {
            result.Warnings.Add($"Skipped link '{sourceLink.LinkId}' in network diagram '{sourceDiagram.Name}' because media, type, or speed metadata is invalid.");
            return false;
        }

        var lacpMemberCount = string.Equals(linkType, NetworkDiagramLinkTypes.Lacp, StringComparison.Ordinal)
            ? sourceLink.LacpMemberCount
            : null;
        if (lacpMemberCount is < 1 or > 16)
        {
            result.Warnings.Add($"Skipped link '{sourceLink.LinkId}' in network diagram '{sourceDiagram.Name}' because LACP member count is invalid.");
            return false;
        }

        link = new NetworkDiagramLink
        {
            LinkId = TrimOrDefault(sourceLink.LinkId, 64, Guid.NewGuid().ToString("N")),
            DiagramId = targetDiagramId,
            SourceNodeId = sourceLink.SourceNodeId,
            TargetNodeId = sourceLink.TargetNodeId,
            Label = TrimOptional(sourceLink.Label, 255),
            SourcePortLabel = TrimOptional(sourceLink.SourcePortLabel, 128),
            TargetPortLabel = TrimOptional(sourceLink.TargetPortLabel, 128),
            Notes = TrimOptional(sourceLink.Notes, 4096),
            MediaType = mediaType,
            FibreSubtype = mediaSubtype,
            LinkType = linkType,
            LinkSpeedValue = sourceLink.LinkSpeedValue is null ? null : Math.Round(sourceLink.LinkSpeedValue.Value, 3),
            LinkSpeedUnit = speedUnit,
            LacpMemberCount = lacpMemberCount,
            LacpMemberPortsJson = string.Equals(linkType, NetworkDiagramLinkTypes.Lacp, StringComparison.Ordinal) ? TrimOptional(sourceLink.LacpMemberPortsJson, 65535) : null,
            MetadataJson = TrimOptional(sourceLink.MetadataJson, 65535),
            CreatedAtUtc = sourceLink.CreatedAtUtc == default ? DateTimeOffset.UtcNow : sourceLink.CreatedAtUtc,
            UpdatedAtUtc = sourceLink.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : sourceLink.UpdatedAtUtc
        };
        return true;
    }

    private static bool TryBuildNetworkDiagramLinkVlan(
        BackupNetworkDiagramRecord sourceDiagram,
        BackupNetworkDiagramLinkRecord sourceLink,
        BackupNetworkDiagramLinkVlanRecord sourceVlan,
        string targetDiagramId,
        string targetLinkId,
        RestoreSectionResult result,
        out NetworkDiagramLinkVlan vlan)
    {
        vlan = new NetworkDiagramLinkVlan();
        var mode = NetworkDiagramVlanModes.Normalize(sourceVlan.Mode);
        if (sourceVlan.VlanId is < 1 or > 4094 || !NetworkDiagramVlanModes.IsAllowed(mode))
        {
            result.Warnings.Add($"Skipped VLAN metadata '{sourceVlan.VlanId}' on link '{sourceLink.LinkId}' in network diagram '{sourceDiagram.Name}' because VLAN data is invalid.");
            return false;
        }

        vlan = new NetworkDiagramLinkVlan
        {
            LinkVlanId = string.IsNullOrWhiteSpace(sourceVlan.LinkVlanId) ? Guid.NewGuid().ToString("N") : TrimOrDefault(sourceVlan.LinkVlanId, 64, Guid.NewGuid().ToString("N")),
            LinkId = targetLinkId,
            DiagramId = targetDiagramId,
            VlanId = sourceVlan.VlanId,
            Name = TrimOptional(sourceVlan.Name, 128),
            Mode = mode,
            Notes = TrimOptional(sourceVlan.Notes, 512),
            SortOrder = sourceVlan.SortOrder,
            CreatedAtUtc = sourceVlan.CreatedAtUtc == default ? DateTimeOffset.UtcNow : sourceVlan.CreatedAtUtc,
            UpdatedAtUtc = sourceVlan.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : sourceVlan.UpdatedAtUtc
        };
        return true;
    }

    private static string TrimOrDefault(string? value, int maxLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? TrimOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static double ClampFinite(double value, double min, double max)
    {
        if (!IsFinite(value))
        {
            return min;
        }

        return Math.Min(Math.Max(value, min), max);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

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

    private static bool TryParseTelegramInboundMode(string? rawValue, out TelegramInboundMode mode)
    {
        if (string.Equals(rawValue, "polling", StringComparison.OrdinalIgnoreCase))
        {
            mode = TelegramInboundMode.Polling;
            return true;
        }

        if (string.Equals(rawValue, "webhook", StringComparison.OrdinalIgnoreCase))
        {
            mode = TelegramInboundMode.Webhook;
            return true;
        }

        mode = default;
        return false;
    }

    private static List<BackupEndpointDependencyRecord> BuildLegacyDependencyRecords(IReadOnlyList<BackupEndpointRecord>? endpoints)
    {
        if (endpoints is null)
        {
            return [];
        }

        var records = new List<BackupEndpointDependencyRecord>();
        foreach (var endpoint in endpoints)
        {
            if (endpoint.DependsOnEndpointIds is null || endpoint.DependsOnEndpointIds.Count == 0)
            {
                continue;
            }

            foreach (var dependsOnEndpointId in endpoint.DependsOnEndpointIds)
            {
                records.Add(new BackupEndpointDependencyRecord
                {
                    EndpointId = endpoint.EndpointId,
                    DependsOnEndpointId = dependsOnEndpointId,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

        return records;
    }
}
