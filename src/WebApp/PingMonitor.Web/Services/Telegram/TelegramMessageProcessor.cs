using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Services.AiChat;

namespace PingMonitor.Web.Services.Telegram;

internal sealed partial class TelegramMessageProcessor : ITelegramMessageProcessor
{
    private const int TelegramMessageLimit = 4096;
    private const int MaxInboundAiMessageCharacters = 4000;
    private const string UnlinkedMessage = "Your Telegram account is not linked to a Ping Monitor user yet.";
    private readonly ITelegramLinkService _telegramLinkService;
    private readonly INotificationSettingsService _notificationSettingsService;
    private readonly IAiAssistantSettingsService _aiSettingsService;
    private readonly IAiChatService _aiChatService;
    private readonly ITelegramAiConversationStore _conversationStore;
    private readonly PingMonitorDbContext _dbContext;

    public TelegramMessageProcessor(
        ITelegramLinkService telegramLinkService,
        INotificationSettingsService notificationSettingsService,
        IAiAssistantSettingsService aiSettingsService,
        IAiChatService aiChatService,
        ITelegramAiConversationStore conversationStore,
        PingMonitorDbContext dbContext)
    {
        _telegramLinkService = telegramLinkService;
        _notificationSettingsService = notificationSettingsService;
        _aiSettingsService = aiSettingsService;
        _aiChatService = aiChatService;
        _conversationStore = conversationStore;
        _dbContext = dbContext;
    }

    public async Task<TelegramMessageProcessingResult> ProcessAsync(TelegramInboundMessage inboundMessage, CancellationToken cancellationToken)
    {
        var text = inboundMessage.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return Ignored("No text content.");
        }

        var codeMatch = CodeRegex().Match(text);
        if (codeMatch.Success)
        {
            return await ConsumeLinkCodeAsync(inboundMessage, codeMatch.Groups[1].Value, cancellationToken);
        }

        if (IsKnownCommand(text))
        {
            if (IsClearAiCommand(text))
            {
                return await ClearAiAsync(inboundMessage, cancellationToken);
            }

            return Ignored("Known Telegram command is handled by existing command routing.");
        }

        if (!string.Equals(inboundMessage.ChatType, "private", StringComparison.OrdinalIgnoreCase))
        {
            return Ignored("AI chat is only available in private Telegram chats.");
        }

        var channel = await _notificationSettingsService.GetTelegramChannelAsync(cancellationToken);
        if (!channel.TelegramEnabled)
        {
            return Reply(TelegramMessageProcessingStatus.Ignored, "Telegram bot notifications are disabled. An administrator can enable Telegram infrastructure from notification settings.");
        }

        var aiSettings = await _aiSettingsService.GetCurrentAsync(cancellationToken);
        if (!aiSettings.AssistantEnabled)
        {
            return Reply(TelegramMessageProcessingStatus.Ignored, "AI assistant is disabled. An administrator can enable it from AI Assistant settings.");
        }

        if (!aiSettings.TelegramChatEnabled)
        {
            return Reply(TelegramMessageProcessingStatus.Ignored, "Telegram AI chat is disabled. An administrator can enable it from AI Assistant settings.");
        }

        var account = await FindLinkedAccountAsync(inboundMessage.ChatId, cancellationToken);
        if (account is null)
        {
            return Reply(TelegramMessageProcessingStatus.Ignored, UnlinkedMessage);
        }

        var userMessage = text.Length <= MaxInboundAiMessageCharacters ? text : text[..MaxInboundAiMessageCharacters];
        var response = await _aiChatService.SendAsync(new AiChatRequest
        {
            Source = AiChatSource.Telegram,
            UserMessage = userMessage,
            UserId = account.UserId,
            ConversationHistory = _conversationStore.GetHistory(inboundMessage.ChatId, account.UserId).ToList()
        }, cancellationToken);

        if (!response.Succeeded || string.IsNullOrWhiteSpace(response.AssistantMessage))
        {
            return Reply(TelegramMessageProcessingStatus.Ignored, ToTelegramSafeError(response.ErrorMessage));
        }

