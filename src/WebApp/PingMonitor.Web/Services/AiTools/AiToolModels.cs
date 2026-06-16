using System.Security.Claims;
using System.Text.Json.Nodes;
using PingMonitor.Web.Services.AiProviders;

namespace PingMonitor.Web.Services.AiTools;

public sealed class AiToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonObject Parameters { get; init; }
    public int MaxResultCharacters { get; init; } = 12000;

    public AiProviderToolDefinition ToProviderDefinition() => new()
    {
        Type = "function",
        Function = new AiProviderFunctionDefinition { Name = Name, Description = Description, Parameters = Parameters }
    };
}

public sealed class AiToolCall
{
    public required string Name { get; init; }
    public string ArgumentsJson { get; init; } = "{}";
    public ClaimsPrincipal? Principal { get; init; }
    public string? ApplicationUserId { get; init; }
    public string? CurrentUserMessage { get; init; }
    public string? ConversationSource { get; init; }
}

public sealed class AiToolExecutionResult
{
    public bool Succeeded { get; init; }
    public required string ContentJson { get; init; }
    public string? ErrorMessage { get; init; }
}

public interface IAiTool
{
    AiToolDefinition Definition { get; }
    Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken);
}

public interface IAiToolRegistry
{
    IReadOnlyList<AiToolDefinition> GetDefinitions();
    bool IsRegistered(string name);
    Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken);
}
