using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PdfEdit.Blazor.Services;

/// <summary>A node of the mind map: a topic, the page it's on (if any), and its subtopics.</summary>
public sealed class MindMapNode
{
    public string Title { get; set; } = "";
    public int? Page { get; set; }
    public List<MindMapNode> Children { get; set; } = new();
}

/// <summary>
/// Mind map of the document (UPDF style), ported from the Windows app: the prompt that asks the AI
/// for it, the reader for its reply, and an SVG drawing with the main topic on the left, branches to
/// the right and one colour per branch. Topics with a page carry <c>data-page="N"</c> so the page
/// can make them clickable.
/// </summary>
public static class MindMap
{
    /// <summary>The system prompt to send with <see cref="Prompt"/>.</summary>
    public const string SystemPrompt = "You turn documents into clear, well-organised mind maps. You reply with JSON only.";

    /// <summary>The request, given the document's text with pages marked [Page N].</summary>
    public static string Prompt(string documentText) =>
        "Make a mind map of this document. Reply with JSON only, in this shape: " +
        "{\"title\": \"short title of the document\", \"children\": [{\"title\": \"main topic (max 6 words)\", \"page\": 1, " +
        "\"children\": [{\"title\": \"key point (max 9 words)\", \"page\": 2, \"children\": []}]}]}. " +
        "Use 4 to 8 main topics with 2 to 5 key points each, and a third level only where it really helps. " +
        "\"page\" is the page number where the topic is found (pages are marked [Page N]).\n\nDocument:\n" + documentText;

    /// <summary>Shown when the reply has no usable map.</summary>
    public const string NoMapMessage = "The AI didn't return a mind map. Try again, or try another model.";

