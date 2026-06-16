namespace PingMonitor.Web.Services.AiTools;

internal sealed class AiToolRegistry : IAiToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAiTool> _tools;

    public AiToolRegistry(IEnumerable<IAiTool> tools)
    {
        _tools = tools.ToDictionary(x => x.Definition.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<AiToolDefinition> GetDefinitions() => _tools.Values.Select(x => x.Definition).OrderBy(x => x.Name).ToArray();
    public bool IsRegistered(string name) => _tools.ContainsKey(name);

    public Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(call.Name, out var tool))
        {
            return Task.FromResult(new AiToolExecutionResult { Succeeded = false, ErrorMessage = "Unknown tool requested.", ContentJson = "{\"error\":\"unknown_tool\"}" });
        }

        return tool.ExecuteAsync(call, cancellationToken);
    }
}
