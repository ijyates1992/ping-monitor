using Xunit;
using PingMonitor.Web.Services.AiChat;

namespace PingMonitor.Web.Tests;

public sealed class AiNetworkDiagramSliceTests
{
    [Fact]
    public void BuiltInPrompt_IncludesSavedDiagramOnlyCaveats()
    {
        Assert.Contains("Use Network Diagram tools", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("saved documentation only", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("not live switch port state", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Do not claim a diagram link is currently up/down", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Do not create or modify monitoring dependencies from diagram links", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("According to the saved diagram", AiChatService.BuiltInSystemPrompt);
    }

    [Fact]
    public void DiagramTools_AreRegisteredOnSharedAiToolPath()
    {
        var programSource = ReadWebSource("Program.cs");
        Assert.Contains("ListNetworkDiagramsAiTool", programSource);
        Assert.Contains("SearchDiagramNodesAiTool", programSource);
        Assert.Contains("GetNetworkDiagramAiTool", programSource);
        Assert.Contains("FindDiagramConnectionsAiTool", programSource);
        Assert.Contains("IAiToolRegistry", programSource);
    }

    [Fact]
    public void DiagramToolDefinitions_AreReadOnlyAndDocumentSavedStateLimitations()
    {
        var source = ReadWebSource("Services", "AiTools", "NetworkDiagramAiTools.cs");
        Assert.Contains("list_network_diagrams", source);
        Assert.Contains("search_diagram_nodes", source);
        Assert.Contains("get_network_diagram", source);
        Assert.Contains("find_diagram_connections", source);
        Assert.Contains("Network Diagram lookup is disabled", source);
        Assert.Contains("permissionFiltered", source);
        Assert.Contains("isLiveLinkState = false", source);
        Assert.Contains("saved_network_diagram", source);
        Assert.Contains("This is saved diagram documentation", source);
        Assert.DoesNotContain("SaveAsync", source);
        Assert.DoesNotContain("DeleteAsync", source);
        Assert.DoesNotContain("CreateAsync", source);
    }

    [Fact]
    public void DiagramTools_EnforceBoundsAndHideHiddenEndpointLinksBeforeReturningResults()
    {
        var source = ReadWebSource("Services", "AiTools", "NetworkDiagramAiTools.cs");
        Assert.Contains("MaxDiagrams = 50", source);
        Assert.Contains("MaxNodeMatches = 10", source);
        Assert.Contains("MaxConnections = 50", source);
        Assert.Contains("MaxFullNodes = 120", source);
        Assert.Contains("visibleEndpointIds", source);
        Assert.Contains("ApplyEndpointFilter(DbContext.Endpoints.AsNoTracking(), ids)", source);
        Assert.Contains("Expression.OrElse", source);
        Assert.DoesNotContain("ids.Contains(x.EndpointId)", source);
        Assert.Contains("visibleNodeIds.Contains(l.SourceNodeId) && visibleNodeIds.Contains(l.TargetNodeId)", source);
        Assert.Contains("SafeMetadata", source);
        Assert.Contains("Truncate", source);
    }

    private static string ReadWebSource(params string[] path)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", Path.Combine(path));
        return File.ReadAllText(fullPath);
    }
}
