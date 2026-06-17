using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PingMonitor.Web.Services.Telegram;

internal static partial class TelegramAiMarkdownFormatter
{
    internal const int TelegramMessageLimit = 3900;

    public static IReadOnlyList<TelegramOutgoingMessage> BuildMessages(string text, TelegramMessageFormat format)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (format == TelegramMessageFormat.PlainText) return SplitPlainText(text).Select(x => new TelegramOutgoingMessage(x, TelegramMessageFormat.PlainText)).ToArray();
        if (format == TelegramMessageFormat.Html) return SplitPlainText(text).Select(x => new TelegramOutgoingMessage(x, TelegramMessageFormat.Html)).ToArray();
        return SplitPlainText(text, 2400).Select(x => new TelegramOutgoingMessage(ToTelegramHtml(x), TelegramMessageFormat.Html)).ToArray();
    }

    public static string ToTelegramHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        var sb = new StringBuilder();
        var inCode = false;
        var code = new StringBuilder();
        foreach (var raw in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    AppendLine(sb, "<pre>" + WebUtility.HtmlEncode(code.ToString().TrimEnd('\n')) + "</pre>");
                    code.Clear();
                    inCode = false;
                }
                else inCode = true;
                continue;
            }
            if (inCode) { code.AppendLine(line); continue; }
            AppendLine(sb, ConvertLine(line));
        }
        if (inCode) AppendLine(sb, "<pre>" + WebUtility.HtmlEncode(code.ToString().TrimEnd('\n')) + "</pre>");
        return sb.ToString().TrimEnd();
    }

    private static string ConvertLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return string.Empty;
        var heading = HeadingRegex().Match(line);
        if (heading.Success) return "<b>" + ConvertInline(heading.Groups[1].Value.Trim()) + "</b>";
        var bullet = BulletRegex().Match(line);
        if (bullet.Success) return "• " + ConvertInline(bullet.Groups[1].Value.Trim());
        var numbered = NumberedRegex().Match(line);
        if (numbered.Success) return WebUtility.HtmlEncode(numbered.Groups[1].Value) + ". " + ConvertInline(numbered.Groups[2].Value.Trim());
        var quote = QuoteRegex().Match(line);
        if (quote.Success) return "&gt; " + ConvertInline(quote.Groups[1].Value.Trim());
        return ConvertInline(line);
    }

    private static string ConvertInline(string text)
    {
        var encoded = WebUtility.HtmlEncode(StripRawHtmlTags(text)) ?? string.Empty;
        encoded = SafeLinkRegex().Replace(encoded, m => IsSafeUrl(WebUtility.HtmlDecode(m.Groups[2].Value))
            ? $"<a href=\"{WebUtility.HtmlEncode(WebUtility.HtmlDecode(m.Groups[2].Value))}\">{m.Groups[1].Value}</a>"
            : $"{m.Groups[1].Value} ({m.Groups[2].Value})");
        encoded = CodeRegex().Replace(encoded, "<code>$1</code>");
        encoded = BoldRegex().Replace(encoded, "<b>$1</b>");
        encoded = ItalicStarRegex().Replace(encoded, "<i>$1</i>");
        encoded = ItalicUnderscoreRegex().Replace(encoded, "<i>$1</i>");
        return encoded;
    }

    private static string StripRawHtmlTags(string text) => RawHtmlTagRegex().Replace(text, m =>
    {
        var tag = m.Value.Trim('<', '>', '/', ' ');
        var space = tag.IndexOfAny([' ', '\t', '\r', '\n']);
        return space >= 0 ? tag[..space] : tag;
    });
    private static bool IsSafeUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    private static void AppendLine(StringBuilder sb, string line) { if (sb.Length > 0) sb.Append('\n'); sb.Append(line); }

    internal static IReadOnlyList<string> SplitPlainText(string text) => SplitPlainText(text, TelegramMessageLimit);

    private static IReadOnlyList<string> SplitPlainText(string text, int messageLimit)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (line.Length > messageLimit)
            {
                Flush();
                for (var i = 0; i < line.Length; i += messageLimit) chunks.Add(line.Substring(i, Math.Min(messageLimit, line.Length - i)));
                continue;
            }
            if (current.Length + line.Length + 1 > messageLimit) Flush();
            if (current.Length > 0) current.Append('\n');
            current.Append(line);
        }
        Flush();
        return chunks;
        void Flush() { if (current.Length > 0) { chunks.Add(current.ToString()); current.Clear(); } }
    }

    [GeneratedRegex("^#{1,6}\\s+(.+)$", RegexOptions.CultureInvariant)] private static partial Regex HeadingRegex();
    [GeneratedRegex("^\\s*[-*]\\s+(.+)$", RegexOptions.CultureInvariant)] private static partial Regex BulletRegex();
    [GeneratedRegex("^\\s*(\\d+)\\.\\s+(.+)$", RegexOptions.CultureInvariant)] private static partial Regex NumberedRegex();
    [GeneratedRegex("^\\s*>\\s?(.+)$", RegexOptions.CultureInvariant)] private static partial Regex QuoteRegex();
    [GeneratedRegex("`([^`]+)`", RegexOptions.CultureInvariant)] private static partial Regex CodeRegex();
    [GeneratedRegex("\\*\\*([^*]+)\\*\\*", RegexOptions.CultureInvariant)] private static partial Regex BoldRegex();
    [GeneratedRegex("(?<!\\*)\\*([^*]+)\\*(?!\\*)", RegexOptions.CultureInvariant)] private static partial Regex ItalicStarRegex();
    [GeneratedRegex("(?<!\\w)_([^_]+)_(?!\\w)", RegexOptions.CultureInvariant)] private static partial Regex ItalicUnderscoreRegex();
    [GeneratedRegex("\\[([^\\]]+)\\]\\(([^)]+)\\)", RegexOptions.CultureInvariant)] private static partial Regex SafeLinkRegex();
    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)] private static partial Regex RawHtmlTagRegex();
}

public sealed record TelegramOutgoingMessage(string Text, TelegramMessageFormat Format);
