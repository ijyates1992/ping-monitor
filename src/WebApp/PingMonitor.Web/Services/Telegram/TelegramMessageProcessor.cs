using System.Text.RegularExpressions;

namespace PingMonitor.Web.Services.Telegram;

internal sealed partial class TelegramMessageProcessor : ITelegramMessageProcessor
{
    private readonly ITelegramLinkService _telegramLinkService;

    public TelegramMessageProcessor(ITelegramLinkService telegramLinkService)
    {
        _telegramLinkService = telegramLinkService;
    }

    public async Task<TelegramMessageProcessingResult> ProcessAsync(TelegramInboundMessage inboundMessage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inboundMessage.Text))
        {
            return new TelegramMessageProcessingResult
            {
                Status = TelegramMessageProcessingStatus.Ignored,
                Handled = false,
                Success = true,
                Message = "No text content.",
                ShouldReply = false
            };
        }

        var match = CodeRegex().Match(inboundMessage.Text.Trim());
        if (!match.Success)
        {
            return new TelegramMessageProcessingResult
            {
                Status = TelegramMessageProcessingStatus.Ignored,
                Handled = false,
                Success = true,
                Message = "Unsupported message content.",
                ShouldReply = false
            };
        }

        var result = await _telegramLinkService.ConsumeCodeAsync(
            match.Groups[1].Value,
            inboundMessage.ChatId,
            inboundMessage.ChatType,
            inboundMessage.Username,
            inboundMessage.DisplayName,
            cancellationToken);

        return new TelegramMessageProcessingResult
        {
            Status = MapStatus(result.Status),
            Handled = true,
            Success = result.Success,
            Message = result.Message,
            ShouldReply = true,
            ReplyText = GetReplyText(result.Status)
        };
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

    [GeneratedRegex("^(\\d{8})$")]
    private static partial Regex CodeRegex();
}
