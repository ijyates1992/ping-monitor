using PingMonitor.Web.Services.AiChat;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AiDependencyToolsSliceTests
{
    [Fact]
    public void BuiltInPrompt_IncludesDependencySemantics()
    {
        Assert.Contains("read-only dependency tools", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Dependency data is saved monitoring configuration", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Only a direct parent endpoint in DOWN state suppresses", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("SUPPRESSED parents do not cascade suppression", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("UNKNOWN is not DOWN", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Do not create, edit, delete, infer", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("may be suppressed or affected according to the saved dependency configuration", AiChatService.BuiltInSystemPrompt);
    }

    [Fact]
    public void DependencyTools_AreRegisteredOnSharedAiToolPath()
    {
        var programSource = ReadWebSource("Program.cs");
        Assert.Contains("GetEndpointDependenciesAiTool", programSource);
        Assert.Contains("GetDependencyImpactAiTool", programSource);
        Assert.Contains("GetDependencySummaryAiTool", programSource);
        Assert.Contains("ExplainEndpointSuppressionAiTool", programSource);
        Assert.Contains("IAiToolRegistry", programSource);
    }

    [Fact]
    public void DependencyToolDefinitions_AreReadOnlyAndDocumentSavedDependencySemantics()
    {
        var source = ReadWebSource("Services", "AiTools", "DependencyAiTools.cs");
        Assert.Contains("get_endpoint_dependencies", source);
        Assert.Contains("get_dependency_impact", source);
        Assert.Contains("get_dependency_summary", source);
        Assert.Contains("explain_endpoint_suppression", source);
        Assert.Contains("permissionFiltered", source);
        Assert.Contains("Only direct parent DOWN state suppresses", source);
        Assert.Contains("SUPPRESSED parent endpoints do not cascade suppression", source);
        Assert.Contains("UNKNOWN is not DOWN", source);
        Assert.Contains("not inferred topology", source);
        Assert.DoesNotContain("SaveChangesAsync", source);
        Assert.DoesNotContain("DbContext.EndpointDependencies.Add", source);
        Assert.DoesNotContain("DbContext.EndpointDependencies.Remove", source);
    }

    [Fact]
    public void DependencyTools_EnforceVisibilityBoundsTruncationAndCycleProtection()
    {
        var source = ReadWebSource("Services", "AiTools", "DependencyAiTools.cs");
        var limits = ReadWebSource("Services", "AiTools", "AiToolExecutionLimits.cs");
        Assert.Contains("GetVisibleEndpointIdsOrNullForAdminAsync", source);
        Assert.Contains("MaxDependencyEndpointsReturned", source);
        Assert.Contains("MaxDependencyPathsReturned", source);
        Assert.Contains("MaxDependencyTraversalDepth", source);
        Assert.Contains("MaxTopDependedOnEndpoints", source);
        Assert.Contains("Cycle encountered", source);
        Assert.Contains("current.Path.Contains", source);
        Assert.Contains("truncated = true", source);
        Assert.Contains("DefaultMaxDependencyEndpointsReturned = 100", limits);
        Assert.Contains("DefaultMaxDependencyPathsReturned = 100", limits);
        Assert.Contains("DefaultMaxDependencyTraversalDepth = 5", limits);
        Assert.Contains("DefaultMaxTopDependedOnEndpoints = 20", limits);
    }

    private static string ReadWebSource(params string[] path)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", Path.Combine(path));
        return File.ReadAllText(fullPath);
    }
}
