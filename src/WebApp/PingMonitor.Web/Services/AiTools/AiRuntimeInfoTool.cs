using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.Identity;

namespace PingMonitor.Web.Services.AiTools;

internal sealed class AiRuntimeInfoTool : IAiTool
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IAiRuntimeInfoService _runtimeInfoService;
    private readonly UserManager<ApplicationUser> _userManager;

    public AiRuntimeInfoTool(IAiRuntimeInfoService runtimeInfoService, UserManager<ApplicationUser> userManager)
    {
        _runtimeInfoService = runtimeInfoService;
        _userManager = userManager;
    }

    public AiToolDefinition Definition { get; } = new()
    {
        Name = "get_application_runtime_info",
        Description = "Get read-only Ping Monitor runtime and build information for the current application instance, including app version, assembly version, schema version, database provider/size where allowed, runtime environment, startup gate status, and build/update metadata where available. Use this for questions about the running app instance, versioning, database size, schema/build status, and deployment/runtime health.",
        MaxResultCharacters = 16000,
        Parameters = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["includeDatabase"] = new JsonObject { ["type"] = "boolean", ["description"] = "Whether to include database provider, schema, and approximate database size when the current user is allowed to see it." },
                ["includeEnvironment"] = new JsonObject { ["type"] = "boolean", ["description"] = "Whether to include safe runtime environment details when the current user is allowed to see them." },
                ["includeBuild"] = new JsonObject { ["type"] = "boolean", ["description"] = "Whether to include safe build/version/release metadata when available." }
            },
            ["required"] = new JsonArray(),
            ["additionalProperties"] = false
        }
    };

    public async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!TryReadArguments(call.ArgumentsJson, out var includeDatabase, out var includeEnvironment, out var includeBuild, out var error)) return error!;

        var user = await AiToolUserVisibility.ResolveUserAsync(call, _userManager, cancellationToken);
        if (user is null) return Error("unauthorized", "No application user was available for tool execution.");

        var isAdmin = await _userManager.IsInRoleAsync(user, ApplicationRoles.Admin);
        var result = await _runtimeInfoService.GetRuntimeInfoAsync(new AiRuntimeInfoRequest { IsAdmin = isAdmin, IncludeDatabase = includeDatabase, IncludeEnvironment = includeEnvironment, IncludeBuild = includeBuild }, cancellationToken);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return new AiToolExecutionResult { Succeeded = true, ContentJson = json.Length <= Definition.MaxResultCharacters ? json : json[..Definition.MaxResultCharacters] };
    }

    private static bool TryReadArguments(string json, out bool includeDatabase, out bool includeEnvironment, out bool includeBuild, out AiToolExecutionResult? error)
    {
        includeDatabase = true;
        includeEnvironment = true;
        includeBuild = true;
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { error = Error("invalid_arguments", "Arguments must be a JSON object."); return false; }
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name is not ("includeDatabase" or "includeEnvironment" or "includeBuild")) { error = Error("invalid_arguments", "Unsupported argument."); return false; }
                if (prop.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False) { error = Error("invalid_arguments", $"{prop.Name} must be a boolean."); return false; }
                if (prop.Name == "includeDatabase") includeDatabase = prop.Value.GetBoolean();
                if (prop.Name == "includeEnvironment") includeEnvironment = prop.Value.GetBoolean();
                if (prop.Name == "includeBuild") includeBuild = prop.Value.GetBoolean();
            }
            return true;
        }
        catch (JsonException)
        {
            error = Error("invalid_arguments", "Arguments must be valid JSON.");
            return false;
        }
    }

    private static AiToolExecutionResult Error(string code, string message) => new() { Succeeded = false, ErrorMessage = message, ContentJson = JsonSerializer.Serialize(new { error = code, message }, JsonOptions) };
}
