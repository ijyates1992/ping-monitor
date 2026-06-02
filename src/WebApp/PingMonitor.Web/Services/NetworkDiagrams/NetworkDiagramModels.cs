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

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        OM1,
        OM2,
        OM3,
        OM4,
        OM5,
        OS1,
        OS2,
        Other
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
    public string? FibreSubtype { get; init; }
    public string? LinkType { get; init; }
    public decimal? LinkSpeedValue { get; init; }
    public string? LinkSpeedUnit { get; init; }
    public int? LacpMemberCount { get; init; }
    public string? LacpMemberPortsJson { get; init; }
    public string? MetadataJson { get; init; }
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
    public string? FibreSubtype { get; init; }
    public string LinkType { get; init; } = NetworkDiagramLinkTypes.Standard;
    public decimal? LinkSpeedValue { get; init; }
    public string? LinkSpeedUnit { get; init; }
    public int? LacpMemberCount { get; init; }
    public string? LacpMemberPortsJson { get; init; }
    public string? MetadataJson { get; init; }
}
