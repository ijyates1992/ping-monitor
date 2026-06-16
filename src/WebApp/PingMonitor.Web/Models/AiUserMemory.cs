namespace PingMonitor.Web.Models;

public sealed class AiUserMemory
{
    public const int MemoryIdMaxLength = 64;
    public const int UserIdMaxLength = 255;
    public const int MemoryTypeMaxLength = 64;
    public const int ContentMaxLength = 1000;
    public const int NormalizedContentMaxLength = 1000;
    public const int SourceMaxLength = 64;

    public string AiUserMemoryId { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public AiUserMemoryType MemoryType { get; set; } = AiUserMemoryType.Other;
    public string Content { get; set; } = string.Empty;
    public string NormalizedContent { get; set; } = string.Empty;
    public string Source { get; set; } = "User";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAtUtc { get; set; }
    public int UseCount { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public string? CreatedFromConversationSource { get; set; }
}

public enum AiUserMemoryType
{
    UserPreference,
    EndpointAlias,
    NetworkAlias,
    LocationAlias,
    OperationalNote,
    AssistantInstruction,
    Other
}
