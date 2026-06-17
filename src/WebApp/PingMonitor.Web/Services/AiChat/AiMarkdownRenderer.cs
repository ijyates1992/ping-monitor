using System.Text.RegularExpressions;
using Ganss.Xss;
using Markdig;

namespace PingMonitor.Web.Services.AiChat;

internal sealed partial class AiMarkdownRenderer : IAiMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public string RenderAssistantMessage(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var html = Markdown.ToHtml(RawHtmlTag().Replace(markdown, string.Empty), Pipeline);
        var sanitized = Sanitizer.Sanitize(html);
        return AnchorTag().Replace(sanitized, match =>
        {
            var tag = match.Value;
            if (!tag.Contains(" rel=", StringComparison.OrdinalIgnoreCase))
            {
                tag = tag.Insert(tag.Length - 1, " rel=\"noopener noreferrer\"");
            }
            return tag;
        });
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "strong", "em", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "code", "pre", "blockquote", "a", "br", "hr", "table", "thead", "tbody", "tr", "th", "td" })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedAttributes.Add("title");
        sanitizer.AllowedAttributes.Add("rel");
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");
        return sanitizer;
    }

    [GeneratedRegex("<a\\s+[^>]*href=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnchorTag();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex RawHtmlTag();
}
