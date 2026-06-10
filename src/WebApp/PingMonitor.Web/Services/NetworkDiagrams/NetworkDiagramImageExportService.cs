using System.Globalization;
using System.Text;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PingMonitor.Web.Services.NetworkDiagrams;

public interface INetworkDiagramImageExportService
{
    NetworkDiagramImageExportResult Export(NetworkDiagramDto diagram, NetworkDiagramImageExportOptions options);
}

public sealed record NetworkDiagramImageExportOptions(string Format, double Scale, string Background, DateTimeOffset ExportedAtUtc);

public sealed record NetworkDiagramImageExportResult(byte[] Content, string ContentType, string FileName, int PixelWidth, int PixelHeight);

public sealed class NetworkDiagramImageExportException : Exception
{
    public NetworkDiagramImageExportException(string message) : base(message)
    {
    }
}

internal sealed class NetworkDiagramImageExportService : INetworkDiagramImageExportService
{
    private const int MaximumPixelDimension = 12000;
    private const int MaximumTotalPixels = 48_000_000;
    private const double ParallelLinkOffsetStep = 34d;
    private const double NodePadding = 10d;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public NetworkDiagramImageExportResult Export(NetworkDiagramDto diagram, NetworkDiagramImageExportOptions options)
    {
        var format = NormalizeFormat(options.Format);
        var scale = NormalizeScale(options.Scale);
        var background = NormalizeBackground(options.Background);
        var layout = NetworkDiagramNativeExportLayout.Create(diagram);
        var pixelWidth = CheckedPixelDimension(layout.CanvasWidth, scale, nameof(diagram.CanvasWidth));
        var pixelHeight = CheckedPixelDimension(layout.CanvasHeight, scale, nameof(diagram.CanvasHeight));
        ValidatePixelBudget(pixelWidth, pixelHeight);

        var safeName = MakeSafeFileName(diagram.Name);
        var extension = format.ToLowerInvariant();
        var fileName = $"PingMonitor-NetworkDiagram-{safeName}-{options.ExportedAtUtc:yyyyMMdd-HHmm}.{extension}";

        var svg = RenderSvg(layout, background, scale);
        if (format == "SVG")
        {
            return new NetworkDiagramImageExportResult(Utf8NoBom.GetBytes(svg), "image/svg+xml; charset=utf-8", fileName, pixelWidth, pixelHeight);
        }

        var png = RenderPng(layout, background, pixelWidth, pixelHeight, scale);
        return new NetworkDiagramImageExportResult(png, "image/png", fileName, pixelWidth, pixelHeight);
    }

    internal static string RenderSvg(NetworkDiagramNativeExportLayout layout, string background, double scale = 1d)
    {
        var backgroundStyle = ResolveBackground(background);
        var width = Format(layout.CanvasWidth * scale);
        var height = Format(layout.CanvasHeight * scale);
        var svg = new StringBuilder();
        svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {Format(layout.CanvasWidth)} {Format(layout.CanvasHeight)}\" role=\"img\" aria-label=\"{EscapeXml(layout.DiagramName)} network diagram\">");
        svg.AppendLine("<defs><style><![CDATA[");
        svg.AppendLine(".pm-area-label{font:800 16px Arial,Helvetica,sans-serif;fill:#1f2937}.pm-area-notes{font:600 11px Arial,Helvetica,sans-serif;fill:#4b5563}.pm-node-label{font:700 14px Arial,Helvetica,sans-serif;fill:#101828}.pm-node-type{font:600 11px Arial,Helvetica,sans-serif;fill:#475467}.pm-node-notes{font:500 10px Arial,Helvetica,sans-serif;fill:#667085}.pm-link-label{font:800 12px Arial,Helvetica,sans-serif;fill:#101828;paint-order:stroke;stroke:#fff;stroke-width:4px;stroke-linejoin:round}.pm-link-label-dark{stroke:#111827;fill:#f9fafb}.pm-node-icon{font:800 13px Arial,Helvetica,sans-serif}.pm-footer{font:600 11px Arial,Helvetica,sans-serif;fill:#667085}");
        svg.AppendLine("]]></style></defs>");
        if (backgroundStyle.IsTransparent)
        {
            svg.AppendLine("<rect x=\"0\" y=\"0\" width=\"100%\" height=\"100%\" fill=\"none\"/>");
        }
        else
        {
            svg.AppendLine($"<rect x=\"0\" y=\"0\" width=\"{Format(layout.CanvasWidth)}\" height=\"{Format(layout.CanvasHeight)}\" fill=\"{backgroundStyle.CanvasFill}\"/>");
        }

