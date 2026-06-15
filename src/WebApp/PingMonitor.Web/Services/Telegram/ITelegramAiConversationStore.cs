using PingMonitor.Web.Services.AiChat;

namespace PingMonitor.Web.Services.Telegram;

public interface ITelegramAiConversationStore
{
    IReadOnlyList<AiChatMessageDto> GetHistory(string chatId, string userId);
    void AddTurn(string chatId, string userId, string userMessage, string assistantMessage);
    void Clear(string chatId, string userId);
}
