using System.Text.Json;

namespace PingMonitor.Web.Services.Telegram;

internal sealed class TelegramBotIdentityResolver : ITelegramBotIdentityResolver
{
    private readonly ILogger<TelegramBotIdentityResolver> _logger;
    private readonly HttpClient _httpClient = new();

    public TelegramBotIdentityResolver(ILogger<TelegramBotIdentityResolver> logger)
    {
        _logger = logger;
    }

    public async Task<string?> ResolveBotIdentifierAsync(string botToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return null;
        }

        var trimmedToken = botToken.Trim();
        var fallbackBotId = TryExtractBotId(trimmedToken);
        var fallbackIdentifier = fallbackBotId is not null
            ? $"bot ID {fallbackBotId}"
            : "configured Telegram bot";

        var url = $"https://api.telegram.org/bot{trimmedToken}/getMe";
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram getMe returned status code {StatusCode}. Falling back to configured bot identifier.", (int)response.StatusCode);
                return fallbackIdentifier;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            {
                return fallbackIdentifier;
            }

            var username = result.TryGetProperty("username", out var usernameElement) ? usernameElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(username))
            {
                return username.StartsWith('@') ? username : $"@{username}";
            }

            return fallbackIdentifier;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram getMe request failed while resolving bot identity. Falling back to configured bot identifier.");
            return fallbackIdentifier;
        }
    }

    private static string? TryExtractBotId(string token)
    {
        var separatorIndex = token.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return null;
        }

        var candidate = token[..separatorIndex];
        foreach (var character in candidate)
        {
            if (!char.IsDigit(character))
            {
                return null;
            }
        }

        return candidate;
    }
}
