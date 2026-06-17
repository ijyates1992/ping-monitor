using PingMonitor.Web.Models;
using PingMonitor.Web.Services.NetworkDiagrams;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationBackupDocumentValidator
{
    void Validate(ConfigurationBackupDocument document, string fileIdForErrors);
}

public sealed class ConfigurationBackupDocumentValidator : IConfigurationBackupDocumentValidator
{
    public void Validate(ConfigurationBackupDocument document, string fileIdForErrors)
    {
        _ = fileIdForErrors;

        if (document.FormatVersion <= 0 || document.FormatVersion > ConfigurationBackupMetadata.CurrentFormatVersion)
        {
            throw new InvalidOperationException($"Backup formatVersion {document.FormatVersion} is not supported.");
        }

        if (string.IsNullOrWhiteSpace(document.BackupName)
            || string.IsNullOrWhiteSpace(document.AppVersion)
            || document.ExportedAtUtc == default)
        {
            throw new InvalidOperationException("Backup metadata is incomplete.");
        }

        if (!string.IsNullOrWhiteSpace(document.BackupSource)
            && !ConfigurationBackupSources.All.Contains(document.BackupSource, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Backup source metadata is not supported.");
        }

        if (document.Sections is null)
        {
            throw new InvalidOperationException("Backup sections are missing.");
        }

        var hasAtLeastOneSection =
            document.Sections.Agents is not null
            || document.Sections.Endpoints is not null
            || document.Sections.Groups is not null
            || document.Sections.Dependencies is not null
            || document.Sections.Assignments is not null
            || document.Sections.SecuritySettings is not null
            || document.Sections.NotificationSettings is not null
            || document.Sections.UserNotificationSettings is not null
            || document.Sections.AiAssistantSettings is not null
            || document.Sections.Identity is not null
            || document.Sections.NetworkDiagrams is not null;

        if (!hasAtLeastOneSection)
        {
            throw new InvalidOperationException("Backup does not contain any recognized configuration sections.");
        }

        if (document.Sections.Agents is not null)
        {
            foreach (var agent in document.Sections.Agents)
            {
                if (string.IsNullOrWhiteSpace(agent.InstanceId))
                {
                    throw new InvalidOperationException("Agent section contains an invalid record (instanceId missing).");
                }
            }
        }

        if (document.Sections.Endpoints is not null)
        {
            foreach (var endpoint in document.Sections.Endpoints)
            {
                if (string.IsNullOrWhiteSpace(endpoint.Name) || string.IsNullOrWhiteSpace(endpoint.Target))
                {
                    throw new InvalidOperationException("Endpoint section contains an invalid record (name/target missing).");
                }
            }
        }

        if (document.Sections.Groups is not null)
        {
            foreach (var group in document.Sections.Groups.Groups)
            {
                if (string.IsNullOrWhiteSpace(group.Name))
                {
                    throw new InvalidOperationException("Groups section contains an invalid record (name missing).");
                }
            }
        }

        if (document.Sections.Dependencies is not null)
        {
            foreach (var dependency in document.Sections.Dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency.EndpointId)
                    || string.IsNullOrWhiteSpace(dependency.DependsOnEndpointId))
                {
                    throw new InvalidOperationException("Dependencies section contains an invalid record.");
                }
            }
        }

        if (document.Sections.Assignments is not null)
        {
            foreach (var assignment in document.Sections.Assignments)
            {
                if (string.IsNullOrWhiteSpace(assignment.AgentId)
                    || string.IsNullOrWhiteSpace(assignment.EndpointId)
                    || string.IsNullOrWhiteSpace(assignment.CheckType))
                {
                    throw new InvalidOperationException("Assignment section contains an invalid record.");
                }
            }
        }

        if (document.Sections.Identity is not null)
        {
            foreach (var user in document.Sections.Identity.Users)
            {
                if (string.IsNullOrWhiteSpace(user.NormalizedUserName) && string.IsNullOrWhiteSpace(user.NormalizedEmail))
                {
                    throw new InvalidOperationException("Identity section contains a user without normalized username/email.");
                }
            }
        }

        if (document.Sections.UserNotificationSettings is not null)
        {
            foreach (var userSettings in document.Sections.UserNotificationSettings)
            {
                if (string.IsNullOrWhiteSpace(userSettings.UserId))
                {
                    throw new InvalidOperationException("User notification settings section contains an invalid record (userId missing).");
                }
            }
        }

