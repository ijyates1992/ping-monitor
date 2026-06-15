namespace PingMonitor.Web.Services.AiChat;

public interface IAiChatService
{
    Task<AiChatResponse> SendAsync(AiChatRequest request, CancellationToken cancellationToken);
}
