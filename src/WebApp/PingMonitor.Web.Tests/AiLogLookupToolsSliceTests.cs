using PingMonitor.Web.Services.AiChat;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AiLogLookupToolsSliceTests
{
    [Fact]
    public void LogLookupTools_AreRegisteredReadOnlyAndUseStructuredStores()
    {
        var program = ReadWebSource("Program.cs");
        var source = ReadWebSource("Services", "AiTools", "LogLookupAiTools.cs");

        Assert.Contains("SearchLogsAiTool", program);
        Assert.Contains("GetLogContextAiTool", program);
        Assert.Contains("search_logs", source);
        Assert.Contains("get_log_context", source);
        Assert.Contains("EventLogs.AsNoTracking()", source);
        Assert.Contains("SecurityAuthLogs.AsNoTracking()", source);
        Assert.Contains("never exposes raw filesystem logs", source);
        Assert.DoesNotContain("File.Read", source);
        Assert.DoesNotContain("Directory.", source);
        Assert.DoesNotContain("SaveChanges", source);
        Assert.DoesNotContain("EventLogs.Add", source);
        Assert.DoesNotContain("SecurityAuthLogs.Add", source);
        Assert.DoesNotContain("EventLogs.Remove", source);
        Assert.DoesNotContain("SecurityAuthLogs.Remove", source);
    }

    [Fact]
    public void SearchLogs_DeclaresExpectedFiltersAndSorts()
    {
        var source = ReadWebSource("Services", "AiTools", "LogLookupAiTools.cs");

        foreach (var token in new[] { "fromUtc", "toUtc", "categories", "levels", "entityType", "entityId", "searchText", "maxResults", "timestamp_asc", "timestamp_desc" })
        {
            Assert.Contains(token, source);
        }
    }

    [Fact]
    public void LogLookup_AppliesAccessControlAndAdminOnlyAuthLogs()
    {
        var source = ReadWebSource("Services", "AiTools", "LogLookupAiTools.cs");

        Assert.Contains("ResolveUserAsync", source);
        Assert.Contains("GetVisibleEndpointIdsOrNullForAdminAsync", source);
        Assert.Contains("visibleEndpointIds.Contains", source);
        Assert.Contains("IsInRoleAsync(user, ApplicationRoles.Admin)", source);
        Assert.Contains("if (!isAdmin) allowed.Remove(\"auth\")", source);
        Assert.Contains("if (isAdmin && allowedCategories.Contains(\"auth\"))", source);
        Assert.Contains("permissionFiltered", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LogLookup_BoundsRangesWindowsResultsAndMessageSize()
    {
        var source = ReadWebSource("Services", "AiTools", "LogLookupAiTools.cs");
        var limits = ReadWebSource("Services", "AiTools", "AiToolExecutionLimits.cs");

        Assert.Contains("HardMaxResults = 500", source);
        Assert.Contains("HardMaxLookbackDays = 365", source);
        Assert.Contains("HardMaxContextWindowMinutes = 240", source);
        Assert.Contains("MaxLogSearchResults", limits);
        Assert.Contains("DefaultMaxLogSearchResults = 100", limits);
        Assert.Contains("DefaultMaxLogContextWindowMinutes = 60", limits);
        Assert.Contains("DefaultMaxLogLookupLookbackDays = 30", limits);
        Assert.Contains("DefaultMaxLogMessageDetailCharacters = 1000", limits);
        Assert.Contains("Result exceeded configured AI log lookup limit.", source);
    }

    [Fact]
    public void LogLookup_RedactsSensitiveValues()
    {
        var source = ReadWebSource("Services", "AiTools", "LogLookupAiTools.cs");

        foreach (var token in new[] { "authorization", "api[-_ ]?key", "password", "token", "secret", "connection", "[redacted]", "Redacted" })
        {
            Assert.Contains(token, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Prompt_GuidesModelToUseLogToolsAndRespectLimitations()
    {
        Assert.Contains("Use log lookup tools for questions about application events", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Use `search_logs` for bounded log/event searches", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Use `get_log_context` for logs around a specific incident time", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Use state transition tools for monitoring state timelines", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Do not claim logs prove root cause", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("If log results are truncated or redacted", AiChatService.BuiltInSystemPrompt);
    }

    private static string ReadWebSource(params string[] path)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", Path.Combine(path));
        return File.ReadAllText(fullPath);
    }
}
