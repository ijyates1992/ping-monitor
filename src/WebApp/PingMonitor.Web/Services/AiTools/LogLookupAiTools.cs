using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.Identity;

namespace PingMonitor.Web.Services.AiTools;

internal abstract class LogLookupAiToolBase(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager) : IAiTool
{
    protected const int HardMaxResults = 500;
    protected const int HardMaxLookbackDays = 365;
    protected const int HardMaxContextWindowMinutes = 240;
    protected const int HardMaxMessageCharacters = 4000;
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex[] SecretPatterns =
    [
        new("(?i)(authorization\\s*[:=]\\s*bearer\\s+)[^\\s,;\\\"']+", RegexOptions.Compiled),
        new("(?i)(api[-_ ]?key\\s*[:=]\\s*)[^\\s,;\\\"']+", RegexOptions.Compiled),
        new("(?i)(password\\s*[:=]\\s*)[^\\s,;\\\"']+", RegexOptions.Compiled),
        new("(?i)(token\\s*[:=]\\s*)[^\\s,;\\\"']+", RegexOptions.Compiled),
        new("(?i)(secret\\s*[:=]\\s*)[^\\s,;\\\"']+", RegexOptions.Compiled),
        new("(?i)(connection\\s*string\\s*[:=]\\s*)[^\\r\\n]+", RegexOptions.Compiled),
        new("(?i)(server=.*;.*(?:password|pwd)=)[^;]+", RegexOptions.Compiled),
        new("\\b\\d{8,12}:[A-Za-z0-9_-]{20,}\\b", RegexOptions.Compiled)
    ];

