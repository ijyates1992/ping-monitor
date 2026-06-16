using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.AiMemory;

public sealed record AiUserMemoryDto(string MemoryId, string UserId, string MemoryType, string Content, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, DateTimeOffset? LastUsedAtUtc, int UseCount, bool Enabled);
public sealed record CreateAiUserMemoryCommand(string UserId, string MemoryType, string Content, string? Source, string? ConversationSource, string? CurrentUserMessage);
public sealed record SearchAiUserMemoriesQuery(string UserId, string? Query, int MaxResults = 10, bool MarkUsed = true);
public sealed record DeleteAiUserMemoryCommand(string UserId, string MemoryId);
public sealed record AiUserMemoryMutationResult(bool Succeeded, string? ErrorMessage, AiUserMemoryDto? Memory);

public interface IAiUserMemoryService
{
    Task<AiUserMemoryMutationResult> CreateAsync(CreateAiUserMemoryCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiUserMemoryDto>> SearchAsync(SearchAiUserMemoriesQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiUserMemoryDto>> ListAsync(string userId, CancellationToken cancellationToken);
    Task<AiUserMemoryMutationResult> DeleteAsync(DeleteAiUserMemoryCommand command, CancellationToken cancellationToken);
    string? ResolveUserId(ClaimsPrincipal? principal, string? applicationUserId);
}

internal sealed partial class AiUserMemoryService : IAiUserMemoryService
{
    private static readonly string[] ExplicitMemoryPhrases = ["remember that", "remember this", "from now on", "when i say", "call this", "i usually mean"];
    private static readonly string[] RejectedLiveTruthPhrases = ["currently down", "currently up", "rtt is", "packet loss is", "uptime is", "last check", "today", "right now", "is unhealthy"];
    private readonly PingMonitorDbContext _dbContext;

    public AiUserMemoryService(PingMonitorDbContext dbContext) => _dbContext = dbContext;

    public string? ResolveUserId(ClaimsPrincipal? principal, string? applicationUserId)
        => !string.IsNullOrWhiteSpace(applicationUserId) ? applicationUserId : principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public async Task<AiUserMemoryMutationResult> CreateAsync(CreateAiUserMemoryCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.UserId)) return Fail("A linked Ping Monitor user is required.");
        if (!HasExplicitMemoryIntent(command.CurrentUserMessage)) return Fail("Memory creation requires an explicit user request.");
        var content = command.Content.Trim();
        if (content.Length == 0) return Fail("Memory content is required.");
        if (content.Length > AiUserMemory.ContentMaxLength) return Fail($"Memory content must be {AiUserMemory.ContentMaxLength} characters or fewer.");
        if (LooksLikeSecret(content)) return Fail("Memory content appears to contain a secret, password, API key, or token.");
        if (LooksLikeLiveMonitoringTruth(content)) return Fail("Memory cannot store live monitoring state, current health, raw metrics, uptime, RTT, packet loss, or temporary incident status.");
        if (LooksLikeRawJson(content)) return Fail("Memory cannot store raw JSON or tool result dumps.");

        var normalized = Normalize(content);
        var duplicate = await _dbContext.AiUserMemories.AsNoTracking().AnyAsync(x => x.UserId == command.UserId && x.NormalizedContent == normalized && x.Enabled && x.DeletedAtUtc == null, cancellationToken);
        if (duplicate) return Fail("A near-identical memory already exists.");

        var memory = new AiUserMemory
        {
            AiUserMemoryId = Guid.NewGuid().ToString("N"),
            UserId = command.UserId,
            MemoryType = Enum.TryParse<AiUserMemoryType>(command.MemoryType, true, out var type) ? type : AiUserMemoryType.Other,
            Content = content,
            NormalizedContent = normalized,
            Source = string.IsNullOrWhiteSpace(command.Source) ? "AiTool" : command.Source.Trim(),
            CreatedFromConversationSource = command.ConversationSource,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Enabled = true
        };
        _dbContext.AiUserMemories.Add(memory);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new AiUserMemoryMutationResult(true, null, ToDto(memory));
    }

    public async Task<IReadOnlyList<AiUserMemoryDto>> SearchAsync(SearchAiUserMemoriesQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.UserId)) return [];
        var q = Normalize(query.Query ?? string.Empty);
        var memories = await _dbContext.AiUserMemories
            .Where(x => x.UserId == query.UserId && x.Enabled && x.DeletedAtUtc == null)
            .Where(x => q == string.Empty || x.NormalizedContent.Contains(q) || x.Content.Contains(query.Query!))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(query.MaxResults, 1, 10))
            .ToListAsync(cancellationToken);
        if (query.MarkUsed && memories.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var memory in memories) { memory.LastUsedAtUtc = now; memory.UseCount++; }
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return memories.Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyList<AiUserMemoryDto>> ListAsync(string userId, CancellationToken cancellationToken)
    {
        var memories = await _dbContext.AiUserMemories.AsNoTracking()
            .Where(x => x.UserId == userId && x.DeletedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return memories.Select(ToDto).ToArray();
    }

    public async Task<AiUserMemoryMutationResult> DeleteAsync(DeleteAiUserMemoryCommand command, CancellationToken cancellationToken)
    {
        var memory = await _dbContext.AiUserMemories.SingleOrDefaultAsync(x => x.AiUserMemoryId == command.MemoryId && x.UserId == command.UserId && x.DeletedAtUtc == null, cancellationToken);
        if (memory is null) return Fail("Memory was not found for the current user.");
        memory.Enabled = false;
        memory.DeletedAtUtc = DateTimeOffset.UtcNow;
        memory.UpdatedAtUtc = memory.DeletedAtUtc.Value;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new AiUserMemoryMutationResult(true, null, ToDto(memory));
    }

    private static bool HasExplicitMemoryIntent(string? message) => !string.IsNullOrWhiteSpace(message) && ExplicitMemoryPhrases.Any(x => message.Contains(x, StringComparison.OrdinalIgnoreCase));
    private static bool LooksLikeLiveMonitoringTruth(string content) => RejectedLiveTruthPhrases.Any(x => content.Contains(x, StringComparison.OrdinalIgnoreCase));
    private static bool LooksLikeSecret(string content) => SecretRegex().IsMatch(content);
    private static bool LooksLikeRawJson(string content) => content.TrimStart().StartsWith('{') || content.TrimStart().StartsWith('[');
    private static string Normalize(string value) => Regex.Replace(value.Trim().ToLowerInvariant(), "\\s+", " ");
    private static AiUserMemoryMutationResult Fail(string message) => new(false, message, null);
    private static AiUserMemoryDto ToDto(AiUserMemory x) => new(x.AiUserMemoryId, x.UserId, x.MemoryType.ToString(), x.Content, x.CreatedAtUtc, x.UpdatedAtUtc, x.LastUsedAtUtc, x.UseCount, x.Enabled);

    [GeneratedRegex("(?i)(password|api[_ -]?key|secret|token|bearer\\s+[a-z0-9._\\-]{12,}|sk-[a-z0-9]{12,})")]
    private static partial Regex SecretRegex();
}
