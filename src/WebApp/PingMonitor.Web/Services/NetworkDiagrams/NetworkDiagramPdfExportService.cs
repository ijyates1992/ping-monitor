using System.Globalization;
using System.Text;

namespace PingMonitor.Web.Services.NetworkDiagrams;

public interface INetworkDiagramPdfExportService
{
    NetworkDiagramPdfExportResult Export(NetworkDiagramDto diagram, NetworkDiagramPdfExportOptions options);
}

public sealed record NetworkDiagramPdfExportOptions(string PaperSize, DateTimeOffset ExportedAtUtc);

public sealed record NetworkDiagramPdfExportResult(byte[] Content, string ContentType, string FileName);

internal sealed class NetworkDiagramPdfExportService : INetworkDiagramPdfExportService
{
    private const string DocumentationFooter = "Diagram links are visual documentation only and do not create monitoring dependencies.";

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

        var nodeLookup = diagram.Nodes.ToDictionary(x => x.NodeId, StringComparer.Ordinal);
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
            stream.AppendLine("q");
            stream.AppendLine("0.28 0.32 0.38 RG");
            stream.AppendLine("1.5 w");
            stream.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} m {2:0.##} {3:0.##} l S\n", sx, sy, tx, ty);
            stream.AppendLine("Q");

            var label = BuildLinkLabel(link);
            if (!string.IsNullOrWhiteSpace(label))
            {
                AddText(stream, (sx + tx) / 2 - 45, (sy + ty) / 2 + 5, 7, Truncate(label, 48), bold: false);
            }
        }

        foreach (var node in diagram.Nodes)
        {
            var x = MapX(node.X);
            var height = Math.Max(22, MapHeight(node.Height));
            var width = Math.Max(44, MapWidth(node.Width));
            var y = MapY(node.Y + node.Height);
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

            AddText(stream, x + 5, y + height - 12, 8, Truncate(node.DisplayLabel, 34), bold: true);
            AddText(stream, x + 5, y + height - 23, 6.5, FormatNodeType(node.NodeType), bold: false);
            if (!string.IsNullOrWhiteSpace(node.Notes))
            {
                AddText(stream, x + 5, y + 6, 6, Truncate(node.Notes, 44), bold: false);
            }
        }

        if (diagram.Nodes.Any(x => !string.IsNullOrWhiteSpace(x.Notes)) || diagram.Links.Any(x => !string.IsNullOrWhiteSpace(x.Notes)))
        {
            AddText(stream, pageMargin, pageMargin + 20, 7, "Notes are included on canvas where space allows; export uses the last saved diagram.", bold: false);
        }

        return stream.ToString();
    }

    private static DiagramBounds GetExportBounds(NetworkDiagramDto diagram)
    {
        if (diagram.Nodes.Count == 0)
        {
            return new DiagramBounds(0, 0, Math.Max(1, diagram.CanvasWidth), Math.Max(1, diagram.CanvasHeight));
        }

        var padding = 160d;
        var minX = Math.Max(0, diagram.Nodes.Min(x => x.X) - padding);
        var minY = Math.Max(0, diagram.Nodes.Min(x => x.Y) - padding);
        var maxX = Math.Min(Math.Max(diagram.CanvasWidth, 1), diagram.Nodes.Max(x => x.X + x.Width) + padding);
        var maxY = Math.Min(Math.Max(diagram.CanvasHeight, 1), diagram.Nodes.Max(x => x.Y + x.Height) + padding);
        if (maxX <= minX || maxY <= minY)
        {
            return new DiagramBounds(0, 0, Math.Max(1, diagram.CanvasWidth), Math.Max(1, diagram.CanvasHeight));
        }

        return new DiagramBounds(minX, minY, maxX, maxY);
    }

    private static string BuildLinkLabel(NetworkDiagramLinkDto link)
    {
        var parts = new[] { link.SourcePortLabel, link.Label, link.TargetPortLabel }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join(" • ", parts);
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
