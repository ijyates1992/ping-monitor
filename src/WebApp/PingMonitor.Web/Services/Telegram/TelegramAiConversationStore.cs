using Microsoft.Extensions.Caching.Memory;
using PingMonitor.Web.Services.AiChat;

namespace PingMonitor.Web.Services.Telegram;

public sealed class TelegramAiConversationStore : ITelegramAiConversationStore
{
    public const int MaxTurns = 10;
    public const int MaxMessageCharacters = 4000;
    public const int MaxHistoryCharacters = 20000;
    private static readonly TimeSpan SlidingTtl = TimeSpan.FromHours(6);
    private readonly IMemoryCache _cache;

    public TelegramAiConversationStore(IMemoryCache cache) => _cache = cache;

    public IReadOnlyList<AiChatMessageDto> GetHistory(string chatId, string userId)
    {
        return _cache.TryGetValue(Key(chatId, userId), out List<AiChatMessageDto>? history) && history is not null
            ? history.Select(x => new AiChatMessageDto { Role = x.Role, Content = x.Content }).ToList()
            : [];
    }

    public void AddTurn(string chatId, string userId, string userMessage, string assistantMessage)
    {
        var history = GetHistory(chatId, userId).ToList();
        history.Add(new AiChatMessageDto { Role = "user", Content = Truncate(userMessage.Trim(), MaxMessageCharacters) });
        history.Add(new AiChatMessageDto { Role = "assistant", Content = Truncate(assistantMessage.Trim(), MaxMessageCharacters) });

        history = history.TakeLast(MaxTurns * 2).ToList();
        while (history.Sum(x => x.Content.Length) > MaxHistoryCharacters && history.Count > 2)
        {
            history.RemoveAt(0);
        }

        _cache.Set(Key(chatId, userId), history, new MemoryCacheEntryOptions { SlidingExpiration = SlidingTtl });
    }

    public void Clear(string chatId, string userId) => _cache.Remove(Key(chatId, userId));

    private static string Key(string chatId, string userId) => $"telegram-ai:{chatId}:{userId}";
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
