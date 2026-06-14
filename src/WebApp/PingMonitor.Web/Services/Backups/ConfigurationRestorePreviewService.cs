using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.NetworkDiagrams;
using PingMonitor.Web.Support;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationRestorePreviewService
{
    Task<ConfigurationBackupPreview> GetPreviewAsync(string fileId, CancellationToken cancellationToken);
}

public sealed class ConfigurationRestorePreviewService : IConfigurationRestorePreviewService
{
    private readonly IConfigurationBackupDocumentLoader _documentLoader;
    private readonly ILogger<ConfigurationRestorePreviewService> _logger;
    private readonly PingMonitorDbContext? _dbContext;

    public ConfigurationRestorePreviewService(
        IConfigurationBackupDocumentLoader documentLoader,
        ILogger<ConfigurationRestorePreviewService> logger,
        PingMonitorDbContext? dbContext = null)
    {
        _documentLoader = documentLoader;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task<ConfigurationBackupPreview> GetPreviewAsync(string fileId, CancellationToken cancellationToken)
    {
        var document = await _documentLoader.LoadValidatedDocumentAsync(fileId, cancellationToken);

        var includedSections = GetIncludedSections(document).ToArray();
        var warnings = await BuildNetworkDiagramPreviewWarningsAsync(document, cancellationToken);
        var preview = new ConfigurationBackupPreview
        {
            FileId = fileId,
            FileName = fileId,
            Metadata = new RestorePreviewMetadata
            {
                BackupName = document.BackupName,
                ExportedAtUtc = document.ExportedAtUtc,
                AppVersion = document.AppVersion,
                FormatVersion = document.FormatVersion,
                Notes = document.Notes
            },
            IncludedSections = includedSections,
            Counts = new ConfigurationBackupSectionCounts
            {
                Agents = document.Sections.Agents?.Count ?? 0,
                Endpoints = document.Sections.Endpoints?.Count ?? 0,
                Groups = document.Sections.Groups?.Groups.Count ?? 0,
                GroupEndpointMemberships = document.Sections.Groups?.EndpointMemberships.Count ?? 0,
                Dependencies = document.Sections.Dependencies?.Count ?? document.Sections.Endpoints?.Sum(x => x.DependsOnEndpointIds?.Count ?? 0) ?? 0,
                Assignments = document.Sections.Assignments?.Count ?? 0,
                SecuritySettings = document.Sections.SecuritySettings is null ? 0 : 1,
                NotificationSettings = document.Sections.NotificationSettings is null ? 0 : 1,
                AiAssistantSettings = document.Sections.AiAssistantSettings is null ? 0 : 1,
                UserNotificationSettings = document.Sections.UserNotificationSettings?.Count ?? 0,
                IdentityUsers = document.Sections.Identity?.Users.Count ?? 0,
                IdentityRoles = document.Sections.Identity?.Roles.Count ?? 0,
                IdentityUserRoles = document.Sections.Identity?.UserRoles.Count ?? 0,
                NetworkDiagrams = document.Sections.NetworkDiagrams?.Diagrams.Count ?? 0,
                NetworkDiagramAreas = document.Sections.NetworkDiagrams?.Diagrams.Sum(x => x.Areas.Count) ?? 0,
                NetworkDiagramNodes = document.Sections.NetworkDiagrams?.Diagrams.Sum(x => x.Nodes.Count) ?? 0,
                NetworkDiagramLinks = document.Sections.NetworkDiagrams?.Diagrams.Sum(x => x.Links.Count) ?? 0,
                NetworkDiagramLinkVlans = document.Sections.NetworkDiagrams?.Diagrams.Sum(x => x.Links.Sum(link => link.Vlans.Count)) ?? 0
            },
            Warnings = warnings
        };

        _logger.LogInformation(
            "Loaded restore preview for {FileId}. Included sections: {Sections}.",
            LogValueSanitizer.ForLog(fileId),
            string.Join(",", includedSections));

        return preview;
    }

    private async Task<IReadOnlyList<string>> BuildNetworkDiagramPreviewWarningsAsync(ConfigurationBackupDocument document, CancellationToken cancellationToken)
    {
        var section = document.Sections.NetworkDiagrams;
        if (section is null)
        {
            return [];
        }

        var warnings = new List<string>();
        var existingEndpointIds = _dbContext is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await _dbContext.Endpoints.AsNoTracking().Select(x => x.EndpointId).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var backupEndpointIds = (document.Sections.Endpoints ?? []).Select(x => x.EndpointId).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<NetworkDiagram> existingDiagrams = [];
        if (_dbContext is not null)
        {
            existingDiagrams = await _dbContext.NetworkDiagrams.AsNoTracking().ToListAsync(cancellationToken);
            var createCount = section.Diagrams.Count(source => !existingDiagrams.Any(existing => string.Equals(existing.DiagramId, source.DiagramId, StringComparison.Ordinal) || string.Equals(existing.Name, source.Name, StringComparison.OrdinalIgnoreCase)));
            var updateCount = section.Diagrams.Count - createCount;
            warnings.Add($"Network diagrams preview: {createCount} diagram(s) to create and {updateCount} diagram(s) to update by matching diagram ID or name.");
        }
        else
        {
            warnings.Add("Network diagrams preview: existing diagram create/update matching requires database context; backup structure validation still ran.");
        }

        foreach (var diagram in section.Diagrams)
        {
            var validNodeIds = diagram.Nodes.Select(x => x.NodeId).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal);
            foreach (var node in diagram.Nodes)
            {
                if (!Enum.TryParse<NetworkDiagramNodeType>(node.NodeType, ignoreCase: true, out var nodeType) || !Enum.IsDefined(nodeType))
                {
                    warnings.Add($"Network diagram '{diagram.Name}' node '{node.NodeId}' has unsupported node type '{node.NodeType}' and will be skipped.");
                    continue;
                }

                if (nodeType == NetworkDiagramNodeType.MonitoredEndpoint && !string.IsNullOrWhiteSpace(node.EndpointId))
                {
                    if (existingEndpointIds.Contains(node.EndpointId))
                    {
                        continue;
                    }

                    if (backupEndpointIds.Contains(node.EndpointId))
                    {
                        warnings.Add($"Network diagram '{diagram.Name}' monitored node '{node.NodeId}' references endpoint '{node.EndpointId}', which can be remapped if the endpoints section is restored in the same operation.");
                    }
                    else
                    {
                        warnings.Add($"Network diagram '{diagram.Name}' monitored node '{node.NodeId}' references missing endpoint '{node.EndpointId}'; restore will keep the node but clear EndpointId instead of creating an endpoint.");
                    }
                }
            }

            foreach (var link in diagram.Links)
            {
                if (string.Equals(link.SourceNodeId, link.TargetNodeId, StringComparison.Ordinal)
                    || !validNodeIds.Contains(link.SourceNodeId)
                    || !validNodeIds.Contains(link.TargetNodeId)
                    || !NetworkDiagramLinkMediaTypes.IsAllowed(link.MediaType)
                    || !NetworkDiagramMediaSubtypes.IsAllowed(link.MediaSubtype ?? link.FibreSubtype, link.MediaType)
                    || !NetworkDiagramLinkTypes.IsAllowed(link.LinkType)
                    || link.LinkSpeedValue is <= 0 or > 1000000
                    || !NetworkDiagramLinkSpeedUnits.IsAllowed(link.LinkSpeedUnit))
                {
                    warnings.Add($"Network diagram '{diagram.Name}' link '{link.LinkId}' has invalid references or metadata and will be skipped with its VLAN metadata.");
                    continue;
                }

                foreach (var vlan in link.Vlans)
                {
                    if (vlan.VlanId is < 1 or > 4094 || !NetworkDiagramVlanModes.IsAllowed(vlan.Mode))
                    {
                        warnings.Add($"Network diagram '{diagram.Name}' link '{link.LinkId}' VLAN '{vlan.VlanId}' is invalid and will be skipped.");
                    }
                }
            }
        }

        return warnings;
    }

