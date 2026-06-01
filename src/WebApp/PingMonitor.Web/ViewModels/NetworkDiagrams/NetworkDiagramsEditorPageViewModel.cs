namespace PingMonitor.Web.ViewModels.NetworkDiagrams;

public sealed class NetworkDiagramsEditorPageViewModel
{
    public const string DocumentationOnlyNotice =
        "Diagrams are documentation-only and do not alter monitoring state, dependencies, alerts, or agent behaviour.";

    public string PageTitle { get; init; } = "Network diagrams";

    public string Notice { get; init; } = DocumentationOnlyNotice;

    public bool LayoutIsSaved { get; init; }
}
