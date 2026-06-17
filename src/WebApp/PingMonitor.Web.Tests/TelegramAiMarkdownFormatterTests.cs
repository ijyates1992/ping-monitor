using Xunit;
using PingMonitor.Web.Services.Telegram;

namespace PingMonitor.Web.Tests;

public sealed class TelegramAiMarkdownFormatterTests
{
    [Fact]
    public void ConvertsBoldHeadingListsAndCodeToTelegramHtml()
    {
        var html = TelegramAiMarkdownFormatter.ToTelegramHtml("### Status Report\n**Event:** Endpoint Down\n- first\n* second\n1. numbered\n`DOWN`\n```\nraw <tag>\n```");

        Assert.Contains("<b>Status Report</b>", html);
        Assert.Contains("<b>Event:</b> Endpoint Down", html);
        Assert.Contains("• first", html);
        Assert.Contains("• second", html);
        Assert.Contains("1. numbered", html);
        Assert.Contains("<code>DOWN</code>", html);
        Assert.Contains("<pre>raw &lt;tag&gt;</pre>", html);
    }

    [Fact]
    public void EscapesRawHtmlAndRejectsUnsafeLinks()
    {
        var html = TelegramAiMarkdownFormatter.ToTelegramHtml("<script>alert(1)</script> <a onclick=\"x\">bad</a> [unsafe](javascript:alert(1)) [safe](https://example.com?a=1&b=2)");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scriptalert(1)script", html);
        Assert.Contains("unsafe (javascript:alert(1))", html);
        Assert.Contains("<a href=\"https://example.com?a=1&amp;b=2\">safe</a>", html);
    }

    [Fact]
    public void BuildsAiMarkdownMessagesAsHtmlChunksWithoutBrokenTags()
    {
        var markdown = string.Join('\n', Enumerable.Range(0, 600).Select(i => $"### Heading {i}\n**bold** line with `code` and - not a list"));
        var chunks = TelegramAiMarkdownFormatter.BuildMessages(markdown, TelegramMessageFormat.AiMarkdown);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(TelegramMessageFormat.Html, chunk.Format);
            Assert.True(chunk.Text.Length <= TelegramAiMarkdownFormatter.TelegramMessageLimit);
            Assert.DoesNotContain("###", chunk.Text);
            Assert.True(HasBalanced(chunk.Text, "<b>", "</b>"));
            Assert.True(HasBalanced(chunk.Text, "<code>", "</code>"));
            Assert.True(HasBalanced(chunk.Text, "<pre>", "</pre>"));
        });
    }

    [Fact]
    public void PlainTextMessagesAreNotTreatedAsMarkdown()
    {
        var chunks = TelegramAiMarkdownFormatter.BuildMessages("**plain alert**", TelegramMessageFormat.PlainText);

        var chunk = Assert.Single(chunks);
        Assert.Equal(TelegramMessageFormat.PlainText, chunk.Format);
        Assert.Equal("**plain alert**", chunk.Text);
    }

    [Fact]
    public void DirectTelegramAiChatRepliesRequestAiMarkdownFormatting()
    {
        var source = ReadWebFile("Services", "Telegram", "TelegramMessageProcessor.cs");

        Assert.Contains("TelegramMessageFormat.AiMarkdown", source);
        Assert.Contains("ReplyFormat = format", source);
    }

    [Fact]
    public void ScheduledAndEventAiTaskTelegramReportsRequestAiMarkdownFormatting()
    {
        Assert.Contains("TelegramMessageFormat.AiMarkdown", ReadWebFile("Services", "AiScheduledTasks", "AiScheduledTaskWorker.cs"));
        Assert.Contains("TelegramMessageFormat.AiMarkdown", ReadWebFile("Services", "AiEventTasks", "AiEventTriggeredTaskWorker.cs"));
    }

    [Fact]
    public void TelegramFormattedSendFailuresRetryAsPlainTextWithoutMessageContentInLog()
    {
        Assert.Contains("retrying the chunk as plain text", ReadWebFile("Services", "Telegram", "TelegramDirectMessageSender.cs"));
        Assert.Contains("retrying as plain text", ReadWebFile("Services", "Telegram", "TelegramPollingService.cs"));
    }

    private static bool HasBalanced(string value, string open, string close) => Count(value, open) == Count(value, close);
    private static int Count(string value, string token) => value.Split(token).Length - 1;
    private static string ReadWebFile(params string[] parts) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", Path.Combine(parts)));
}
