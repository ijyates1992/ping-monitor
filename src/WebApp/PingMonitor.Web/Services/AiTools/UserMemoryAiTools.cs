using System.Text.Json;
using System.Text.Json.Nodes;
using PingMonitor.Web.Services.AiMemory;

namespace PingMonitor.Web.Services.AiTools;

internal abstract class UserMemoryAiToolBase
{
    protected readonly IAiUserMemoryService MemoryService;
    protected readonly IAiAssistantSettingsService SettingsService;
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected UserMemoryAiToolBase(IAiUserMemoryService memoryService, IAiAssistantSettingsService settingsService)
    {
        MemoryService = memoryService;
        SettingsService = settingsService;
    }

    protected async Task<(bool ok, string? userId, AiToolExecutionResult? result)> ValidateAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        var settings = await SettingsService.GetCurrentAsync(cancellationToken);
        if (!settings.MemoryEnabled)
        {
            return (false, null, Error("memory_disabled", "AI memory is disabled."));
        }
        var userId = MemoryService.ResolveUserId(call.Principal, call.ApplicationUserId);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return (false, null, Error("user_required", "A linked Ping Monitor user is required."));
        }
        return (true, userId, null);
    }

    protected static AiToolExecutionResult Ok(object payload) => new() { Succeeded = true, ContentJson = JsonSerializer.Serialize(payload, JsonOptions) };
    protected static AiToolExecutionResult Error(string code, string message) => new() { Succeeded = false, ErrorMessage = message, ContentJson = JsonSerializer.Serialize(new { error = code, message }, JsonOptions) };
}

internal sealed class SearchUserMemoriesAiTool : UserMemoryAiToolBase, IAiTool
{
    public SearchUserMemoriesAiTool(IAiUserMemoryService memoryService, IAiAssistantSettingsService settingsService) : base(memoryService, settingsService) { }
    public AiToolDefinition Definition { get; } = new()
    {
        Name = "search_user_memories",
        Description = "Search the current user's AI memories for relevant preferences, aliases, and context. Live Ping Monitor tool data overrides memory.",
        Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Text to search for, such as an alias or preference phrase." } }, ["required"] = new JsonArray("query") }
    };
    public async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(call, cancellationToken); if (!validation.ok) return validation.result!;
        var args = JsonSerializer.Deserialize<SearchArgs>(call.ArgumentsJson, JsonOptions) ?? new SearchArgs();
        var matches = await MemoryService.SearchAsync(new SearchAiUserMemoriesQuery(validation.userId!, args.Query, 10), cancellationToken);
        return Ok(new { matches = matches.Select(x => new { memoryId = x.MemoryId, memoryType = x.MemoryType, content = x.Content, createdAtUtc = x.CreatedAtUtc, lastUsedAtUtc = x.LastUsedAtUtc }) });
    }
    private sealed class SearchArgs { public string? Query { get; set; } }
}

internal sealed class RememberUserMemoryAiTool : UserMemoryAiToolBase, IAiTool
{
    public RememberUserMemoryAiTool(IAiUserMemoryService memoryService, IAiAssistantSettingsService settingsService) : base(memoryService, settingsService) { }
    public AiToolDefinition Definition { get; } = new()
    {
        Name = "remember_user_memory",
        Description = "Create a user-specific memory only when the current user explicitly asks to remember something. Do not store secrets or live monitoring truth.",
        Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["memoryType"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("UserPreference", "EndpointAlias", "NetworkAlias", "LocationAlias", "OperationalNote", "AssistantInstruction", "Other") }, ["content"] = new JsonObject { ["type"] = "string", ["maxLength"] = 1000 } }, ["required"] = new JsonArray("memoryType", "content") }
    };
    public async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(call, cancellationToken); if (!validation.ok) return validation.result!;
        var args = JsonSerializer.Deserialize<CreateArgs>(call.ArgumentsJson, JsonOptions) ?? new CreateArgs();
        var result = await MemoryService.CreateAsync(new CreateAiUserMemoryCommand(validation.userId!, args.MemoryType ?? "Other", args.Content ?? string.Empty, "AiTool", call.ConversationSource, call.CurrentUserMessage), cancellationToken);
        return result.Succeeded ? Ok(new { memoryId = result.Memory!.MemoryId, memoryType = result.Memory.MemoryType, content = result.Memory.Content }) : Error("memory_rejected", result.ErrorMessage ?? "Memory was rejected.");
    }
    private sealed class CreateArgs { public string? MemoryType { get; set; } public string? Content { get; set; } }
}

internal sealed class DeleteUserMemoryAiTool : UserMemoryAiToolBase, IAiTool
{
    public DeleteUserMemoryAiTool(IAiUserMemoryService memoryService, IAiAssistantSettingsService settingsService) : base(memoryService, settingsService) { }
    public AiToolDefinition Definition { get; } = new()
    {
        Name = "delete_user_memory",
        Description = "Soft-delete one of the current user's own AI memories.",
        Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["memoryId"] = new JsonObject { ["type"] = "string" } }, ["required"] = new JsonArray("memoryId") }
    };
    public async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(call, cancellationToken); if (!validation.ok) return validation.result!;
        var args = JsonSerializer.Deserialize<DeleteArgs>(call.ArgumentsJson, JsonOptions) ?? new DeleteArgs();
        var result = await MemoryService.DeleteAsync(new DeleteAiUserMemoryCommand(validation.userId!, args.MemoryId ?? string.Empty), cancellationToken);
        return result.Succeeded ? Ok(new { deleted = true, memoryId = result.Memory!.MemoryId }) : Error("memory_not_deleted", result.ErrorMessage ?? "Memory was not deleted.");
    }
    private sealed class DeleteArgs { public string? MemoryId { get; set; } }
}
