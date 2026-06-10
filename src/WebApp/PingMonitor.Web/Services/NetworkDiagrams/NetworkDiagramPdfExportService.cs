using System.Globalization;
using System.Text;

namespace PingMonitor.Web.Services.NetworkDiagrams;

public interface INetworkDiagramPdfExportService
{
    NetworkDiagramPdfExportResult Export(NetworkDiagramDto diagram, NetworkDiagramPdfExportOptions options);
}

public sealed record NetworkDiagramPdfExportOptions(string PaperSize, DateTimeOffset ExportedAtUtc);

public sealed record NetworkDiagramPdfExportResult(byte[] Content, string ContentType, string FileName);

internal sealed record NetworkDiagramPdfTextFitResult(IReadOnlyList<string> Lines, double FontSize)
{
    public double LineHeight => FontSize * 1.18d;
    public double TotalHeight => Lines.Count == 0 ? 0 : FontSize + (Lines.Count - 1) * LineHeight;
}

internal static class NetworkDiagramPdfTextFitter
{
    private const string Ellipsis = "...";

    public static NetworkDiagramPdfTextFitResult Fit(string? text, double maxWidth, int maxLines, double fontSize, double minimumFontSize, bool useEllipsis)
    {
        if (maxLines < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLines), "At least one line is required.");
        }

        var normalized = Normalize(text);
        if (string.IsNullOrEmpty(normalized) || maxWidth <= 0)
        {
            return new NetworkDiagramPdfTextFitResult(Array.Empty<string>(), Math.Max(minimumFontSize, Math.Min(fontSize, minimumFontSize)));
        }

        for (var size = fontSize; size >= minimumFontSize; size -= 0.5d)
        {
            var lines = Wrap(normalized, maxWidth, size);
            if (lines.Count <= maxLines && lines.All(line => MeasureWidth(line, size) <= maxWidth + 0.01d))
            {
                return new NetworkDiagramPdfTextFitResult(lines, size);
            }
        }

        var fittedLines = WrapToMaximumLines(normalized, maxWidth, minimumFontSize, maxLines, useEllipsis);
        return new NetworkDiagramPdfTextFitResult(fittedLines, minimumFontSize);
    }

    public static double MeasureWidth(string text, double fontSize)
    {
        var width = 0d;
        foreach (var ch in Normalize(text))
        {
            width += CharacterWidthFactor(ch) * fontSize;
        }

        return width;
    }

    private static IReadOnlyList<string> WrapToMaximumLines(string text, double maxWidth, double fontSize, int maxLines, bool useEllipsis)
    {
        var allLines = Wrap(text, maxWidth, fontSize);
        if (allLines.Count <= maxLines)
        {
            return allLines;
        }

        var result = allLines.Take(maxLines).ToList();
        if (useEllipsis && result.Count > 0)
        {
            result[^1] = TrimToWidth(result[^1], maxWidth, fontSize, appendEllipsis: true);
        }

        return result;
    }

    private static List<string> Wrap(string text, double maxWidth, double fontSize)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (MeasureWidth(word, fontSize) > maxWidth)
            {
                if (!string.IsNullOrEmpty(current))
                {
                    lines.Add(current);
                    current = string.Empty;
                }

                lines.AddRange(SplitLongWord(word, maxWidth, fontSize));
                continue;
            }

            var candidate = string.IsNullOrEmpty(current) ? word : current + " " + word;
            if (MeasureWidth(candidate, fontSize) <= maxWidth)
            {
                current = candidate;
            }
            else
            {
                if (!string.IsNullOrEmpty(current))
                {
                    lines.Add(current);
                }

                current = word;
            }
        }

        if (!string.IsNullOrEmpty(current))
        {
            lines.Add(current);
        }

        return lines;
    }

    private static IEnumerable<string> SplitLongWord(string word, double maxWidth, double fontSize)
    {
        var current = new StringBuilder();
        foreach (var ch in word)
        {
            var candidate = current.ToString() + ch;
            if (MeasureWidth(candidate, fontSize) > maxWidth)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                else
                {
                    var singleCharacter = TrimToWidth(ch.ToString(), maxWidth, fontSize, appendEllipsis: false);
                    if (!string.IsNullOrEmpty(singleCharacter))
                    {
                        yield return singleCharacter;
                    }

                    continue;
                }
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static string TrimToWidth(string text, double maxWidth, double fontSize, bool appendEllipsis)
    {
        var suffix = appendEllipsis ? TrimSuffixToWidth(Ellipsis, maxWidth, fontSize) : string.Empty;
        var availableWidth = Math.Max(0, maxWidth - MeasureWidth(suffix, fontSize));
        var builder = new StringBuilder();
        foreach (var ch in Normalize(text))
        {
            var candidate = builder.ToString() + ch;
            if (MeasureWidth(candidate, fontSize) > availableWidth)
            {
                break;
            }

            builder.Append(ch);
        }

        return builder.ToString().TrimEnd() + suffix;
    }

    private static string TrimSuffixToWidth(string suffix, double maxWidth, double fontSize)
    {
        var candidate = suffix;
        while (candidate.Length > 0 && MeasureWidth(candidate, fontSize) > maxWidth)
        {
            candidate = candidate[..^1];
        }

        return candidate;
    }

    private static string Normalize(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Replace("\r", " ").Replace("\n", " ").Replace("…", Ellipsis).Trim();
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
}

internal sealed class NetworkDiagramPdfExportService : INetworkDiagramPdfExportService
{
    private const string DocumentationFooter = "Diagram links are visual documentation only and do not create monitoring dependencies.";
    private const double MinimumExportNodeWidth = 92d;
    private const double MinimumExportNodeHeight = 48d;
    private const double NodePadding = 6d;

    public NetworkDiagramPdfExportResult Export(NetworkDiagramDto diagram, NetworkDiagramPdfExportOptions options)
    {
        var paper = ResolvePaper(options.PaperSize);
        var content = RenderContent(diagram, paper, options.ExportedAtUtc);
        var pdf = SimplePdfDocument.Create(paper.WidthPoints, paper.HeightPoints, content);
        var fileName = $"PingMonitor-NetworkDiagram-{MakeSafeFileName(diagram.Name)}-{options.ExportedAtUtc:yyyyMMdd-HHmm}.pdf";
        return new NetworkDiagramPdfExportResult(pdf, "application/pdf", fileName);
    }

    private static PdfPaper ResolvePaper(string? paperSize)
    {
        return string.Equals(paperSize, "A3", StringComparison.OrdinalIgnoreCase)
            ? new PdfPaper("A3", 1190.55, 841.89)
            : new PdfPaper("A4", 841.89, 595.28);
    }

    private static string RenderContent(NetworkDiagramDto diagram, PdfPaper paper, DateTimeOffset exportedAtUtc)
    {
        var stream = new StringBuilder();
        var pageMargin = 36d;
        var headerHeight = 58d;
        var footerHeight = 34d;
        var drawingLeft = pageMargin;
        var drawingBottom = pageMargin + footerHeight;
        var drawingWidth = paper.WidthPoints - pageMargin * 2;
        var drawingHeight = paper.HeightPoints - pageMargin * 2 - headerHeight - footerHeight;
        var drawingTop = drawingBottom + drawingHeight;

        stream.AppendLine("q");
        stream.AppendLine("1 1 1 rg");
        stream.AppendFormat(CultureInfo.InvariantCulture, "0 0 {0:0.##} {1:0.##} re f\n", paper.WidthPoints, paper.HeightPoints);
        stream.AppendLine("Q");

        AddText(stream, pageMargin, paper.HeightPoints - pageMargin - 6, 18, Truncate(diagram.Name, 90), bold: true);
        AddText(stream, pageMargin, paper.HeightPoints - pageMargin - 28, 9,
            $"Exported {exportedAtUtc:yyyy-MM-dd HH:mm} UTC • {paper.Name} landscape • saved diagram data", bold: false);
        AddText(stream, pageMargin, pageMargin + 8, 8, DocumentationFooter, bold: false);

        stream.AppendLine("q");
        stream.AppendLine("0.96 0.98 1 rg");
        stream.AppendLine("0.82 0.86 0.92 RG");
        stream.AppendLine("0.75 w");
        stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##} {3:0.##} re B\n", drawingLeft, drawingBottom, drawingWidth, drawingHeight);
        stream.AppendLine("Q");

        var bounds = GetExportBounds(diagram);
        var scaledWidth = bounds.Width * Math.Min(drawingWidth / bounds.Width, drawingHeight / bounds.Height);
        var scaledHeight = bounds.Height * Math.Min(drawingWidth / bounds.Width, drawingHeight / bounds.Height);
        var scale = Math.Min(drawingWidth / bounds.Width, drawingHeight / bounds.Height);
        var offsetX = drawingLeft + (drawingWidth - scaledWidth) / 2;
        var offsetY = drawingBottom + (drawingHeight - scaledHeight) / 2;

        double MapX(double worldX) => offsetX + (worldX - bounds.MinX) * scale;
        double MapY(double worldY) => offsetY + scaledHeight - (worldY - bounds.MinY) * scale;
        double MapWidth(double worldWidth) => worldWidth * scale;
        double MapHeight(double worldHeight) => worldHeight * scale;


        foreach (var area in diagram.Areas.OrderBy(x => x.SortOrder))
        {
            var x = MapX(area.X);
            var y = MapY(area.Y + area.Height);
            var width = Math.Max(1, MapWidth(area.Width));
            var height = Math.Max(1, MapHeight(area.Height));
            stream.AppendLine("q");
            ApplyAreaStyle(stream, area.StyleKey);
            stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##} {3:0.##} re B\n", x, y, width, height);
            stream.AppendLine("Q");
            AddText(stream, x + 8, y + height - 18, 9, Truncate(area.Label, 60), bold: true);
            if (!string.IsNullOrWhiteSpace(area.Notes) && height >= 45)
            {
                var fittedNotes = NetworkDiagramPdfTextFitter.Fit(area.Notes, maxWidth: Math.Max(20, width - 16), maxLines: 1, fontSize: 6.5, minimumFontSize: 5, useEllipsis: true);
                AddFittedText(stream, x + 8, y + 8, fittedNotes, bold: false);
            }
        }

        var nodeLookup = diagram.Nodes.ToDictionary(x => x.NodeId, StringComparer.Ordinal);
        var offsetIndexes = GetParallelOffsetIndexes(diagram.Links);
        foreach (var link in diagram.Links)
        {
            if (!nodeLookup.TryGetValue(link.SourceNodeId, out var source) || !nodeLookup.TryGetValue(link.TargetNodeId, out var target))
            {
                continue;
            }

            var sx = MapX(source.X + source.Width / 2);
            var sy = MapY(source.Y + source.Height / 2);
            var tx = MapX(target.X + target.Width / 2);
            var ty = MapY(target.Y + target.Height / 2);
            var geometry = BuildLinkGeometry(sx, sy, tx, ty, offsetIndexes.GetValueOrDefault(link.LinkId));
            stream.AppendLine("q");
            ApplyLinkStyle(stream, link);
            stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} m {2:0.##} {3:0.##} l S\n", geometry.StartX, geometry.StartY, geometry.EndX, geometry.EndY);
            stream.AppendLine("Q");

            var label = BuildLinkLabel(link);
            if (!string.IsNullOrWhiteSpace(label))
            {
                var fittedLabel = NetworkDiagramPdfTextFitter.Fit(label, maxWidth: 132, maxLines: 1, fontSize: 7, minimumFontSize: 5, useEllipsis: true);
                AddFittedText(stream, geometry.LabelX - 66, geometry.LabelY + 5, fittedLabel, bold: false);
            }
        }

        foreach (var node in diagram.Nodes)
        {
            var rawWidth = Math.Max(1, MapWidth(node.Width));
            var rawHeight = Math.Max(1, MapHeight(node.Height));
            var width = Math.Max(MinimumExportNodeWidth, rawWidth);
            var height = Math.Max(MinimumExportNodeHeight, rawHeight);
            var x = MapX(node.X) - (width - rawWidth) / 2;
            var y = MapY(node.Y + node.Height) - (height - rawHeight) / 2;
            stream.AppendLine("q");
            if (string.Equals(node.NodeType, "MonitoredEndpoint", StringComparison.OrdinalIgnoreCase))
            {
                stream.AppendLine("0.93 0.96 1 rg");
                stream.AppendLine("0.29 0.48 0.82 RG");
            }
            else if (string.Equals(node.NodeType, "Note", StringComparison.OrdinalIgnoreCase))
            {
                stream.AppendLine("1 0.98 0.86 rg");
                stream.AppendLine("0.72 0.53 0.04 RG");
            }
            else
            {
                stream.AppendLine("1 1 1 rg");
                stream.AppendLine("0.55 0.61 0.69 RG");
            }

            stream.AppendLine("1 w");
            stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##} {3:0.##} re B\n", x, y, width, height);
            stream.AppendLine("Q");

            AddNodeText(stream, node, x, y, width, height);
        }

        if (diagram.Nodes.Any(x => !string.IsNullOrWhiteSpace(x.Notes)) || diagram.Links.Any(x => !string.IsNullOrWhiteSpace(x.Notes)))
        {
            AddText(stream, pageMargin, pageMargin + 20, 7, "Notes are included on canvas where space allows; export uses the last saved diagram.", bold: false);
        }

        return stream.ToString();
    }


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

    private static LinkGeometry BuildLinkGeometry(double sx, double sy, double tx, double ty, double offsetIndex)
    {
        var dx = tx - sx;
        var dy = ty - sy;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0.01d)
        {
            length = 1d;
        }

        var px = -dy / length;
        var py = dx / length;
        var offset = offsetIndex * 9d;
        var startX = sx + px * offset;
        var startY = sy + py * offset;
        var endX = tx + px * offset;
        var endY = ty + py * offset;

        return new LinkGeometry(startX, startY, endX, endY, (startX + endX) / 2, (startY + endY) / 2 + (offset == 0 ? 6 : Math.Sign(offset) * 6));
    }


    private static void ApplyAreaStyle(StringBuilder stream, string? styleKey)
    {
        switch (string.IsNullOrWhiteSpace(styleKey) ? "neutral" : styleKey.Trim().ToLowerInvariant())
        {
            case "blue":
                stream.AppendLine("0.88 0.94 1 rg");
                stream.AppendLine("0.15 0.39 0.92 RG");
                break;
            case "green":
                stream.AppendLine("0.88 0.98 0.94 rg");
                stream.AppendLine("0.02 0.59 0.41 RG");
                break;
            case "amber":
                stream.AppendLine("1 0.96 0.82 rg");
                stream.AppendLine("0.85 0.47 0.02 RG");
                break;
            case "red":
                stream.AppendLine("1 0.92 0.92 rg");
                stream.AppendLine("0.86 0.15 0.15 RG");
                break;
            case "purple":
                stream.AppendLine("0.95 0.91 1 rg");
                stream.AppendLine("0.49 0.23 0.93 RG");
                break;
            default:
                stream.AppendLine("0.94 0.96 0.98 rg");
                stream.AppendLine("0.39 0.45 0.55 RG");
                break;
        }

        stream.AppendLine("1.2 w");
        stream.AppendLine("[7 4] 0 d");
    }

    private static void ApplyLinkStyle(StringBuilder stream, NetworkDiagramLinkDto link)
    {
        var width = NetworkDiagramLinkTypes.Normalize(link.LinkType) == NetworkDiagramLinkTypes.Lacp ? 2.6d : 1.5d;
        switch (NetworkDiagramLinkMediaTypes.Normalize(link.MediaType))
        {
            case NetworkDiagramLinkMediaTypes.Fibre:
                stream.AppendLine("0.49 0.23 0.93 RG");
                stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} w\n", Math.Max(width, 1.8d));
                stream.AppendLine("[] 0 d");
                break;
            case NetworkDiagramLinkMediaTypes.Copper:
                stream.AppendLine("0.60 0.40 0 RG");
                stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} w\n", width);
                stream.AppendLine("[] 0 d");
                break;
            case NetworkDiagramLinkMediaTypes.Wireless:
                stream.AppendLine("0.28 0.32 0.38 RG");
                stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} w\n", width);
                stream.AppendLine("[8 5] 0 d");
                break;
            case NetworkDiagramLinkMediaTypes.Vpn:
            case NetworkDiagramLinkMediaTypes.Virtual:
                stream.AppendLine("0.28 0.32 0.38 RG");
                stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} w\n", width);
                stream.AppendLine("[9 4 2 4] 0 d");
                break;
            case NetworkDiagramLinkMediaTypes.Dac:
                stream.AppendLine("0.06 0.46 0.43 RG");
                stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} w\n", Math.Max(width, 1.8d));
                stream.AppendLine("[] 0 d");
                break;
            default:
                stream.AppendLine("0.28 0.32 0.38 RG");
                stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} w\n", width);
                stream.AppendLine("[] 0 d");
                break;
        }
    }

    private static DiagramBounds GetExportBounds(NetworkDiagramDto diagram)
    {
        if (diagram.Nodes.Count == 0 && diagram.Areas.Count == 0)
        {
            return new DiagramBounds(0, 0, Math.Max(1, diagram.CanvasWidth), Math.Max(1, diagram.CanvasHeight));
        }

        var padding = 160d;
        var minX = Math.Max(0, Math.Min(
            diagram.Nodes.Count == 0 ? double.PositiveInfinity : diagram.Nodes.Min(x => x.X),
            diagram.Areas.Count == 0 ? double.PositiveInfinity : diagram.Areas.Min(x => x.X)) - padding);
        var minY = Math.Max(0, Math.Min(
            diagram.Nodes.Count == 0 ? double.PositiveInfinity : diagram.Nodes.Min(x => x.Y),
            diagram.Areas.Count == 0 ? double.PositiveInfinity : diagram.Areas.Min(x => x.Y)) - padding);
        var maxX = Math.Min(Math.Max(diagram.CanvasWidth, 1), Math.Max(
            diagram.Nodes.Count == 0 ? double.NegativeInfinity : diagram.Nodes.Max(x => x.X + x.Width),
            diagram.Areas.Count == 0 ? double.NegativeInfinity : diagram.Areas.Max(x => x.X + x.Width)) + padding);
        var maxY = Math.Min(Math.Max(diagram.CanvasHeight, 1), Math.Max(
            diagram.Nodes.Count == 0 ? double.NegativeInfinity : diagram.Nodes.Max(x => x.Y + x.Height),
            diagram.Areas.Count == 0 ? double.NegativeInfinity : diagram.Areas.Max(x => x.Y + x.Height)) + padding);
        if (maxX <= minX || maxY <= minY)
        {
            return new DiagramBounds(0, 0, Math.Max(1, diagram.CanvasWidth), Math.Max(1, diagram.CanvasHeight));
        }

        return new DiagramBounds(minX, minY, maxX, maxY);
    }

    private static string BuildLinkLabel(NetworkDiagramLinkDto link)
    {
        var linkType = NetworkDiagramLinkTypes.Normalize(link.LinkType);
        var normalizedMediaType = NetworkDiagramLinkMediaTypes.Normalize(link.MediaType);
        var mediaType = normalizedMediaType.ToLowerInvariant();
        var mediaSubtype = NetworkDiagramMediaSubtypes.Normalize(link.MediaSubtype ?? link.FibreSubtype, normalizedMediaType);
        var mediaLabel = !string.IsNullOrWhiteSpace(mediaSubtype) && normalizedMediaType == NetworkDiagramLinkMediaTypes.Copper
            ? mediaSubtype
            : string.Join(" ", new[] { mediaType, mediaSubtype }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var speed = link.LinkSpeedValue is null || string.IsNullOrWhiteSpace(link.LinkSpeedUnit)
            ? null
            : $"{link.LinkSpeedValue:0.###} {NetworkDiagramLinkSpeedUnits.Normalize(link.LinkSpeedUnit)}";
        var summary = linkType == NetworkDiagramLinkTypes.Lacp
            ? string.Join(" ", new[] { "LACP", $"{link.LacpMemberCount ?? 2} x", speed, mediaLabel }.Where(x => !string.IsNullOrWhiteSpace(x)))
            : string.Join(" ", new[] { linkType == NetworkDiagramLinkTypes.Standard ? null : linkType, speed, mediaLabel }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var ports = linkType != NetworkDiagramLinkTypes.Lacp && (!string.IsNullOrWhiteSpace(link.SourcePortLabel) || !string.IsNullOrWhiteSpace(link.TargetPortLabel))
            ? $"{link.SourcePortLabel ?? "?"} <-> {link.TargetPortLabel ?? "?"}"
            : null;
        var labelAndNotes = string.Join(" -- ", new[] { link.Label, link.Notes }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var vlanSummary = BuildVlanSummary(link);
        var parts = new[] { summary, ports, labelAndNotes, vlanSummary }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join(" • ", parts);
    }


    private static string? BuildVlanSummary(NetworkDiagramLinkDto link)
    {
        if (link.Vlans.Count == 0)
        {
            return null;
        }

        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NetworkDiagramVlanModes.Tagged] = "T",
            [NetworkDiagramVlanModes.Untagged] = "U",
            [NetworkDiagramVlanModes.Native] = "Native",
            [NetworkDiagramVlanModes.Management] = "Mgmt",
            [NetworkDiagramVlanModes.Other] = "Other"
        };

        var parts = NetworkDiagramVlanModes.Allowed
            .Select(mode =>
            {
                var values = link.Vlans
                    .Where(vlan => string.Equals(NetworkDiagramVlanModes.Normalize(vlan.Mode), mode, StringComparison.Ordinal))
                    .OrderBy(vlan => vlan.SortOrder)
                    .ThenBy(vlan => vlan.VlanId)
                    .Select(vlan => string.IsNullOrWhiteSpace(vlan.Name) ? vlan.VlanId.ToString(CultureInfo.InvariantCulture) : $"{vlan.VlanId} {vlan.Name}")
                    .ToArray();
                return values.Length == 0 ? null : $"{labels[mode]}:{string.Join(",", values)}";
            })
            .Where(x => !string.IsNullOrWhiteSpace(x));

        return Truncate(string.Join(" · ", parts), 80);
    }

    private static string FormatNodeType(string nodeType)
    {
        return nodeType switch
        {
            "MonitoredEndpoint" => "monitored endpoint",
            "CustomDevice" => "custom diagram node",
            "Note" => "note",
            _ => "diagram node"
        };
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : ch).ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "Diagram" : Truncate(safe, 80);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var singleLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..Math.Max(0, maxLength - 1)] + "…";
    }

    private static void AddNodeText(StringBuilder stream, NetworkDiagramNodeDto node, double x, double y, double width, double height)
    {
        var contentX = x + NodePadding;
        var contentTop = y + height - NodePadding;
        var contentWidth = Math.Max(1, width - NodePadding * 2);
        var contentHeight = Math.Max(1, height - NodePadding * 2);
        var currentTop = contentTop;

        stream.AppendLine("q");
        stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##} {3:0.##} re W n\n", x + 1, y + 1, Math.Max(1, width - 2), Math.Max(1, height - 2));

        var label = NetworkDiagramPdfTextFitter.Fit(node.DisplayLabel, contentWidth, maxLines: 2, fontSize: 8, minimumFontSize: 5.5, useEllipsis: true);
        AddFittedTextFromTop(stream, contentX, currentTop, label, bold: true);
        currentTop -= label.TotalHeight + 3;

        if (currentTop - y > 9)
        {
            var type = NetworkDiagramPdfTextFitter.Fit(FormatNodeType(node.NodeType), contentWidth, maxLines: 1, fontSize: 6.5, minimumFontSize: 5, useEllipsis: true);
            AddFittedTextFromTop(stream, contentX, currentTop, type, bold: false);
            currentTop -= type.TotalHeight + 3;
        }

        if (!string.IsNullOrWhiteSpace(node.Notes) && currentTop - y > 12 && contentHeight >= 42)
        {
            var notes = NetworkDiagramPdfTextFitter.Fit(node.Notes, contentWidth, maxLines: 1, fontSize: 5.5, minimumFontSize: 5, useEllipsis: true);
            AddFittedText(stream, contentX, y + NodePadding, notes, bold: false);
        }

        stream.AppendLine("Q");
    }

    private static void AddFittedTextFromTop(StringBuilder stream, double x, double topY, NetworkDiagramPdfTextFitResult text, bool bold)
    {
        AddFittedText(stream, x, topY - text.FontSize, text, bold);
    }

    private static void AddFittedText(StringBuilder stream, double x, double baselineY, NetworkDiagramPdfTextFitResult text, bool bold)
    {
        for (var i = 0; i < text.Lines.Count; i++)
        {
            AddText(stream, x, baselineY - i * text.LineHeight, text.FontSize, text.Lines[i], bold);
        }
    }

    private static void AddText(StringBuilder stream, double x, double y, double size, string text, bool bold)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        stream.AppendLine("BT");
        stream.AppendFormat(CultureInfo.InvariantCulture, "/{0} {1:0.##} Tf\n", bold ? "F2" : "F1", size);
        stream.AppendLine("0 0 0 rg");
        stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} Td\n", x, y);
        stream.AppendFormat("({0}) Tj\n", EscapePdfText(text));
        stream.AppendLine("ET");
    }

    private static string EscapePdfText(string text)
    {
        var normalized = text.Replace("…", "...");
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            builder.Append(ch switch
            {
                '(' => "\\(",
                ')' => "\\)",
                '\\' => "\\\\",
                >= ' ' and <= '~' => ch,
                _ => '?'
            });
        }

        return builder.ToString();
    }

    private sealed record PdfPaper(string Name, double WidthPoints, double HeightPoints);
    private sealed record LinkGeometry(double StartX, double StartY, double EndX, double EndY, double LabelX, double LabelY);
    private sealed record DiagramBounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        public double Width => Math.Max(1, MaxX - MinX);
        public double Height => Math.Max(1, MaxY - MinY);
    }

    private static class SimplePdfDocument
    {
        public static byte[] Create(double widthPoints, double heightPoints, string content)
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPoints:0.##} {heightPoints:0.##}] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>"),
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>",
                $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"
            };

            using var memory = new MemoryStream();
            void Write(string value)
            {
                var bytes = Encoding.ASCII.GetBytes(value);
                memory.Write(bytes, 0, bytes.Length);
            }

            Write("%PDF-1.4\n% PingMonitor\n");
            var offsets = new List<long> { 0 };
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(memory.Position);
                Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }

            var xrefOffset = memory.Position;
            Write($"xref\n0 {objects.Count + 1}\n");
            Write("0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1))
            {
                Write($"{offset:0000000000} 00000 n \n");
            }

            Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
            return memory.ToArray();
        }
    }
}
