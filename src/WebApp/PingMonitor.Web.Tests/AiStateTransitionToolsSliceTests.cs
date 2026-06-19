using PingMonitor.Web.Services.AiChat;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AiStateTransitionToolsSliceTests
{
    [Fact]
    public void StateTransitionTool_IsRegisteredAndReadOnly()
    {
        var program = ReadWebSource("Program.cs");
        var source = ReadWebSource("Services", "AiTools", "SearchStateTransitionsAiTool.cs");

        Assert.Contains("SearchStateTransitionsAiTool", program);
        Assert.Contains("search_state_transitions", source);
        Assert.Contains("StateTransitions.AsNoTracking()", source);
        Assert.DoesNotContain("_dbContext.CheckResults", source);
        Assert.DoesNotContain("SaveChanges", source);
        Assert.DoesNotContain("_dbContext.StateTransitions.Add", source);
        Assert.DoesNotContain("_dbContext.StateTransitions.Remove", source);
    }

    [Fact]
    public void StateTransitionTool_DeclaresAllUtcFiltersAndSorts()
    {
        var source = ReadWebSource("Services", "AiTools", "SearchStateTransitionsAiTool.cs");

        Assert.Contains("[\"fromUtc\"]", source);
        Assert.Contains("[\"toUtc\"]", source);
        Assert.Contains("[\"endpointIds\"]", source);
        Assert.Contains("[\"endpointGroupIds\"]", source);
        Assert.Contains("[\"agentIds\"]", source);
        Assert.Contains("[\"fromStates\"]", source);
        Assert.Contains("[\"toStates\"]", source);
        Assert.Contains("timestamp_asc", source);
        Assert.Contains("timestamp_desc", source);
        Assert.Contains("explicit UTC timestamps ending in Z", source);
    }

    [Fact]
    public void StateTransitionTool_AppliesPermissionAndDetailFiltering()
    {
        var source = ReadWebSource("Services", "AiTools", "SearchStateTransitionsAiTool.cs");

        Assert.Contains("ResolveUserAsync", source);
        Assert.Contains("GetVisibleEndpointIdsOrNullForAdminAsync", source);
        Assert.Contains("scopedEndpointIds", source);
        Assert.Contains("Intersect(visibleEndpointIds", source);
        Assert.Contains("dependencyIds.Intersect(visibleEndpointIds", source);
        Assert.Contains("permissionFiltered = true", source);
    }

    [Fact]
    public void StateTransitionTool_BoundsRangeResultsDetailsAndTruncation()
    {
        var source = ReadWebSource("Services", "AiTools", "SearchStateTransitionsAiTool.cs");
        var limits = ReadWebSource("Services", "AiTools", "AiToolExecutionLimits.cs");

        Assert.Contains("HardMaxResults = 1000", source);
        Assert.Contains("HardMaxLookbackDays = 365", source);
        Assert.Contains("HardMaxEndpointDetails = 1000", source);
        Assert.Contains("range_too_wide", source);
        Assert.Contains("Take(resultLimit)", source);
        Assert.Contains("totalCount", source);
        Assert.Contains("Result exceeded configured AI state transition limit.", source);
        Assert.Contains("DefaultMaxStateTransitionSearchResults = 200", limits);
        Assert.Contains("DefaultMaxStateTransitionLookbackDays = 90", limits);
        Assert.Contains("DefaultMaxStateTransitionEndpointDetails = 200", limits);
    }

    [Fact]
    public void StateTransitionTool_PreservesMonitoringStateSemanticsAndDependencySafety()
    {
        var source = ReadWebSource("Services", "AiTools", "SearchStateTransitionsAiTool.cs");

        Assert.Contains("EndpointStateKind.Up", source);
        Assert.Contains("EndpointStateKind.Down", source);
        Assert.Contains("EndpointStateKind.Degraded", source);
        Assert.Contains("EndpointStateKind.Suppressed", source);
        Assert.Contains("EndpointStateKind.Unknown", source);
        Assert.Contains("SUPPRESSED is dependency-related", source);
        Assert.Contains("UNKNOWN may indicate agent/check visibility issues", source);
        Assert.Contains("Temporal correlation does not prove cause", source);
        Assert.Contains("directDownDependencyNames", source);
    }

    [Fact]
    public void StateTransitionTool_UsesSharedExecutionUserContext()
    {
        var toolModel = ReadWebSource("Services", "AiTools", "AiToolModels.cs");
        var visibility = ReadWebSource("Services", "AiTools", "EndpointMetricsAiTools.cs");

        Assert.Contains("ClaimsPrincipal? Principal", toolModel);
        Assert.Contains("string? ApplicationUserId", toolModel);
        Assert.Contains("if (call.Principal is not null)", visibility);
        Assert.Contains("call.ApplicationUserId", visibility);
    }

    [Fact]
    public void BuiltInPrompt_DirectsTimelineQuestionsToBulkTransitionLookup()
    {
        Assert.Contains("Use `search_state_transitions` for timeline questions", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Do not query endpoint history one endpoint at a time", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("State transitions are monitoring state changes, not raw ping results", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("SUPPRESSED is dependency-related", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("UNKNOWN may indicate agent/check visibility issues", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Temporal correlation does not prove cause", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("total downtime, longest outage, outage count", AiChatService.BuiltInSystemPrompt);
    }

    [Fact]
    public void AdminSettings_ExposeStateTransitionBoundsWithoutSchemaColumns()
    {
        var model = ReadWebSource("ViewModels", "Admin", "AiAssistantSettingsPageViewModel.cs");
        var controller = ReadWebSource("Controllers", "AdminAiAssistantSettingsController.cs");
        var view = ReadWebSource("Views", "AdminAiAssistantSettings", "Index.cshtml");

        foreach (var property in new[]
                 {
                     "MaxStateTransitionSearchResults",
                     "MaxStateTransitionLookbackDays",
                     "MaxStateTransitionEndpointDetails"
                 })
        {
            Assert.Contains(property, model);
            Assert.Contains(property, controller);
            Assert.Contains(property, view);
        }
    }

    private static string ReadWebSource(params string[] path)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", Path.Combine(path));
        return File.ReadAllText(fullPath);
    }
}
