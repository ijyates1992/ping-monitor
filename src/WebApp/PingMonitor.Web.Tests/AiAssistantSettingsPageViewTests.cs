using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AiAssistantSettingsPageViewTests
{
    [Fact]
    public void AdminAiAssistantSettingsView_GroupsSettingsIntoReadableSections()
    {
        var view = ReadWebFile("Views", "AdminAiAssistantSettings", "Index.cshtml");

        AssertInOrder(view,
            "Feature enablement",
            "Provider and model",
            "Tool calling and context limits",
            "Admin global prompt",
            "Safety notes");

        foreach (var heading in new[]
                 {
                     "Provider connection",
                     "Model behaviour",
                     "General tool execution",
                     "Endpoint and metrics tools",
                     "Historic state transition tools",
                     "Diagram tools",
                     "Dependency tools",
                     "Memory tools",
                     "Runtime tools"
                 })
        {
            Assert.Contains(heading, view);
        }

        Assert.Contains("class=\"grid two\"", view);
        Assert.Contains("@@media (min-width: 720px)", view);
        Assert.Contains("sub-card", view);
    }

    [Fact]
    public void AdminAiAssistantSettingsView_PlacesProviderTestWithProviderSettings()
    {
        var view = ReadWebFile("Views", "AdminAiAssistantSettings", "Index.cshtml");

        var providerSection = view.IndexOf("<h2>Provider and model</h2>", StringComparison.Ordinal);
        var toolSection = view.IndexOf("<h2>Tool calling and context limits</h2>", StringComparison.Ordinal);
        var testHeading = view.IndexOf("Test AI provider connection", StringComparison.Ordinal);
        var testButton = view.IndexOf("Test saved provider settings", StringComparison.Ordinal);

        Assert.True(providerSection >= 0);
        Assert.True(toolSection > providerSection);
        Assert.InRange(testHeading, providerSection, toolSection);
        Assert.InRange(testButton, providerSection, toolSection);
        Assert.Contains("Save changes before testing updated provider settings.", view);
        Assert.Contains("This does not send monitoring data, endpoint data, diagrams, dependencies, memories, or secrets.", view);
    }

    [Fact]
    public void AdminAiAssistantSettingsView_KeepsAllSettingsBoundAndApiKeyHidden()
    {
        var view = ReadWebFile("Views", "AdminAiAssistantSettings", "Index.cshtml");

        foreach (var property in new[]
                 {
                     "AssistantEnabled", "WebChatEnabled", "TelegramChatEnabled", "MemoryEnabled", "DebugLoggingEnabled",
                     "ProviderDisplayName", "ProviderType", "BaseUrl", "ApiKey", "ClearApiKey", "ModelName",
                     "RequestTimeoutSeconds", "MaxOutputTokens", "Temperature", "ToolCallingEnabled",
                     "MaxToolRounds", "MaxToolCallsPerRound", "MaxTotalToolResultCharacters", "MaxSingleToolResultCharacters",
                     "MaxEndpointSearchResults", "MaxEndpointMetricsSampleTailPoints", "MaxEndpointTransitionItems", "MaxEndpointFailureClusters",
                     "DefaultEndpointMetricsWindow", "MaximumEndpointMetricsWindow", "MaxStateTransitionSearchResults", "MaxStateTransitionLookbackDays", "MaxStateTransitionEndpointDetails",
                     "MaxDiagramListResults", "MaxDiagramNodeSearchResults", "MaxDiagramConnectionResults", "MaxFullDiagramNodesReturned", "MaxFullDiagramLinksReturned", "MaxDiagramToolResultCharacters", "MaxDiagramItemMetadataCharacters",
                     "MaxDependencyEndpointsReturned", "MaxDependencyPathsReturned", "MaxDependencyTraversalDepth", "MaxTopDependedOnEndpoints",
                     "MaxMemorySearchResults", "MaxMemoryContentCharacters", "MaxRuntimeLargestTablesReturned", "GlobalSystemPrompt"
                 })
        {
            Assert.Contains($"asp-for=\"{property}\"", view);
        }

        Assert.Contains("type=\"password\"", view);
        Assert.Contains("value=\"\"", view);
        Assert.Contains("Leave blank to keep the saved key. The saved key must never be displayed.", view);
    }

    [Fact]
    public void AdminAiAssistantSettingsView_AddsHelpTextForKeySafetyAndLimitSettings()
    {
        var view = ReadWebFile("Views", "AdminAiAssistantSettings", "Index.cshtml");

        foreach (var text in new[]
                 {
                     "Master switch for AI assistant features.",
                     "Friendly name shown in the admin UI.",
                     "Maximum number of tool-call cycles",
                     "Maximum endpoints returned when the assistant searches",
                     "Maximum bulk state transitions returned",
                     "Maximum dependency paths returned",
                     "Memories should not contain live monitoring truth.",
                     "Provider/API secrets are protected and must not be logged or displayed."
                 })
        {
            Assert.Contains(text, view);
        }
    }

    private static void AssertInOrder(string source, params string[] expected)
    {
        var previous = -1;
        foreach (var item in expected)
        {
            var index = source.IndexOf(item, StringComparison.Ordinal);
            Assert.True(index > previous, $"Expected '{item}' after index {previous}, but found {index}.");
            previous = index;
        }
    }

    private static string ReadWebFile(params string[] path)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", Path.Combine(path));
        return File.ReadAllText(fullPath);
    }
}
