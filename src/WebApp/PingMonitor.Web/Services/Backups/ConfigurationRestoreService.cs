using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
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

        if (selectedSections.Contains(ConfigurationBackupSections.Identity, StringComparer.Ordinal))
        {
            var section = await RestoreIdentityAsync(backup, cancellationToken);
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