    public abstract AiToolDefinition Definition { get; }
    public abstract Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken);

    protected async Task<(ApplicationUser? User, bool IsAdmin, IReadOnlyList<string>? VisibleEndpointIds, AiToolExecutionResult? Error)> ValidateAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        var user = await AiToolUserVisibility.ResolveUserAsync(call, userManager, cancellationToken);
        if (user is null) return (null, false, null, Error("unauthorized", "No application user was available for tool execution."));
        var isAdmin = await userManager.IsInRoleAsync(user, ApplicationRoles.Admin);
        var visibleEndpointIds = await AiToolUserVisibility.GetVisibleEndpointIdsOrNullForAdminAsync(dbContext, userManager, user, cancellationToken);
        return (user, isAdmin, visibleEndpointIds, null);
    }

    protected async Task<LogSearchResult> SearchAsync(LogSearchArgs args, AiToolCall call, bool isAdmin, IReadOnlyList<string>? visibleEndpointIds, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var lookbackDays = Math.Clamp(call.Limits.MaxLogLookupLookbackDays, 1, HardMaxLookbackDays);
        if (args.ToUtc - args.FromUtc > TimeSpan.FromDays(lookbackDays))
            return LogSearchResult.RangeError(now, args.FromUtc, args.ToUtc, $"The requested UTC range exceeds the configured maximum log lookup lookback window of {lookbackDays} days.");

        var maxMessageChars = Math.Clamp(call.Limits.MaxLogMessageDetailCharacters, 100, HardMaxMessageCharacters);
        var configuredLimit = Math.Clamp(call.Limits.MaxLogSearchResults, 1, HardMaxResults);
        var limit = Math.Min(args.MaxResults ?? configuredLimit, configuredLimit);
        var allowedCategories = NormalizeCategories(args.Categories, isAdmin);
        var levels = args.Levels.Select(Normalize).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = new List<LogRow>();
        var total = 0;
        var redacted = false;

        if (allowedCategories.Overlaps(["event", "endpoint", "agent", "system"]))
        {
            var q = dbContext.EventLogs.AsNoTracking().Where(x => x.OccurredAtUtc >= args.FromUtc && x.OccurredAtUtc < args.ToUtc);
            if (visibleEndpointIds is not null) q = q.Where(x => x.EndpointId == null || visibleEndpointIds.Contains(x.EndpointId));
            if (!string.IsNullOrWhiteSpace(args.EntityType) && !string.IsNullOrWhiteSpace(args.EntityId))
            {
                if (args.EntityType.Equals("Endpoint", StringComparison.OrdinalIgnoreCase)) q = q.Where(x => x.EndpointId == args.EntityId);
                else if (args.EntityType.Equals("Agent", StringComparison.OrdinalIgnoreCase)) q = q.Where(x => x.AgentId == args.EntityId);
            }
            if (!string.IsNullOrWhiteSpace(args.SearchText)) q = q.Where(x => x.Message.Contains(args.SearchText) || (x.DetailsJson != null && x.DetailsJson.Contains(args.SearchText)) || x.EventType.Contains(args.SearchText));
            if (levels.Count > 0)
            {
                var includeInfo = levels.Contains("info");
                var includeWarning = levels.Contains("warning");
                var includeError = levels.Contains("error") || levels.Contains("critical");
                q = q.Where(x => (includeInfo && x.Severity == EventSeverity.Info) || (includeWarning && x.Severity == EventSeverity.Warning) || (includeError && x.Severity == EventSeverity.Error));
            }
            if (!allowedCategories.Contains("event"))
            {
                var includeEndpoint = allowedCategories.Contains("endpoint");
                var includeAgent = allowedCategories.Contains("agent");
                var includeSystem = allowedCategories.Contains("system");
                q = q.Where(x => (includeEndpoint && x.EventCategory == EventCategory.Endpoint) || (includeAgent && x.EventCategory == EventCategory.Agent) || (includeSystem && (x.EventCategory == EventCategory.System || x.EventCategory == EventCategory.Security)));
            }
            total += await q.CountAsync(cancellationToken);
            var eventRows = await q.Select(x => new { x.OccurredAtUtc, x.EventCategory, x.Severity, x.EventType, x.Message, x.DetailsJson, x.EndpointId, x.AgentId, x.EventLogId }).ToArrayAsync(cancellationToken);
            foreach (var x in eventRows)
            {
                rows.Add(ToRow(x.OccurredAtUtc, x.EventCategory.ToString().ToLowerInvariant(), x.Severity.ToString().ToLowerInvariant(), x.EventType, x.Message, x.DetailsJson, x.EndpointId is not null ? "Endpoint" : x.AgentId is not null ? "Agent" : null, x.EndpointId ?? x.AgentId, x.EventLogId, maxMessageChars, ref redacted));
            }
        }

        if (isAdmin && allowedCategories.Contains("auth"))
        {
            var q = dbContext.SecurityAuthLogs.AsNoTracking().Where(x => x.OccurredAtUtc >= args.FromUtc && x.OccurredAtUtc < args.ToUtc);
            if (!string.IsNullOrWhiteSpace(args.EntityType) && !string.IsNullOrWhiteSpace(args.EntityId) && args.EntityType.Equals("Agent", StringComparison.OrdinalIgnoreCase)) q = q.Where(x => x.AgentId == args.EntityId);
            if (!string.IsNullOrWhiteSpace(args.SearchText)) q = q.Where(x => x.SubjectIdentifier.Contains(args.SearchText) || (x.FailureReason != null && x.FailureReason.Contains(args.SearchText)) || (x.DetailsJson != null && x.DetailsJson.Contains(args.SearchText)));
            if (levels.Count > 0)
            {
                var includeInfo = levels.Contains("info");
                var includeWarning = levels.Contains("warning") || levels.Contains("error") || levels.Contains("critical");
                q = q.Where(x => (includeInfo && x.Success) || (includeWarning && !x.Success));
            }
            total += await q.CountAsync(cancellationToken);
            var authRows = await q.Select(x => new { x.OccurredAtUtc, x.AuthType, x.Success, x.SubjectIdentifier, x.FailureReason, x.AgentId, x.SecurityAuthLogId, x.DetailsJson }).ToArrayAsync(cancellationToken);
            foreach (var x in authRows)
            {
                rows.Add(ToRow(x.OccurredAtUtc, "auth", x.Success ? "info" : "warning", x.AuthType.ToString(), x.Success ? $"Successful {x.AuthType} authentication for {x.SubjectIdentifier}." : $"Failed {x.AuthType} authentication for {x.SubjectIdentifier}.", x.FailureReason ?? x.DetailsJson, x.AgentId is null ? null : "Agent", x.AgentId, x.SecurityAuthLogId, maxMessageChars, ref redacted));
            }
        }

        rows = args.Sort == "timestamp_asc" ? rows.OrderBy(x => x.TimestampUtc).ToList() : rows.OrderByDescending(x => x.TimestampUtc).ToList();
        var returned = rows.Take(limit).ToArray();
        return new LogSearchResult(now, args.FromUtc, args.ToUtc, true, redacted, total > returned.Length, total > returned.Length ? "Result exceeded configured AI log lookup limit." : null, returned.Length, total, returned);
    }

    private static HashSet<string> NormalizeCategories(string[] categories, bool isAdmin)
    {
        var requested = categories.Length == 0 ? ["event", "endpoint", "agent", "system", "auth"] : categories.Select(Normalize).ToArray();
        var allowed = new HashSet<string>(requested.Where(x => x is "event" or "endpoint" or "agent" or "system" or "auth"), StringComparer.OrdinalIgnoreCase);
        if (!isAdmin) allowed.Remove("auth");
        return allowed;
    }

    protected static bool TryReadArgs(string json, out JsonElement root, out AiToolExecutionResult? error)
    {
        try { using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); root = doc.RootElement.Clone(); error = root.ValueKind == JsonValueKind.Object ? null : Error("invalid_arguments", "Arguments must be a JSON object."); return error is null; }
        catch (JsonException) { root = default; error = Error("invalid_arguments", "Arguments must be valid JSON."); return false; }
    }

    protected static AiToolExecutionResult JsonResult(object result, int max) { var json = JsonSerializer.Serialize(result, JsonOptions); return new AiToolExecutionResult { Succeeded = true, ContentJson = json.Length <= max ? json : JsonSerializer.Serialize(new { truncated = true, reason = "Result exceeded configured AI tool limit.", originalCharacterCount = json.Length, maxCharacters = max }, JsonOptions) }; }
    protected static AiToolExecutionResult Error(string code, string message) => new() { Succeeded = false, ErrorMessage = message, ContentJson = JsonSerializer.Serialize(new { error = code, message }, JsonOptions) };
    protected static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
    protected static DateTimeOffset ReadDate(JsonElement root, string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(p.GetString(), out var value) ? value.ToUniversalTime() : default;
    protected static string[] ReadStringArray(JsonElement root, string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0).Take(20).ToArray() : [];
    protected static string? ReadString(JsonElement root, string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()?.Trim() : null;
    protected static int? ReadInt(JsonElement root, string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var value) ? value : null;

    private static LogRow ToRow(DateTimeOffset timestamp, string category, string level, string source, string? message, string? details, string? entityType, string? entityId, string correlationId, int maxChars, ref bool redacted)
    {
        var sanitizedMessage = Sanitize(Truncate(message, maxChars), ref redacted) ?? string.Empty;
        var sanitizedDetails = Sanitize(Truncate(details, maxChars), ref redacted);
        return new LogRow(timestamp, category, level, source, sanitizedMessage, entityType, entityId, null, sanitizedDetails, correlationId);
    }

    private static string? Truncate(string? value, int maxChars) => value is null ? null : value.Length <= maxChars ? value : value[..maxChars];
    private static string? Sanitize(string? value, ref bool redacted)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var current = value;
        foreach (var pattern in SecretPatterns)
        {
            var next = pattern.Replace(current, m => m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value + "[redacted]" : "[redacted]");
            if (!ReferenceEquals(next, current) && next != current) redacted = true;
            current = next;
        }
        return current;
    }

    protected sealed record LogSearchArgs(DateTimeOffset FromUtc, DateTimeOffset ToUtc, string[] Categories, string[] Levels, string? EntityType, string? EntityId, string? SearchText, int? MaxResults, string Sort);
    protected sealed record LogSearchResult(DateTimeOffset GeneratedAtUtc, DateTimeOffset FromUtc, DateTimeOffset ToUtc, bool PermissionFiltered, bool Redacted, bool Truncated, string? Reason, int ReturnedCount, int TotalCount, IReadOnlyList<LogRow> Logs)
    { public static LogSearchResult RangeError(DateTimeOffset now, DateTimeOffset from, DateTimeOffset to, string reason) => new(now, from, to, true, false, true, reason, 0, 0, []); }
    protected sealed record LogRow(DateTimeOffset TimestampUtc, string Category, string Level, string Source, string Message, string? EntityType, string? EntityId, string? EntityName, string? Details, string? CorrelationId);
}

