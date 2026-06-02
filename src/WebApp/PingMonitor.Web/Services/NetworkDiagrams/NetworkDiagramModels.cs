using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.NetworkDiagrams;

public static class NetworkDiagramLinkMediaTypes
{
    public const string Copper = "Copper";
    public const string Fibre = "Fibre";
    public const string Wireless = "Wireless";
    public const string Dac = "DAC";
    public const string Vpn = "VPN";
    public const string Virtual = "Virtual";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        Copper,
        Fibre,
        Wireless,
        Dac,
        Vpn,
        Virtual,
        Other
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "default", StringComparison.OrdinalIgnoreCase))
        {
            return Copper;
        }

        var trimmed = value.Trim();
        return Allowed.FirstOrDefault(allowed => string.Equals(allowed, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    public static bool IsAllowed(string? value) => Allowed.Contains(Normalize(value));
}

public static class NetworkDiagramLinkTypes
{
    public const string Standard = "Standard";
    public const string Trunk = "Trunk";
    public const string Access = "Access";
    public const string Lacp = "LACP";
    public const string PointToPoint = "PointToPoint";
    public const string Backhaul = "Backhaul";
    public const string Wan = "WAN";
    public const string Management = "Management";
    public const string Logical = "Logical";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        Standard,
        Trunk,
        Access,
        Lacp,
        PointToPoint,
        Backhaul,
        Wan,
        Management,
        Logical,
        Other
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "default", StringComparison.OrdinalIgnoreCase))
        {
            return Standard;
        }

        var trimmed = value.Trim();
        return Allowed.FirstOrDefault(allowed => string.Equals(allowed, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    public static bool IsAllowed(string? value) => Allowed.Contains(Normalize(value));
}


public static class NetworkDiagramVlanModes
{
    public const string Tagged = "Tagged";
    public const string Untagged = "Untagged";
    public const string Native = "Native";
    public const string Management = "Management";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        Tagged,
        Untagged,
        Native,
        Management,
        Other
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return Allowed.FirstOrDefault(allowed => string.Equals(allowed, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    public static bool IsAllowed(string? value) => Allowed.Contains(Normalize(value));
}

public static class NetworkDiagramMediaSubtypes
{
    public const string None = "None";
    public const string Other = "Other";

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedByMediaType = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
    {
        [NetworkDiagramLinkMediaTypes.Copper] = new HashSet<string>(StringComparer.Ordinal) { "Cat5e", "Cat6", "Cat6a", "Cat7", "Cat8", "Coax", Other },
        [NetworkDiagramLinkMediaTypes.Fibre] = new HashSet<string>(StringComparer.Ordinal) { "OM1", "OM2", "OM3", "OM4", "OM5", "OS1", "OS2", Other },
        [NetworkDiagramLinkMediaTypes.Wireless] = new HashSet<string>(StringComparer.Ordinal) { "802.11a", "802.11b", "802.11g", "802.11n / Wi-Fi 4", "802.11ac / Wi-Fi 5", "802.11ax / Wi-Fi 6", "802.11be / Wi-Fi 7", "60GHz", Other },
        [NetworkDiagramLinkMediaTypes.Dac] = new HashSet<string>(StringComparer.Ordinal) { "Passive DAC", "Active DAC", "AOC", Other },
        [NetworkDiagramLinkMediaTypes.Vpn] = new HashSet<string>(StringComparer.Ordinal) { "IPsec", "WireGuard", "OpenVPN", "GRE", Other },
        [NetworkDiagramLinkMediaTypes.Virtual] = new HashSet<string>(StringComparer.Ordinal) { "Hyper-V vSwitch", "VMware vSwitch", "VLAN interface", "Loopback", Other },
        [NetworkDiagramLinkMediaTypes.Other] = new HashSet<string>(StringComparer.Ordinal) { None, Other }
    };

    public static string? Normalize(string? value, string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalizedMediaType = NetworkDiagramLinkMediaTypes.Normalize(mediaType);
        var trimmed = value.Trim();
        if (string.Equals(trimmed, None, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedMediaType == NetworkDiagramLinkMediaTypes.Other ? None : null;
        }

        return GetAllowed(normalizedMediaType).FirstOrDefault(allowed => string.Equals(allowed, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    public static bool IsAllowed(string? value, string? mediaType)
    {
        var normalized = Normalize(value, mediaType);
        return normalized is null || GetAllowed(NetworkDiagramLinkMediaTypes.Normalize(mediaType)).Contains(normalized);
    }

    public static IReadOnlySet<string> GetAllowed(string? mediaType)
    {
        var normalizedMediaType = NetworkDiagramLinkMediaTypes.Normalize(mediaType);
        return AllowedByMediaType.TryGetValue(normalizedMediaType, out var allowed) ? allowed : AllowedByMediaType[NetworkDiagramLinkMediaTypes.Other];
    }
}

[Obsolete("Use NetworkDiagramMediaSubtypes for generic media subtype validation.")]
public static class NetworkDiagramFibreSubtypes
{
    public const string OM1 = "OM1";
    public const string OM2 = "OM2";
    public const string OM3 = "OM3";
    public const string OM4 = "OM4";
    public const string OM5 = "OM5";
    public const string OS1 = "OS1";
    public const string OS2 = "OS2";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> Allowed = NetworkDiagramMediaSubtypes.GetAllowed(NetworkDiagramLinkMediaTypes.Fibre);

    public static string? Normalize(string? value) => NetworkDiagramMediaSubtypes.Normalize(value, NetworkDiagramLinkMediaTypes.Fibre);

    public static bool IsAllowed(string? value) => NetworkDiagramMediaSubtypes.IsAllowed(value, NetworkDiagramLinkMediaTypes.Fibre);
}


public static class NetworkDiagramLinkSpeedUnits
{
    public const string Mbps = "Mbps";
    public const string Gbps = "Gbps";
    public const string Tbps = "Tbps";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        Mbps,
        Gbps,
        Tbps
    };

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return Allowed.FirstOrDefault(allowed => string.Equals(allowed, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    public static bool IsAllowed(string? value)
    {
        var normalized = Normalize(value);
        return normalized is null || Allowed.Contains(normalized);
    }
}

public sealed class NetworkDiagramSaveRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public double CanvasWidth { get; init; }
    public double CanvasHeight { get; init; }
    public double ViewportPanX { get; init; }
    public double ViewportPanY { get; init; }
    public double ViewportZoom { get; init; }
    public IReadOnlyList<NetworkDiagramNodeSaveRequest> Nodes { get; init; } = [];
    public IReadOnlyList<NetworkDiagramLinkSaveRequest> Links { get; init; } = [];
}

public sealed class NetworkDiagramNodeSaveRequest
{
    public string NodeId { get; init; } = string.Empty;
    public NetworkDiagramNodeType NodeType { get; init; }
    public string? EndpointId { get; init; }
    public string DisplayLabel { get; init; } = string.Empty;
    public string IconKey { get; init; } = "generic";
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string? Notes { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed class NetworkDiagramLinkSaveRequest
{
    public string LinkId { get; init; } = string.Empty;
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? SourcePortLabel { get; init; }
    public string? TargetPortLabel { get; init; }
    public string? Notes { get; init; }
    public string? MediaType { get; init; }
    public string? MediaSubtype { get; init; }
    public string? FibreSubtype { get; init; }
    public string? LinkType { get; init; }
    public decimal? LinkSpeedValue { get; init; }
    public string? LinkSpeedUnit { get; init; }
    public int? LacpMemberCount { get; init; }
    public string? LacpMemberPortsJson { get; init; }
    public string? MetadataJson { get; init; }
    public IReadOnlyList<NetworkDiagramLinkVlanSaveRequest> Vlans { get; init; } = [];
}

public sealed class NetworkDiagramLinkVlanSaveRequest
{
    public int? VlanId { get; init; }
    public string? Name { get; init; }
    public string? Mode { get; init; }
    public string? Notes { get; init; }
}

public sealed class NetworkDiagramDto
{
    public string DiagramId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public double CanvasWidth { get; init; }
    public double CanvasHeight { get; init; }
    public double ViewportPanX { get; init; }
    public double ViewportPanY { get; init; }
    public double ViewportZoom { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public IReadOnlyList<NetworkDiagramNodeDto> Nodes { get; init; } = [];
    public IReadOnlyList<NetworkDiagramLinkDto> Links { get; init; } = [];
}

public sealed class NetworkDiagramNodeDto
{
    public string NodeId { get; init; } = string.Empty;
    public string NodeType { get; init; } = string.Empty;
    public string? EndpointId { get; init; }
    public string DisplayLabel { get; init; } = string.Empty;
    public string IconKey { get; init; } = "generic";
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string? Notes { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed class NetworkDiagramLinkDto
{
    public string LinkId { get; init; } = string.Empty;
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? SourcePortLabel { get; init; }
    public string? TargetPortLabel { get; init; }
    public string? Notes { get; init; }
    public string MediaType { get; init; } = NetworkDiagramLinkMediaTypes.Copper;
    public string? MediaSubtype { get; init; }
    public string? FibreSubtype { get; init; }
    public string LinkType { get; init; } = NetworkDiagramLinkTypes.Standard;
    public decimal? LinkSpeedValue { get; init; }
    public string? LinkSpeedUnit { get; init; }
    public int? LacpMemberCount { get; init; }
    public string? LacpMemberPortsJson { get; init; }
    public string? MetadataJson { get; init; }
    public IReadOnlyList<NetworkDiagramLinkVlanDto> Vlans { get; init; } = [];
}

public sealed class NetworkDiagramLinkVlanDto
{
    public int VlanId { get; init; }
    public string? Name { get; init; }
    public string Mode { get; init; } = NetworkDiagramVlanModes.Tagged;
    public string? Notes { get; init; }
    public int SortOrder { get; init; }
}