        var replyText = FitTelegramMessage(response.AssistantMessage.Trim());
        _conversationStore.AddTurn(inboundMessage.ChatId, account.UserId, userMessage, replyText);
        return Reply(TelegramMessageProcessingStatus.Ignored, replyText);
    }

    private async Task<TelegramMessageProcessingResult> ConsumeLinkCodeAsync(TelegramInboundMessage inboundMessage, string code, CancellationToken cancellationToken)
    {
        var result = await _telegramLinkService.ConsumeCodeAsync(code, inboundMessage.ChatId, inboundMessage.ChatType, inboundMessage.Username, inboundMessage.DisplayName, cancellationToken);
        return new TelegramMessageProcessingResult { Status = MapStatus(result.Status), Handled = true, Success = result.Success, Message = result.Message, ShouldReply = true, ReplyText = GetReplyText(result.Status) };
    }

    private async Task<TelegramMessageProcessingResult> ClearAiAsync(TelegramInboundMessage inboundMessage, CancellationToken cancellationToken)
    {
        if (!string.Equals(inboundMessage.ChatType, "private", StringComparison.OrdinalIgnoreCase))
        {
            return Ignored("AI clear command is only available in private Telegram chats.");
        }

        var account = await FindLinkedAccountAsync(inboundMessage.ChatId, cancellationToken);
        if (account is null)
        {
            return Reply(TelegramMessageProcessingStatus.Ignored, UnlinkedMessage);
        }

        _conversationStore.Clear(inboundMessage.ChatId, account.UserId);
        return Reply(TelegramMessageProcessingStatus.Ignored, "AI conversation context cleared.");
    }

    private Task<LinkedTelegramAccount?> FindLinkedAccountAsync(string chatId, CancellationToken cancellationToken)
        => _dbContext.TelegramAccounts.AsNoTracking()
            .Where(x => x.ChatId == chatId && x.Verified && x.IsActive)
            .Select(x => new LinkedTelegramAccount(x.UserId))
            .SingleOrDefaultAsync(cancellationToken);

    private static bool IsKnownCommand(string text)
    {
        var command = text.Split(' ', 2)[0].Split('@', 2)[0];
        return command.Equals("/start", StringComparison.OrdinalIgnoreCase)
            || command.Equals("/help", StringComparison.OrdinalIgnoreCase)
            || IsClearAiCommand(command);
    }

    private static bool IsClearAiCommand(string text)
    {
        var command = text.Split(' ', 2)[0].Split('@', 2)[0];
        return command.Equals("/clearai", StringComparison.OrdinalIgnoreCase) || command.Equals("/clear_ai", StringComparison.OrdinalIgnoreCase);
    }

    private static TelegramMessageProcessingResult Ignored(string message) => new() { Status = TelegramMessageProcessingStatus.Ignored, Handled = false, Success = true, Message = message, ShouldReply = false };
    private static TelegramMessageProcessingResult Reply(TelegramMessageProcessingStatus status, string text) => new() { Status = status, Handled = true, Success = true, Message = "Telegram message processed.", ShouldReply = true, ReplyText = text };

    private static string ToTelegramSafeError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return "The AI assistant could not respond right now. Please try again later.";
        }

        if (errorMessage.Contains("disabled", StringComparison.OrdinalIgnoreCase) || errorMessage.Contains("configuration", StringComparison.OrdinalIgnoreCase))
        {
            return errorMessage;
        }

        return "The AI assistant could not respond right now. Please try again later.";
    }

    private static string FitTelegramMessage(string value)
    {
        const string note = "\n\n[Response truncated for Telegram.]";
        return value.Length <= TelegramMessageLimit ? value : value[..(TelegramMessageLimit - note.Length)] + note;
    }

    private static TelegramMessageProcessingStatus MapStatus(TelegramLinkConsumeStatus status)
        => status switch
        {
            TelegramLinkConsumeStatus.Success => TelegramMessageProcessingStatus.VerificationSucceeded,
            TelegramLinkConsumeStatus.InvalidCode => TelegramMessageProcessingStatus.InvalidCode,
            TelegramLinkConsumeStatus.ExpiredCode => TelegramMessageProcessingStatus.ExpiredCode,
            TelegramLinkConsumeStatus.AlreadyUsedCode => TelegramMessageProcessingStatus.AlreadyUsedCode,
            TelegramLinkConsumeStatus.UnsupportedChatType => TelegramMessageProcessingStatus.UnsupportedChatType,
            _ => TelegramMessageProcessingStatus.Ignored
        };

    private static string GetReplyText(TelegramLinkConsumeStatus status)
        => status switch
        {
            TelegramLinkConsumeStatus.Success => "✅ Verification successful. Your Telegram account is now linked to Ping Monitor.",
            TelegramLinkConsumeStatus.ExpiredCode => "⚠️ This verification code has expired. Please generate a new code in Ping Monitor and try again.",
            TelegramLinkConsumeStatus.InvalidCode => "⚠️ Verification code not recognised. Please check the code and try again.",
            TelegramLinkConsumeStatus.AlreadyUsedCode => "⚠️ This verification code has already been used. Please generate a new code in Ping Monitor if needed.",
            TelegramLinkConsumeStatus.UnsupportedChatType => "⚠️ Verification must be completed in a private chat with this bot.",
            _ => string.Empty
        };

    private sealed record LinkedTelegramAccount(string UserId);

    [GeneratedRegex("^(\\d{8})$")]
    private static partial Regex CodeRegex();
}