internal sealed class SearchLogsAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager) : LogLookupAiToolBase(dbContext, userManager)
{
    public override AiToolDefinition Definition { get; } = new() { Name = "search_logs", Description = "Search bounded, structured DB-backed application event/auth logs for a UTC time range. Use for application events, agent events, auth failures, security events, updater/system events, notification-related event messages, AI debug event messages, and errors around an incident. This is read-only and never exposes raw filesystem logs.", MaxResultCharacters = 30000, Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["fromUtc"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" }, ["toUtc"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" }, ["categories"] = StringArray(), ["levels"] = StringArray(), ["entityType"] = new JsonObject { ["type"] = new JsonArray("string", "null") }, ["entityId"] = new JsonObject { ["type"] = new JsonArray("string", "null") }, ["searchText"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["maxLength"] = 200 }, ["maxResults"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = HardMaxResults }, ["sort"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("timestamp_asc", "timestamp_desc") } }, ["required"] = new JsonArray("fromUtc", "toUtc"), ["additionalProperties"] = false } };
    public override async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!TryReadArgs(call.ArgumentsJson, out var root, out var error)) return error!;
        var args = new LogSearchArgs(ReadDate(root, "fromUtc"), ReadDate(root, "toUtc"), ReadStringArray(root, "categories"), ReadStringArray(root, "levels"), ReadString(root, "entityType"), ReadString(root, "entityId"), ReadString(root, "searchText"), ReadInt(root, "maxResults"), ReadString(root, "sort") == "timestamp_asc" ? "timestamp_asc" : "timestamp_desc");
        if (args.FromUtc == default || args.ToUtc == default || args.ToUtc <= args.FromUtc) return Error("invalid_arguments", "fromUtc and toUtc are required UTC timestamps and toUtc must be after fromUtc.");
        var validation = await ValidateAsync(call, cancellationToken); if (validation.Error is not null) return validation.Error;
        var result = await SearchAsync(args, call, validation.IsAdmin, validation.VisibleEndpointIds, cancellationToken);
        return JsonResult(result, Math.Min(Definition.MaxResultCharacters, call.Limits.MaxSingleToolResultCharacters));
    }
    private static JsonObject StringArray() => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["maxItems"] = 20 };
}

