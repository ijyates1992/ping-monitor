using PingMonitor.Web.Services.AiChat;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AiUserMemorySliceTests
{
    [Fact]
    public void BuiltInPromptIncludesExplicitUserMemoryRules()
    {
        Assert.Contains("Only create a memory when the user clearly asks", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Memories are user-specific", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Live Ping Monitor tool data overrides memory", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Do not store current endpoint state", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("delete_user_memory", AiChatService.BuiltInSystemPrompt);
    }

    [Fact]
    public void MemoryServiceSourceContainsValidationAndIsolationGuards()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", "Services", "AiMemory", "AiUserMemoryService.cs"));
        Assert.Contains("Memory creation requires an explicit user request", source);
        Assert.Contains("remember that", source);
        Assert.Contains("password|api", source);
        Assert.Contains("currently down", source);
        Assert.Contains("x.UserId == query.UserId", source);
        Assert.Contains("x.UserId == command.UserId", source);
        Assert.Contains("DeletedAtUtc", source);
    }

    [Fact]
    public void MemoryToolsDeclareBoundedUserSpecificFunctionsWithoutSecrets()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", "Services", "AiTools", "UserMemoryAiTools.cs"));
        Assert.Contains("search_user_memories", source);
        Assert.Contains("remember_user_memory", source);
        Assert.Contains("delete_user_memory", source);
        Assert.Contains("AI memory is disabled", source);
        Assert.Contains("maxLength", source);
        Assert.DoesNotContain("runtime-secret", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartupGateDeclaresAiUserMemoriesSchema()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", "Services", "StartupGate", "StartupSchemaService.cs"));
        Assert.Contains("AiUserMemories", source);
        Assert.Contains("RequiredAiUserMemoryColumns", source);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `AiUserMemories`", source);
    }
}
