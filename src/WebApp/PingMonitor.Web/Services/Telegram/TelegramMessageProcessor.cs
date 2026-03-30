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
            return new TelegramMessageProcessingResult { Handled = false, Success = true, Message = "No text content." };
        }

        var match = CodeRegex().Match(inboundMessage.Text.Trim());
        if (!match.Success)
        {
            return new TelegramMessageProcessingResult { Handled = false, Success = true, Message = "Unsupported message content." };
        }

        var result = await _telegramLinkService.ConsumeCodeAsync(
            match.Groups[1].Value,
            inboundMessage.ChatId,
            inboundMessage.ChatType,
            inboundMessage.Username,
            inboundMessage.DisplayName,
            cancellationToken);

        return new TelegramMessageProcessingResult { Handled = true, Success = result.Success, Message = result.Message };
    }

    [GeneratedRegex("^(\\d{8})$")]
    private static partial Regex CodeRegex();
}