internal sealed class GetLogContextAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager) : LogLookupAiToolBase(dbContext, userManager)
{
    public override AiToolDefinition Definition { get; } = new() { Name = "get_log_context", Description = "Return bounded, structured DB-backed log entries around a specific UTC timestamp, optionally scoped to an endpoint or agent. Use with state transition/dependency/diagram tools when investigating incidents. This is read-only and never exposes raw filesystem logs.", MaxResultCharacters = 30000, Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["aroundUtc"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" }, ["windowMinutesBefore"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = HardMaxContextWindowMinutes }, ["windowMinutesAfter"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = HardMaxContextWindowMinutes }, ["categories"] = StringArray(), ["entityType"] = new JsonObject { ["type"] = new JsonArray("string", "null") }, ["entityId"] = new JsonObject { ["type"] = new JsonArray("string", "null") }, ["maxResults"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = HardMaxResults } }, ["required"] = new JsonArray("aroundUtc"), ["additionalProperties"] = false } };
    public override async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!TryReadArgs(call.ArgumentsJson, out var root, out var error)) return error!;
        var around = ReadDate(root, "aroundUtc"); if (around == default) return Error("invalid_arguments", "aroundUtc is required.");
        var maxWindow = Math.Clamp(call.Limits.MaxLogContextWindowMinutes, 1, HardMaxContextWindowMinutes);
        var before = Math.Clamp(ReadInt(root, "windowMinutesBefore") ?? 15, 0, maxWindow);
        var after = Math.Clamp(ReadInt(root, "windowMinutesAfter") ?? 15, 0, maxWindow);
        var args = new LogSearchArgs(around.AddMinutes(-before), around.AddMinutes(after), ReadStringArray(root, "categories"), [], ReadString(root, "entityType"), ReadString(root, "entityId"), null, ReadInt(root, "maxResults"), "timestamp_asc");
        var validation = await ValidateAsync(call, cancellationToken); if (validation.Error is not null) return validation.Error;
        var result = await SearchAsync(args, call, validation.IsAdmin, validation.VisibleEndpointIds, cancellationToken);
        var payload = new { result.GeneratedAtUtc, aroundUtc = around, windowMinutesBefore = before, windowMinutesAfter = after, result.FromUtc, result.ToUtc, result.PermissionFiltered, result.Redacted, result.Truncated, result.Reason, result.ReturnedCount, result.TotalCount, logs = result.Logs, byCategory = result.Logs.GroupBy(x => x.Category).ToDictionary(x => x.Key, x => x.Count()), limitations = new[] { "Log context is bounded by configured AI log lookup limits.", "Redacted fields are replaced with [redacted].", "Logs show recorded facts; temporal correlation does not prove root cause." } };
        return JsonResult(payload, Math.Min(Definition.MaxResultCharacters, call.Limits.MaxSingleToolResultCharacters));
    }
    private static JsonObject StringArray() => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["maxItems"] = 20 };
}