        if (document.Sections.NetworkDiagrams is not null)
        {
            ValidateNetworkDiagrams(document.Sections.NetworkDiagrams);
        }

    }


    private static void ValidateNetworkDiagrams(BackupNetworkDiagramSection section)
    {
        foreach (var diagram in section.Diagrams)
        {
            if (string.IsNullOrWhiteSpace(diagram.DiagramId) || string.IsNullOrWhiteSpace(diagram.Name))
            {
                throw new InvalidOperationException("Network diagrams section contains an invalid diagram record (id/name missing).");
            }

            ValidateFinite(diagram.CanvasWidth, "diagram canvas width");
            ValidateFinite(diagram.CanvasHeight, "diagram canvas height");
            ValidateFinite(diagram.ViewportPanX, "diagram viewport pan X");
            ValidateFinite(diagram.ViewportPanY, "diagram viewport pan Y");
            ValidateFinite(diagram.ViewportZoom, "diagram viewport zoom");


            var areaIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var area in diagram.Areas)
            {
                if (string.IsNullOrWhiteSpace(area.AreaId) || string.IsNullOrWhiteSpace(area.Label))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains an invalid area record.");
                }

                if (!areaIds.Add(area.AreaId))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains duplicate area id '{area.AreaId}'.");
                }

                ValidateFinite(area.X, "area X");
                ValidateFinite(area.Y, "area Y");
                ValidateFinite(area.Width, "area width");
                ValidateFinite(area.Height, "area height");
                var styleKey = string.IsNullOrWhiteSpace(area.StyleKey) ? null : area.StyleKey.Trim().ToLowerInvariant();
                if (styleKey is not null && styleKey is not ("neutral" or "blue" or "green" or "amber" or "red" or "purple"))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains unsupported area style '{area.StyleKey}'.");
                }
            }

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in diagram.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.NodeId) || string.IsNullOrWhiteSpace(node.DisplayLabel) || string.IsNullOrWhiteSpace(node.IconKey))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains an invalid node record.");
                }

                if (!nodeIds.Add(node.NodeId))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains duplicate node id '{node.NodeId}'.");
                }

                if (!Enum.TryParse<NetworkDiagramNodeType>(node.NodeType, ignoreCase: true, out var nodeType) || !Enum.IsDefined(nodeType))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains unsupported node type '{node.NodeType}'.");
                }

                ValidateFinite(node.X, "node X");
                ValidateFinite(node.Y, "node Y");
                ValidateFinite(node.Width, "node width");
                ValidateFinite(node.Height, "node height");
            }

            var linkIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in diagram.Links)
            {
                if (string.IsNullOrWhiteSpace(link.LinkId) || string.IsNullOrWhiteSpace(link.SourceNodeId) || string.IsNullOrWhiteSpace(link.TargetNodeId))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains an invalid link record.");
                }

                if (!linkIds.Add(link.LinkId))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains duplicate link id '{link.LinkId}'.");
                }

                if (string.Equals(link.SourceNodeId, link.TargetNodeId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains an invalid self-link '{link.LinkId}'.");
                }

                if (!NetworkDiagramLinkMediaTypes.IsAllowed(link.MediaType))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains unsupported media type '{link.MediaType}'.");
                }

                if (!NetworkDiagramMediaSubtypes.IsAllowed(link.MediaSubtype ?? link.FibreSubtype, link.MediaType))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains unsupported media subtype on link '{link.LinkId}'.");
                }

                if (!NetworkDiagramLinkTypes.IsAllowed(link.LinkType))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains unsupported link type '{link.LinkType}'.");
                }

                if (link.LinkSpeedValue is <= 0 or > 1000000 || !NetworkDiagramLinkSpeedUnits.IsAllowed(link.LinkSpeedUnit))
                {
                    throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains invalid link speed metadata on link '{link.LinkId}'.");
                }

                foreach (var vlan in link.Vlans)
                {
                    if (vlan.VlanId is < 1 or > 4094 || !NetworkDiagramVlanModes.IsAllowed(vlan.Mode))
                    {
                        throw new InvalidOperationException($"Network diagram '{diagram.Name}' contains invalid VLAN metadata on link '{link.LinkId}'.");
                    }
                }
            }
        }
    }

    private static void ValidateFinite(double value, string fieldName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException($"Network diagrams section contains invalid {fieldName}.");
        }
    }

}