        svg.AppendLine("<g data-layer=\"links\">");
        foreach (var link in layout.Links)
        {
            var style = ResolveLinkStyle(link.MediaType, link.LinkType);
            svg.Append($"<g data-link-id=\"{EscapeXml(link.LinkId)}\" data-media-type=\"{EscapeXml(link.MediaType.ToLowerInvariant())}\" data-link-type=\"{EscapeXml(link.LinkType.ToLowerInvariant())}\">");
            svg.Append($"<path d=\"M {Format(link.StartX)} {Format(link.StartY)} Q {Format(link.ControlX)} {Format(link.ControlY)} {Format(link.EndX)} {Format(link.EndY)}\" fill=\"none\" stroke=\"{style.Color}\" stroke-width=\"{Format(style.Width)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"");
            if (!string.IsNullOrWhiteSpace(style.DashArray))
            {
                svg.Append($" stroke-dasharray=\"{EscapeXml(style.DashArray)}\"");
            }
            if (style.Opacity < 1d)
            {
                svg.Append($" opacity=\"{Format(style.Opacity)}\"");
            }
            svg.Append("/>");
            if (!string.IsNullOrWhiteSpace(link.Label))
            {
                var labelClass = backgroundStyle.IsDark ? "pm-link-label pm-link-label-dark" : "pm-link-label";
                svg.Append($"<text class=\"{labelClass}\" x=\"{Format(link.LabelX)}\" y=\"{Format(link.LabelY)}\" text-anchor=\"middle\">{EscapeXml(link.Label)}</text>");
            }
            svg.AppendLine("</g>");
        }
        svg.AppendLine("</g>");

        svg.AppendLine("<g data-layer=\"nodes\">");
        foreach (var node in layout.Nodes)
        {
            var style = ResolveNodeStyle(node.NodeType, backgroundStyle.IsDark);
            svg.AppendLine($"<g data-node-id=\"{EscapeXml(node.NodeId)}\" transform=\"translate({Format(node.X)} {Format(node.Y)})\">");
            svg.AppendLine($"<rect x=\"0\" y=\"0\" width=\"{Format(node.Width)}\" height=\"{Format(node.Height)}\" rx=\"14\" ry=\"14\" fill=\"{style.Fill}\" stroke=\"{style.Stroke}\" stroke-width=\"2\"/>");
            svg.AppendLine($"<rect x=\"10\" y=\"10\" width=\"34\" height=\"34\" rx=\"10\" ry=\"10\" fill=\"{style.IconFill}\" stroke=\"{style.Stroke}\" stroke-width=\"1\"/>");
            svg.AppendLine($"<text class=\"pm-node-icon\" x=\"27\" y=\"32\" text-anchor=\"middle\" fill=\"{style.IconText}\">{EscapeXml(Truncate(node.IconKey, 4))}</text>");
            var textX = Math.Min(node.Width - NodePadding, 54d);
            var contentWidth = Math.Max(1d, node.Width - textX - NodePadding);
            var labelLines = FitText(node.DisplayLabel, contentWidth, maxLines: 2, fontSize: 14d);
            var y = 23d;
            foreach (var line in labelLines)
            {
                svg.AppendLine($"<text class=\"pm-node-label\" x=\"{Format(textX)}\" y=\"{Format(y)}\">{EscapeXml(line)}</text>");
                y += 16d;
            }
            svg.AppendLine($"<text class=\"pm-node-type\" x=\"{Format(textX)}\" y=\"{Format(Math.Min(node.Height - 24d, y + 2d))}\">{EscapeXml(FormatNodeType(node.NodeType))}</text>");
            if (!string.IsNullOrWhiteSpace(node.Notes) && node.Height >= 72d)
            {
                var notes = FitText(node.Notes, Math.Max(1d, node.Width - NodePadding * 2), maxLines: 1, fontSize: 10d).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    svg.AppendLine($"<text class=\"pm-node-notes\" x=\"{Format(NodePadding)}\" y=\"{Format(node.Height - 12d)}\">{EscapeXml(notes)}</text>");
                }
            }
            svg.AppendLine("</g>");
        }
        svg.AppendLine("</g>");
        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    private static byte[] RenderPng(NetworkDiagramNativeExportLayout layout, string background, int pixelWidth, int pixelHeight, double scale)
    {
        var bg = ResolveBackground(background);
        var canvasColor = bg.IsTransparent ? Color.Transparent : Color.ParseHex(bg.CanvasFill.TrimStart('#'));
        using var image = new Image<Rgba32>(pixelWidth, pixelHeight, canvasColor);
        var font = CreateFont(12f * (float)scale, FontStyle.Regular);
        var boldFont = CreateFont(14f * (float)scale, FontStyle.Bold);
        var smallFont = CreateFont(10f * (float)scale, FontStyle.Regular);
        var iconFont = CreateFont(13f * (float)scale, FontStyle.Bold);

        PointF P(double x, double y) => new((float)(x * scale), (float)(y * scale));
        float S(double value) => (float)(value * scale);

        image.Mutate(ctx =>
        {
            foreach (var area in layout.Areas)
            {
                var style = ResolveAreaStyle(area.StyleKey, bg.IsDark);
                var rect = new RectangularPolygon(S(area.X), S(area.Y), S(area.Width), S(area.Height));
                ctx.Fill(Color.ParseHex(style.Fill.TrimStart('#')).WithAlpha((float)style.FillOpacity), rect);
                ctx.Draw(Color.ParseHex(style.Stroke.TrimStart('#')), S(3), rect);
                var header = new RectangularPolygon(S(area.X), S(area.Y), S(area.Width), S(Math.Min(38d, area.Height)));
                ctx.Fill(Color.ParseHex(style.HeaderFill.TrimStart('#')).WithAlpha((float)style.HeaderOpacity), header);
                ctx.DrawText(area.Label, boldFont, bg.IsDark ? Color.White : Color.ParseHex("1f2937"), P(area.X + 16, area.Y + 9));
                if (!string.IsNullOrWhiteSpace(area.Notes) && area.Height >= 90d)
                {
                    var notes = FitText(area.Notes, Math.Max(1d, area.Width - 32d), maxLines: 1, fontSize: 10d).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(notes))
                    {
                        ctx.DrawText(notes, smallFont, bg.IsDark ? Color.ParseHex("d1d5db") : Color.ParseHex("4b5563"), P(area.X + 16, area.Y + area.Height - 28));
                    }
                }
            }

            foreach (var link in layout.Links)
            {
                var style = ResolveLinkStyle(link.MediaType, link.LinkType);
                var path = new PathBuilder().AddQuadraticBezier(P(link.StartX, link.StartY), P(link.ControlX, link.ControlY), P(link.EndX, link.EndY)).Build();
                var pen = Pens.Solid(Color.ParseHex(style.Color.TrimStart('#')), S(style.Width));
                ctx.Draw(pen, path);
                if (!string.IsNullOrWhiteSpace(link.Label))
                {
                    var textOptions = new RichTextOptions(font) { Origin = P(link.LabelX, link.LabelY - 12), HorizontalAlignment = HorizontalAlignment.Center };
                    ctx.DrawText(textOptions, link.Label, bg.IsDark ? Color.White : Color.Black);
                }
            }

            foreach (var node in layout.Nodes)
            {
                var style = ResolveNodeStyle(node.NodeType, bg.IsDark);
                var rect = new RectangularPolygon(S(node.X), S(node.Y), S(node.Width), S(node.Height));
                ctx.Fill(Color.ParseHex(style.Fill.TrimStart('#')), rect);
                ctx.Draw(Color.ParseHex(style.Stroke.TrimStart('#')), S(2), rect);
                var iconRect = new RectangularPolygon(S(node.X + 10), S(node.Y + 10), S(34), S(34));
                ctx.Fill(Color.ParseHex(style.IconFill.TrimStart('#')), iconRect);
                ctx.Draw(Color.ParseHex(style.Stroke.TrimStart('#')), S(1), iconRect);
                ctx.DrawText(Truncate(node.IconKey, 4), iconFont, Color.ParseHex(style.IconText.TrimStart('#')), P(node.X + 14, node.Y + 18));

                var textX = node.X + Math.Min(node.Width - NodePadding, 54d);
                var contentWidth = Math.Max(1d, node.Width - (textX - node.X) - NodePadding);
                var y = node.Y + 10d;
                foreach (var line in FitText(node.DisplayLabel, contentWidth, maxLines: 2, fontSize: 14d))
                {
                    ctx.DrawText(line, boldFont, Color.ParseHex("101828"), P(textX, y));
                    y += 16d;
                }
                ctx.DrawText(FormatNodeType(node.NodeType), smallFont, Color.ParseHex("475467"), P(textX, Math.Min(node.Y + node.Height - 32d, y + 2d)));
                if (!string.IsNullOrWhiteSpace(node.Notes) && node.Height >= 72d)
                {
                    var notes = FitText(node.Notes, Math.Max(1d, node.Width - NodePadding * 2), maxLines: 1, fontSize: 10d).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(notes))
                    {
                        ctx.DrawText(notes, smallFont, Color.ParseHex("667085"), P(node.X + NodePadding, node.Y + node.Height - 24d));
                    }
                }
            }
        });

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static Font CreateFont(float size, FontStyle style)
    {
        foreach (var preferred in new[] { "Arial", "DejaVu Sans", "Liberation Sans" })
        {
            var match = SystemFonts.Families.FirstOrDefault(f => string.Equals(f.Name, preferred, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Name))
            {
                return match.CreateFont(size, style);
            }
        }

        return SystemFonts.Families.First().CreateFont(size, style);
    }

    private static string NormalizeFormat(string? format)
    {
        var normalized = string.IsNullOrWhiteSpace(format) ? "PNG" : format.Trim().ToUpperInvariant();
        return normalized is "PNG" or "SVG" ? normalized : throw new NetworkDiagramImageExportException("Unsupported network diagram export format.");
    }

    private static double NormalizeScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            throw new NetworkDiagramImageExportException("Export scale must be greater than zero.");
        }

        var allowed = new[] { 0.5d, 1d, 2d };
        return allowed.FirstOrDefault(candidate => Math.Abs(candidate - scale) < 0.001d) is var matched && matched > 0
            ? matched
            : throw new NetworkDiagramImageExportException("Unsupported export scale. Choose 0.5x, 1x, or 2x.");
    }

    private static string NormalizeBackground(string? background)
    {
        var normalized = string.IsNullOrWhiteSpace(background) ? "light" : background.Trim().ToLowerInvariant();
        return normalized is "light" or "dark" or "transparent" ? normalized : throw new NetworkDiagramImageExportException("Unsupported export background. Choose light, dark, or transparent.");
    }

    private static int CheckedPixelDimension(double logicalDimension, double scale, string name)
    {
        if (double.IsNaN(logicalDimension) || double.IsInfinity(logicalDimension) || logicalDimension <= 0)
        {
            throw new NetworkDiagramImageExportException($"{name} must be greater than zero for image export.");
        }

        var pixels = (int)Math.Ceiling(logicalDimension * scale);
        if (pixels > MaximumPixelDimension)
        {
            throw new NetworkDiagramImageExportException($"Export image dimension {pixels}px exceeds the safe limit of {MaximumPixelDimension}px. Choose a smaller scale or canvas.");
        }

        return pixels;
    }

    private static void ValidatePixelBudget(int width, int height)
    {
        if ((long)width * height > MaximumTotalPixels)
        {
            throw new NetworkDiagramImageExportException($"Export image size {width} × {height} exceeds the safe limit of {MaximumTotalPixels:N0} pixels. Choose a smaller scale or canvas.");
        }
    }

    private static NetworkDiagramExportBackground ResolveBackground(string background)
    {
        return background switch
        {
            "dark" => new NetworkDiagramExportBackground("#111827", true, false),
            "transparent" => new NetworkDiagramExportBackground("#ffffff", false, true),
            _ => new NetworkDiagramExportBackground("#f8fafc", false, false)
        };
    }

    private static NetworkDiagramExportLinkStyle ResolveLinkStyle(string mediaType, string linkType)
    {
        var width = NetworkDiagramLinkTypes.Normalize(linkType) switch
        {
            NetworkDiagramLinkTypes.Lacp => 5d,
            NetworkDiagramLinkTypes.Trunk or NetworkDiagramLinkTypes.Wan or NetworkDiagramLinkTypes.Backhaul => 4d,
            _ => 3d
        };
        var normalizedMedia = NetworkDiagramLinkMediaTypes.Normalize(mediaType);
        var color = normalizedMedia switch
        {
            NetworkDiagramLinkMediaTypes.Fibre => "#7c3aed",
            NetworkDiagramLinkMediaTypes.Copper => "#9a6700",
            NetworkDiagramLinkMediaTypes.Dac => "#0f766e",
            NetworkDiagramLinkMediaTypes.Other => "#667085",
            _ => "#475467"
        };
        var dash = normalizedMedia switch
        {
            NetworkDiagramLinkMediaTypes.Wireless => "12 8",
            NetworkDiagramLinkMediaTypes.Vpn or NetworkDiagramLinkMediaTypes.Virtual => "14 6 3 6",
            _ when NetworkDiagramLinkTypes.Normalize(linkType) == NetworkDiagramLinkTypes.Logical => "5 7",
            _ => string.Empty
        };
        var opacity = NetworkDiagramLinkTypes.Normalize(linkType) == NetworkDiagramLinkTypes.Logical ? 0.72d : 1d;
        return new NetworkDiagramExportLinkStyle(color, width, dash, opacity);
    }


    private static NetworkDiagramExportAreaStyle ResolveAreaStyle(string? styleKey, bool dark)
    {
        var key = string.IsNullOrWhiteSpace(styleKey) ? "neutral" : styleKey.Trim().ToLowerInvariant();
        return key switch
        {
            "blue" => new NetworkDiagramExportAreaStyle("#60a5fa", "#2563eb", "#2563eb", dark ? 0.18 : 0.13, dark ? 0.30 : 0.18),
            "green" => new NetworkDiagramExportAreaStyle("#34d399", "#059669", "#059669", dark ? 0.18 : 0.13, dark ? 0.30 : 0.18),
            "amber" => new NetworkDiagramExportAreaStyle("#fbbf24", "#d97706", "#d97706", dark ? 0.20 : 0.14, dark ? 0.32 : 0.20),
            "red" => new NetworkDiagramExportAreaStyle("#f87171", "#dc2626", "#dc2626", dark ? 0.17 : 0.12, dark ? 0.30 : 0.18),
            "purple" => new NetworkDiagramExportAreaStyle("#a78bfa", "#7c3aed", "#7c3aed", dark ? 0.18 : 0.13, dark ? 0.30 : 0.18),
            _ => new NetworkDiagramExportAreaStyle(dark ? "#64748b" : "#94a3b8", dark ? "#94a3b8" : "#64748b", dark ? "#475569" : "#cbd5e1", dark ? 0.18 : 0.16, dark ? 0.28 : 0.38)
        };
    }

    private static NetworkDiagramExportNodeStyle ResolveNodeStyle(string nodeType, bool dark)
    {
        if (string.Equals(nodeType, "MonitoredEndpoint", StringComparison.OrdinalIgnoreCase))
        {
            return new NetworkDiagramExportNodeStyle("#f4f8ff", "#7aa7ff", "#e7f0ff", "#1d4ed8");
        }

        if (string.Equals(nodeType, "Note", StringComparison.OrdinalIgnoreCase))
        {
            return new NetworkDiagramExportNodeStyle("#fff8db", "#d7a900", "#fff0a6", "#7a4f00");
        }

        return dark
            ? new NetworkDiagramExportNodeStyle("#ffffff", "#94a3b8", "#eef2f7", "#334155")
            : new NetworkDiagramExportNodeStyle("#ffffff", "#98a2b3", "#eef2f7", "#334155");
    }

    internal static string BuildLinkLabel(NetworkDiagramLinkDto link)
    {
        var linkType = NetworkDiagramLinkTypes.Normalize(link.LinkType);
        var media = NetworkDiagramLinkMediaTypes.Normalize(link.MediaType).ToLowerInvariant();
        var speed = link.LinkSpeedValue is null || string.IsNullOrWhiteSpace(link.LinkSpeedUnit)
            ? string.Empty
            : $"{link.LinkSpeedValue:0.###} {NetworkDiagramLinkSpeedUnits.Normalize(link.LinkSpeedUnit)}";
        var summary = linkType == NetworkDiagramLinkTypes.Lacp
            ? string.Join(" ", new[] { "LACP", $"{link.LacpMemberCount ?? 2} x", speed, media }.Where(x => !string.IsNullOrWhiteSpace(x)))
            : string.Join(" ", new[] { linkType == NetworkDiagramLinkTypes.Standard ? string.Empty : linkType, speed, media }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var ports = linkType != NetworkDiagramLinkTypes.Lacp && (!string.IsNullOrWhiteSpace(link.SourcePortLabel) || !string.IsNullOrWhiteSpace(link.TargetPortLabel))
            ? $"{link.SourcePortLabel ?? "?"} ↔ {link.TargetPortLabel ?? "?"}"
            : string.Empty;
        var vlanSummary = BuildVlanSummary(link);
        return Truncate(string.Join(" • ", new[] { summary, link.Label, ports, vlanSummary, link.Notes }.Where(x => !string.IsNullOrWhiteSpace(x))), 116);
    }

    private static string BuildVlanSummary(NetworkDiagramLinkDto link)
    {
        if (link.Vlans.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" · ", link.Vlans
            .OrderBy(vlan => vlan.SortOrder)
            .ThenBy(vlan => vlan.VlanId)
            .Select(vlan => $"{NetworkDiagramVlanModes.Normalize(vlan.Mode)}:{vlan.VlanId}{(string.IsNullOrWhiteSpace(vlan.Name) ? string.Empty : " " + vlan.Name)}"));
    }

    private static IReadOnlyList<string> FitText(string? value, double maxWidth, int maxLines, double fontSize)
    {
        var normalized = NormalizeSingleLine(value);
        if (string.IsNullOrWhiteSpace(normalized) || maxWidth <= 0)
        {
            return [];
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(current) ? word : current + " " + word;
            if (MeasureText(candidate, fontSize) <= maxWidth || string.IsNullOrEmpty(current))
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = word;
            }

            if (lines.Count == maxLines)
            {
                break;
            }
        }

        if (lines.Count < maxLines && !string.IsNullOrEmpty(current))
        {
            lines.Add(current);
        }

        return lines.Take(maxLines).Select(line => TrimToWidth(line, maxWidth, fontSize)).ToArray();
    }

    private static string TrimToWidth(string text, double maxWidth, double fontSize)
    {
        var normalized = NormalizeSingleLine(text);
        if (MeasureText(normalized, fontSize) <= maxWidth)
        {
            return normalized;
        }

        const string ellipsis = "…";
        while (normalized.Length > 0 && MeasureText(normalized + ellipsis, fontSize) > maxWidth)
        {
            normalized = normalized[..^1].TrimEnd();
        }

        return normalized + ellipsis;
    }

    private static double MeasureText(string text, double fontSize)
    {
        return NormalizeSingleLine(text).Sum(ch => CharacterWidthFactor(ch) * fontSize);
    }

    private static double CharacterWidthFactor(char ch)
    {
        return ch switch
        {
            ' ' => 0.28d,
            '.' or ',' or ':' or ';' or '!' or '|' => 0.25d,
            'i' or 'l' or 'I' or '1' => 0.28d,
            'm' or 'w' or 'M' or 'W' => 0.86d,
            >= 'A' and <= 'Z' => 0.68d,
            >= '0' and <= '9' => 0.56d,
            _ => 0.52d
        };
    }

    private static string FormatNodeType(string nodeType)
    {
        return nodeType switch
        {
            "MonitoredEndpoint" => "monitored endpoint",
            "Note" => "note",
            _ => "custom device"
        };
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : ch).ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "Diagram" : Truncate(safe, 80);
    }

    private static string Truncate(string? value, int maxLength)
    {
        var text = NormalizeSingleLine(value);
        return text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 1)] + "…";
    }

    private static string NormalizeSingleLine(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string EscapeXml(string? value)
    {
        return System.Security.SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private sealed record NetworkDiagramExportBackground(string CanvasFill, bool IsDark, bool IsTransparent);
    private sealed record NetworkDiagramExportLinkStyle(string Color, double Width, string DashArray, double Opacity);
    private sealed record NetworkDiagramExportNodeStyle(string Fill, string Stroke, string IconFill, string IconText);
    private sealed record NetworkDiagramExportAreaStyle(string Fill, string Stroke, string HeaderFill, double FillOpacity, double HeaderOpacity);

    internal sealed record NetworkDiagramNativeExportLayout(
        string DiagramName,
        double CanvasWidth,
        double CanvasHeight,
        IReadOnlyList<NetworkDiagramNativeExportArea> Areas,
        IReadOnlyList<NetworkDiagramNativeExportNode> Nodes,
        IReadOnlyList<NetworkDiagramNativeExportLink> Links)
    {
        public static NetworkDiagramNativeExportLayout Create(NetworkDiagramDto diagram)
        {
            var canvasWidth = Math.Max(1d, diagram.CanvasWidth);
            var canvasHeight = Math.Max(1d, diagram.CanvasHeight);
            var areas = diagram.Areas.OrderBy(area => area.SortOrder).Select(area => new NetworkDiagramNativeExportArea(
                area.AreaId,
                area.Label,
                area.Notes,
                area.X,
                area.Y,
                Math.Max(1d, area.Width),
                Math.Max(1d, area.Height),
                area.StyleKey)).ToArray();
            var nodes = diagram.Nodes.Select(node => new NetworkDiagramNativeExportNode(
                node.NodeId,
                node.NodeType,
                node.DisplayLabel,
                node.IconKey,
                node.Notes,
                node.X,
                node.Y,
                Math.Max(1d, node.Width),
                Math.Max(1d, node.Height))).ToArray();
            var nodeLookup = nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
            var offsetIndexes = GetParallelOffsetIndexes(diagram.Links);
            var links = new List<NetworkDiagramNativeExportLink>();
            foreach (var link in diagram.Links)
            {
                if (!nodeLookup.TryGetValue(link.SourceNodeId, out var source) || !nodeLookup.TryGetValue(link.TargetNodeId, out var target))
                {
                    continue;
                }

                var geometry = BuildLinkGeometry(source, target, offsetIndexes.GetValueOrDefault(link.LinkId));
                links.Add(new NetworkDiagramNativeExportLink(
                    link.LinkId,
                    NetworkDiagramLinkMediaTypes.Normalize(link.MediaType),
                    NetworkDiagramLinkTypes.Normalize(link.LinkType),
                    BuildLinkLabel(link),
                    geometry.StartX,
                    geometry.StartY,
                    geometry.ControlX,
                    geometry.ControlY,
                    geometry.EndX,
                    geometry.EndY,
                    geometry.LabelX,
                    geometry.LabelY));
            }

            return new NetworkDiagramNativeExportLayout(diagram.Name, canvasWidth, canvasHeight, areas, nodes, links);
        }
    }

    internal sealed record NetworkDiagramNativeExportArea(string AreaId, string Label, string? Notes, double X, double Y, double Width, double Height, string? StyleKey);

    internal sealed record NetworkDiagramNativeExportNode(string NodeId, string NodeType, string DisplayLabel, string IconKey, string? Notes, double X, double Y, double Width, double Height);

    internal sealed record NetworkDiagramNativeExportLink(string LinkId, string MediaType, string LinkType, string Label, double StartX, double StartY, double ControlX, double ControlY, double EndX, double EndY, double LabelX, double LabelY);

    private sealed record LinkGeometry(double StartX, double StartY, double ControlX, double ControlY, double EndX, double EndY, double LabelX, double LabelY);

    private static Dictionary<string, double> GetParallelOffsetIndexes(IReadOnlyList<NetworkDiagramLinkDto> links)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var group in links.GroupBy(link => string.CompareOrdinal(link.SourceNodeId, link.TargetNodeId) <= 0
            ? $"{link.SourceNodeId}::{link.TargetNodeId}"
            : $"{link.TargetNodeId}::{link.SourceNodeId}"))
        {
            var ordered = group.OrderBy(link => link.LinkId, StringComparer.Ordinal).ToArray();
            var center = (ordered.Length - 1) / 2d;
            for (var i = 0; i < ordered.Length; i++)
            {
                result[ordered[i].LinkId] = i - center;
            }
        }

        return result;
    }

    private static LinkGeometry BuildLinkGeometry(NetworkDiagramNativeExportNode source, NetworkDiagramNativeExportNode target, double offsetIndex)
    {
        var startX = source.X + source.Width / 2d;
        var startY = source.Y + source.Height / 2d;
        var endX = target.X + target.Width / 2d;
        var endY = target.Y + target.Height / 2d;
        var dx = endX - startX;
        var dy = endY - startY;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0.01d)
        {
            length = 1d;
        }

        var perpendicularX = -dy / length;
        var perpendicularY = dx / length;
        var offset = offsetIndex * ParallelLinkOffsetStep;
        var controlX = (startX + endX) / 2d + perpendicularX * offset;
        var controlY = (startY + endY) / 2d + perpendicularY * offset;
        var midpointX = startX * 0.25d + controlX * 0.5d + endX * 0.25d;
        var midpointY = startY * 0.25d + controlY * 0.5d + endY * 0.25d;
        return new LinkGeometry(startX, startY, controlX, controlY, endX, endY, midpointX + perpendicularX * 14d, midpointY + perpendicularY * 14d - 4d);
    }
}
