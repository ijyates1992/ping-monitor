using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.NetworkDiagrams;

public sealed class NetworkDiagramListPageViewModel
{
    public bool IsAdmin { get; init; }

    public IReadOnlyList<NetworkDiagramListItemViewModel> Diagrams { get; init; } = [];
    public CreateNetworkDiagramViewModel CreateDiagram { get; init; } = new();
}

public sealed class NetworkDiagramListItemViewModel
{
    public string DiagramId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int NodeCount { get; init; }
    public int LinkCount { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class CreateNetworkDiagramViewModel
{
    [Required]
    [StringLength(255)]
    public string Name { get; init; } = string.Empty;

    [StringLength(2048)]
    public string? Description { get; init; }
}

public sealed class NetworkDiagramEditorPageViewModel
{
    public const string DocumentationOnlyNotice =
        "Diagrams are documentation-only and do not alter monitoring state, dependencies, alerts, or agent behaviour.";

    public string PageTitle { get; init; } = "Network diagrams";

    public string Notice { get; init; } = DocumentationOnlyNotice;

    public bool LayoutIsSaved { get; init; } = true;

    public string DiagramId { get; init; } = string.Empty;

    public string DiagramName { get; init; } = string.Empty;

    public string? DiagramDescription { get; init; }

    public string LoadUrl { get; init; } = string.Empty;

    public string SaveUrl { get; init; } = string.Empty;

    public string ExportPdfUrl { get; init; } = string.Empty;

    public string ExportPngUrl { get; init; } = string.Empty;

    public string ExportSvgUrl { get; init; } = string.Empty;

    public string ViewerUrl { get; init; } = string.Empty;

    public IReadOnlyList<NetworkDiagramEndpointToolboxItemViewModel> MonitoredEndpoints { get; init; } = [];
}

public sealed class NetworkDiagramEndpointToolboxItemViewModel
{
    public string EndpointId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string IconKey { get; init; } = "generic";

    public Models.EndpointStateKind SummaryState { get; init; } = Models.EndpointStateKind.Unknown;
}


public sealed class NetworkDiagramViewerPageViewModel
{
    public const string DocumentationOnlyNotice =
        "Viewer overlays existing monitoring status only. Visual links remain documentation-only.";

    public string PageTitle { get; init; } = "Network diagram";

    public string Notice { get; init; } = DocumentationOnlyNotice;

    public string DiagramId { get; init; } = string.Empty;

    public string DiagramName { get; init; } = string.Empty;

    public string? DiagramDescription { get; init; }

    public string LoadUrl { get; init; } = string.Empty;

    public string LiveDataUrl { get; init; } = string.Empty;

    public string? EditUrl { get; init; }

    public string? ExportPdfUrl { get; init; }

    public string? ExportPngUrl { get; init; }

    public string? ExportSvgUrl { get; init; }

    public bool IsAdmin { get; init; }
}
