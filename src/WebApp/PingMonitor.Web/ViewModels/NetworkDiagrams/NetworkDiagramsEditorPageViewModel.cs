using PingMonitor.Web.Models;

namespace PingMonitor.Web.ViewModels.NetworkDiagrams;

public sealed class NetworkDiagramsEditorPageViewModel
{
    public const string DocumentationOnlyNotice =
        "Diagrams are documentation-only and do not alter monitoring state, dependencies, alerts, or agent behaviour.";

    public string PageTitle { get; init; } = "Network diagrams";

    public string Notice { get; init; } = DocumentationOnlyNotice;

    public bool LayoutIsSaved { get; init; }

    public IReadOnlyList<NetworkDiagramEndpointToolboxItemViewModel> MonitoredEndpoints { get; init; } = [];
}

public sealed class NetworkDiagramEndpointToolboxItemViewModel
{
    public string EndpointId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string IconKey { get; init; } = "generic";

    public EndpointStateKind SummaryState { get; init; } = EndpointStateKind.Unknown;
}