    /// <summary>
    /// Reads the AI's JSON ({"title","page","children"}), tolerating text around it. Returns null when
    /// there's no map or it has no topics; an empty root title becomes <paramref name="fallbackTitle"/>.
    /// </summary>
    public static MindMapNode? Parse(string reply, string? fallbackTitle = null)
    {
        int a = reply.IndexOf('{'), z = reply.LastIndexOf('}');
        if (a < 0 || z <= a) return null;
        MindMapNode root;
        try
        {
            using var doc = JsonDocument.Parse(reply[a..(z + 1)]);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            root = From(doc.RootElement, 0);
        }
        catch { return null; }
        if (root.Children.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(root.Title) && fallbackTitle != null) root.Title = fallbackTitle;
        return root;

        static MindMapNode From(JsonElement e, int depth)
        {
            var n = new MindMapNode
            {
                Title = e.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "",
                Page = e.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int pg) && pg > 0 ? pg : null,
            };
            if (depth < 4 && e.TryGetProperty("children", out var c) && c.ValueKind == JsonValueKind.Array)
                foreach (var child in c.EnumerateArray().Take(10))
                    if (child.ValueKind == JsonValueKind.Object) n.Children.Add(From(child, depth + 1));
            return n;
        }
    }

    // ── Drawing ─────────────────────────────────────────────────────────────

    private const double ColW = 250, NodeW = 210, RowH = 46, PadX = 10, PadY = 6, Margin = 30, SuffixGap = 6;
    private static readonly string[] Palette =
        ["#3B82F6", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6", "#06B6D4", "#EC4899", "#84CC16"];

    /// <summary>The map as a standalone SVG, sized to its content through its viewBox.</summary>
    public static string ToSvg(MindMapNode root, bool dark)
    {
        var nodes = new StringBuilder();
        var lines = new StringBuilder();
        double minY = 0, maxY = Leaves(root) * RowH, maxX = 0;
        Place(root, 0, 0, maxY, null, null);

        double vx = -Margin, vy = minY - Margin, vw = maxX + 2 * Margin, vh = maxY - minY + 2 * Margin;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{N(vx)} {N(vy)} {N(vw)} {N(vh)}\" width=\"{N(vw)}\" height=\"{N(vh)}\" ");
        sb.Append("font-family=\"'Segoe UI', system-ui, -apple-system, sans-serif\" role=\"img\" aria-label=\"")
          .Append(Esc("Mind map: " + root.Title)).Append("\">");
        sb.Append("<style>a[data-page]{cursor:pointer}a[data-page]:hover rect{filter:brightness(1.08)}a[data-page]:focus rect{stroke-width:2.5}</style>");
        sb.Append(lines).Append(nodes).Append("</svg>");
        return sb.ToString();

        void Place(MindMapNode n, int depth, double top, double bottom, (double X, double Y)? parentRight, string? branch)
        {
            double cy = (top + bottom) / 2, x = depth * ColW;
            string colour = branch ?? (dark ? "#475569" : "#334155");
            double fontSize = depth == 0 ? 16 : depth == 1 ? 13.5 : 12.5;
            bool bold = depth <= 1;
            string? suffix = n.Page is int pg ? $"p.{pg}" : null;

            var wrapped = Wrap(n.Title, NodeW - 2 * PadX, fontSize, bold, suffix == null ? 0 : TextWidth(suffix, 10.5, false) + SuffixGap);
            double lineH = Math.Round(fontSize * 1.33, 2);
            double textW = wrapped.Max(l => l.Width);
            double w = Math.Min(NodeW, textW + 2 * PadX), h = wrapped.Count * lineH + 2 * PadY;
            double y0 = cy - h / 2;
            minY = Math.Min(minY, y0);
            maxY = Math.Max(maxY, y0 + h);
            maxX = Math.Max(maxX, x + w);

            string fill, stroke = colour, textColour, suffixColour;
            double strokeW = depth == 0 ? 0 : 1.5, fillOpacity = 1;
            if (depth == 0) { fill = colour; textColour = "#FFFFFF"; suffixColour = "#FFFFFF"; }
            else if (depth == 1) { fill = colour; fillOpacity = 0x33 / 255.0; textColour = dark ? "#E5E7EB" : "#1F2937"; suffixColour = colour; }
            else { fill = dark ? "#1F2937" : "#FFFFFF"; textColour = dark ? "#E5E7EB" : "#1F2937"; suffixColour = colour; }

            if (n.Page is int page)
                nodes.Append(CultureInfo.InvariantCulture, $"<a href=\"#\" data-page=\"{page}\" aria-label=\"{Esc(n.Title)}, page {page}\"><title>Go to page {page}</title>");
            else
                nodes.Append("<g>");
            // A solid backing keeps the see-through branch colour from showing the lines underneath.
            if (fillOpacity < 1)
                nodes.Append($"<rect x=\"{N(x)}\" y=\"{N(y0)}\" width=\"{N(w)}\" height=\"{N(h)}\" rx=\"8\" fill=\"{(dark ? "#111827" : "#FFFFFF")}\"/>");
            nodes.Append($"<rect x=\"{N(x)}\" y=\"{N(y0)}\" width=\"{N(w)}\" height=\"{N(h)}\" rx=\"{(depth == 0 ? 10 : 8)}\" fill=\"{fill}\"");
            if (fillOpacity < 1) nodes.Append($" fill-opacity=\"{N(fillOpacity)}\"");
            if (strokeW > 0) nodes.Append($" stroke=\"{stroke}\" stroke-width=\"{N(strokeW)}\"");
            nodes.Append("/>");
            nodes.Append($"<text font-size=\"{N(fontSize)}\" font-weight=\"{(bold ? 600 : 400)}\" fill=\"{textColour}\">");
            for (int i = 0; i < wrapped.Count; i++)
            {
                double baseline = y0 + PadY + i * lineH + fontSize * 1.02;
                bool own = wrapped[i].Text.Length == 0;
                if (!own) nodes.Append($"<tspan x=\"{N(x + PadX)}\" y=\"{N(baseline)}\">{Esc(wrapped[i].Text)}</tspan>");
                if (i == wrapped.Count - 1 && suffix != null)
                    nodes.Append(own
                        ? $"<tspan x=\"{N(x + PadX)}\" y=\"{N(baseline)}\" font-size=\"10.5\" font-weight=\"400\" fill=\"{suffixColour}\">{Esc(suffix)}</tspan>"
                        : $"<tspan dx=\"{N(SuffixGap)}\" font-size=\"10.5\" font-weight=\"400\" fill=\"{suffixColour}\">{Esc(suffix)}</tspan>");
            }
            nodes.Append("</text>").Append(n.Page != null ? "</a>" : "</g>");

            if (parentRight is { } pr)
            {
                double mid = (pr.X + x) / 2;
                lines.Append($"<path d=\"M{N(pr.X)},{N(pr.Y)} C{N(mid)},{N(pr.Y)} {N(mid)},{N(cy)} {N(x)},{N(cy)}\" fill=\"none\" stroke=\"{colour}\" stroke-width=\"{(depth == 1 ? "2.5" : "1.6")}\" stroke-opacity=\"0.8\"/>");
            }

            var right = (x + w, cy);
            double y = top;
            int k = 0;
            foreach (var c in n.Children)
            {
                double ch = Leaves(c) * RowH;
                Place(c, depth + 1, y, y + ch, right, depth == 0 ? Palette[k++ % Palette.Length] : colour);
                y += ch;
            }
        }
    }

    private static int Leaves(MindMapNode n) => n.Children.Count == 0 ? 1 : n.Children.Sum(Leaves);

    /// <summary>Breaks the title into lines that fit, leaving room on the last line for the page label where it fits.</summary>
    private static List<(string Text, double Width)> Wrap(string text, double maxWidth, double size, bool bold, double suffixWidth)
    {
        var result = new List<(string, double)>();
        var line = new StringBuilder();
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string tryLine = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && TextWidth(tryLine, size, bold) > maxWidth)
            {
                result.Add((line.ToString(), TextWidth(line.ToString(), size, bold)));
                line.Clear();
                tryLine = word;
            }
            // A single word longer than the box is broken by characters.
            while (TextWidth(tryLine, size, bold) > maxWidth && tryLine.Length > 1)
            {
                int fit = 1;
                while (fit < tryLine.Length && TextWidth(tryLine[..(fit + 1)], size, bold) <= maxWidth) fit++;
                result.Add((tryLine[..fit], TextWidth(tryLine[..fit], size, bold)));
                tryLine = tryLine[fit..];
            }
            line.Clear().Append(tryLine);
        }
        result.Add((line.ToString(), TextWidth(line.ToString(), size, bold)));
        // The page label rides on the last line, like the Windows app's inline run, or on a line of its own.
        if (suffixWidth > 0)
        {
            var last = result[^1];
            if (last.Item2 + suffixWidth <= maxWidth || last.Item1.Length == 0) result[^1] = (last.Item1, last.Item2 + suffixWidth);
            else result.Add(("", suffixWidth));
        }
        return result;
    }

    /// <summary>A rough width for a line of sans-serif text (the browser does the real measuring).</summary>
    private static double TextWidth(string s, double size, bool bold)
    {
        double em = 0;
        foreach (char c in s)
            em += c switch
            {
                ' ' => 0.28,
                'i' or 'l' or 'j' or '.' or ',' or ':' or ';' or '\'' or '|' or '!' => 0.26,
                'f' or 't' or 'r' or 'I' or '(' or ')' or '-' => 0.36,
                'm' or 'w' or 'M' or 'W' => 0.84,
                >= 'A' and <= 'Z' => 0.64,
                >= '0' and <= '9' => 0.55,
                _ when c > 0x2E80 => 1.0,   // CJK and the like are full width
                _ => 0.53,
            };
        return em * size * (bold ? 1.05 : 1.0);
    }

    private static string N(double v) => Math.Round(v, 2).ToString(CultureInfo.InvariantCulture);

    /// <summary>Escapes text for XML, dropping characters XML can't hold.</summary>
    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default:
                    if (c >= 0x20 || c == '\t') sb.Append(c);
                    else if (c is '\n' or '\r') sb.Append(' ');
                    break;
            }
        }
        return sb.ToString();
    }
}