    private static IEnumerable<string> GetIncludedSections(ConfigurationBackupDocument document)
    {
        if (document.Sections.Agents is not null)
        {
            yield return ConfigurationBackupSections.Agents;
        }

        if (document.Sections.Endpoints is not null)
        {
            yield return ConfigurationBackupSections.Endpoints;
        }

        if (document.Sections.Groups is not null)
        {
            yield return ConfigurationBackupSections.Groups;
        }

        if (document.Sections.Dependencies is not null
            || document.Sections.Endpoints?.Any(x => x.DependsOnEndpointIds is { Count: > 0 }) == true)
        {
            yield return ConfigurationBackupSections.Dependencies;
        }

        if (document.Sections.Assignments is not null)
        {
            yield return ConfigurationBackupSections.Assignments;
        }

        if (document.Sections.SecuritySettings is not null)
        {
            yield return ConfigurationBackupSections.SecuritySettings;
        }

        if (document.Sections.NotificationSettings is not null)
        {
            yield return ConfigurationBackupSections.NotificationSettings;
        }

        if (document.Sections.UserNotificationSettings is not null)
        {
            yield return ConfigurationBackupSections.UserNotificationSettings;
        }
        if (document.Sections.AiAssistantSettings is not null)
        {
            yield return ConfigurationBackupSections.AiAssistantSettings;
        }

        if (document.Sections.Identity is not null)
        {
            yield return ConfigurationBackupSections.Identity;
        }

        if (document.Sections.NetworkDiagrams is not null)
        {
            yield return ConfigurationBackupSections.NetworkDiagrams;
        }
    }
}
