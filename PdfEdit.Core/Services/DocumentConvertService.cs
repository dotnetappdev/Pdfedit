using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using iText.Html2pdf;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace PdfEdit.Services;

/// <summary>A block of text read from a PDF page: a heading, a list item or a paragraph.</summary>
public sealed record PdfTextBlock(PdfTextBlockKind Kind, string Text);

public enum PdfTextBlockKind { Heading1, Heading2, Heading3, ListItem, Paragraph }

/// <summary>
/// Conversions PDFgear has beyond Office: PDF → HTML, Markdown and ePub (text with its headings,
/// lists and paragraphs, page by page), and text, Markdown and HTML → PDF.
/// </summary>
public static class DocumentConvertService
{
    // ── Reading a PDF's structure ────────────────────────────────────────────

    private sealed record Run(string Text, float X0, float X1, float Baseline, float Size, bool Bold);

    private sealed class RunCollector : IEventListener
    {
        public readonly List<Run> Runs = new();
        public void EventOccurred(IEventData data, EventType type)
        {
            if (data is not TextRenderInfo info) return;
            string text = info.GetText();
            if (string.IsNullOrEmpty(text)) return;
            if (info.GetTextRenderMode() == 3 && text.Trim().Length == 0) return;
            var start = info.GetBaseline().GetStartPoint();
            var end = info.GetBaseline().GetEndPoint();
            float size = info.GetAscentLine().GetStartPoint().Get(1) - info.GetDescentLine().GetStartPoint().Get(1);
            string font = info.GetFont()?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "";
            bool bold = font.Contains("Bold", StringComparison.OrdinalIgnoreCase) || font.Contains("Black", StringComparison.OrdinalIgnoreCase);
            Runs.Add(new Run(text, start.Get(0), end.Get(0), start.Get(1), Math.Max(1, size), bold));
        }
        public ICollection<EventType> GetSupportedEvents() => new[] { EventType.RENDER_TEXT };
    }

    /// <summary>The text of each page as headings, list items and paragraphs, in reading order.</summary>
    public static List<List<PdfTextBlock>> ReadStructure(string pdfPath)
    {
        using var doc = new PdfDocument(new PdfReader(pdfPath));
        var pages = new List<List<PdfTextBlock>>();
        for (int n = 1; n <= doc.GetNumberOfPages(); n++)
        {
            var collector = new RunCollector();
            try { new PdfCanvasProcessor(collector).ProcessPageContent(doc.GetPage(n)); } catch { }
            pages.Add(BuildBlocks(collector.Runs));
        }
        return pages;
    }

    private sealed record Line(string Text, float Baseline, float Size, bool Bold, float X0);

    private static List<PdfTextBlock> BuildBlocks(List<Run> runs)
    {
        var blocks = new List<PdfTextBlock>();
        if (runs.Count == 0) return blocks;

        // Runs → lines: same baseline (within a third of the text size), left to right.
        var lines = new List<Line>();
        foreach (var group in runs.OrderByDescending(r => r.Baseline).ThenBy(r => r.X0)
                                  .Aggregate(new List<List<Run>>(), (acc, r) =>
                                  {
                                      var last = acc.LastOrDefault();
                                      if (last != null && Math.Abs(last[0].Baseline - r.Baseline) <= Math.Max(2, r.Size * 0.35)) last.Add(r);
                                      else acc.Add(new List<Run> { r });
                                      return acc;
                                  }))
        {
            var ordered = group.OrderBy(r => r.X0).ToList();
            var sb = new StringBuilder();
            float? lastEnd = null;
            foreach (var r in ordered)
            {
                // A gap wider than a fraction of the text size is a space the PDF didn't spell out.
                if (lastEnd != null && r.X0 - lastEnd > r.Size * 0.18 && sb.Length > 0 && sb[^1] != ' ' && !r.Text.StartsWith(' '))
                    sb.Append(' ');
                sb.Append(r.Text);
                lastEnd = r.X1;
            }
            string lineText = sb.ToString().Trim();
            if (lineText.Length == 0) continue;
            float size = ordered.GroupBy(r => MathF.Round(r.Size)).OrderByDescending(g => g.Sum(r => r.Text.Length)).First().Key;
            bool bold = ordered.Sum(r => r.Bold ? r.Text.Length : 0) * 2 > ordered.Sum(r => r.Text.Length);
            lines.Add(new Line(lineText, group[0].Baseline, size, bold, ordered[0].X0));
        }
        if (lines.Count == 0) return blocks;

        // Body size: the size most of the text is in.
        float body = lines.GroupBy(l => l.Size).OrderByDescending(g => g.Sum(l => l.Text.Length)).First().Key;

        PdfTextBlockKind KindOf(Line l)
        {
            if (l.Text.Length <= 120)
            {
                if (l.Size >= body * 1.7f) return PdfTextBlockKind.Heading1;
                if (l.Size >= body * 1.3f) return PdfTextBlockKind.Heading2;
                if (l.Bold && l.Size >= body * 0.95f && l.Text.Length <= 80 && !l.Text.EndsWith('.')) return PdfTextBlockKind.Heading3;
            }
            return IsListMarker(l.Text) ? PdfTextBlockKind.ListItem : PdfTextBlockKind.Paragraph;
        }

        // Lines → blocks: a paragraph carries on while lines keep the same size and normal spacing.
        PdfTextBlockKind? kind = null;
        var text = new StringBuilder();
        Line? prev = null;
        void Flush()
        {
            if (kind != null && text.Length > 0) blocks.Add(new PdfTextBlock(kind.Value, text.ToString().Trim()));
            text.Clear();
            kind = null;
        }
        foreach (var l in lines)
        {
            var k = KindOf(l);
            bool join = prev != null && kind == k && k is PdfTextBlockKind.Paragraph or PdfTextBlockKind.ListItem
                        && Math.Abs(prev!.Size - l.Size) < 0.6f
                        && prev.Baseline - l.Baseline <= l.Size * 1.9f
                        && !(k == PdfTextBlockKind.ListItem && IsListMarker(l.Text));
            if (prev != null && kind == k && k is PdfTextBlockKind.Heading1 or PdfTextBlockKind.Heading2 or PdfTextBlockKind.Heading3
                && prev.Baseline - l.Baseline <= l.Size * 1.6f)
                join = true;   // a heading wrapped onto two lines
            if (!join) Flush();
            kind = k;
            if (text.Length > 0)
            {
                if (text[^1] == '-' && text.Length > 1 && char.IsLetter(text[^2])) text.Length--;   // re-join hyphenated words
                else text.Append(' ');
            }
            text.Append(l.Text);
            prev = l;
        }
        Flush();
        return blocks;
    }

    private static bool IsListMarker(string t) =>
        t.Length > 1 && ("•●▪◦‣-–*".Contains(t[0]) && char.IsWhiteSpace(t.ElementAtOrDefault(1))
                         || System.Text.RegularExpressions.Regex.IsMatch(t, @"^(\d{1,3}|[a-zA-Z])[\.\)]\s"));

    private static string StripMarker(string t) =>
        System.Text.RegularExpressions.Regex.Replace(t, @"^([•●▪◦‣\-–*]|\d{1,3}[\.\)]|[a-zA-Z][\.\)])\s+", "");

    private static string Title(string pdfPath)
    {
        try
        {
            using var doc = new PdfDocument(new PdfReader(pdfPath));
            var t = doc.GetDocumentInfo().GetTitle();
            if (!string.IsNullOrWhiteSpace(t)) return t.Trim();
        }
        catch { }
        return System.IO.Path.GetFileNameWithoutExtension(pdfPath);
    }

    // ── PDF → HTML / Markdown / ePub ─────────────────────────────────────────

    /// <summary>One HTML page: each PDF page a section with its headings, lists and paragraphs.</summary>
    public static int ToHtml(string pdfPath, string htmlPath)
    {
        var pages = ReadStructure(pdfPath);
        string title = Title(pdfPath);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
          .Append("<title>").Append(WebUtility.HtmlEncode(title)).Append("</title>\n")
          .Append("<style>body{font-family:Segoe UI,Arial,sans-serif;max-width:46em;margin:2em auto;padding:0 1em;line-height:1.55;color:#222}" +
                  "section{border-bottom:1px solid #ddd;padding-bottom:1.5em;margin-bottom:1.5em}.page{color:#888;font-size:.8em}</style>\n</head>\n<body>\n");
        for (int i = 0; i < pages.Count; i++)
        {
            sb.Append("<section id=\"page-").Append(i + 1).Append("\">\n<p class=\"page\">Page ").Append(i + 1).Append("</p>\n");
            AppendHtmlBlocks(sb, pages[i]);
            sb.Append("</section>\n");
        }
        sb.Append("</body>\n</html>\n");
        File.WriteAllText(htmlPath, sb.ToString(), new UTF8Encoding(false));
        return pages.Count;
    }

    private static void AppendHtmlBlocks(StringBuilder sb, List<PdfTextBlock> blocks)
    {
        bool inList = false;
        foreach (var b in blocks)
        {
            if (b.Kind != PdfTextBlockKind.ListItem && inList) { sb.Append("</ul>\n"); inList = false; }
            string t = WebUtility.HtmlEncode(b.Kind == PdfTextBlockKind.ListItem ? StripMarker(b.Text) : b.Text);
            switch (b.Kind)
            {
                case PdfTextBlockKind.Heading1: sb.Append("<h1>").Append(t).Append("</h1>\n"); break;
                case PdfTextBlockKind.Heading2: sb.Append("<h2>").Append(t).Append("</h2>\n"); break;
                case PdfTextBlockKind.Heading3: sb.Append("<h3>").Append(t).Append("</h3>\n"); break;
                case PdfTextBlockKind.ListItem:
                    if (!inList) { sb.Append("<ul>\n"); inList = true; }
                    sb.Append("<li>").Append(t).Append("</li>\n");
                    break;
                default: sb.Append("<p>").Append(t).Append("</p>\n"); break;
            }
        }
        if (inList) sb.Append("</ul>\n");
    }

    /// <summary>Markdown: # headings, - lists and paragraphs, a rule between pages.</summary>
    public static int ToMarkdown(string pdfPath, string mdPath)
    {
        var pages = ReadStructure(pdfPath);
        var sb = new StringBuilder();
        for (int i = 0; i < pages.Count; i++)
        {
            if (i > 0) sb.Append("\n---\n\n");
            foreach (var b in pages[i])
            {
                string t = EscapeMarkdown(b.Kind == PdfTextBlockKind.ListItem ? StripMarker(b.Text) : b.Text);
                sb.Append(b.Kind switch
                {
                    PdfTextBlockKind.Heading1 => "# ",
                    PdfTextBlockKind.Heading2 => "## ",
                    PdfTextBlockKind.Heading3 => "### ",
                    PdfTextBlockKind.ListItem => "- ",
                    _ => "",
                }).Append(t).Append(b.Kind == PdfTextBlockKind.ListItem ? "\n" : "\n\n");
            }
        }
        File.WriteAllText(mdPath, sb.ToString().TrimEnd() + "\n", new UTF8Encoding(false));
        return pages.Count;
    }

    private static string EscapeMarkdown(string t)
    {
        var sb = new StringBuilder(t.Length);
        foreach (char c in t)
        {
            if (c is '\\' or '*' or '_' or '`' or '[' or ']' or '<' or '>' or '#') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>An ePub 3 e-book: one chapter per page, with a table of contents from the headings.</summary>
    public static int ToEpub(string pdfPath, string epubPath)
    {
        var pages = ReadStructure(pdfPath);
        string title = Title(pdfPath);
        string id = "urn:uuid:" + Guid.NewGuid();
        if (File.Exists(epubPath)) File.Delete(epubPath);
        using var zip = ZipFile.Open(epubPath, ZipArchiveMode.Create);

        // The mimetype entry must come first and be stored uncompressed.
        Write(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(zip, "META-INF/container.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
            "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>\n");

        var manifest = new StringBuilder();
        var spine = new StringBuilder();
        var nav = new StringBuilder();
        for (int i = 0; i < pages.Count; i++)
        {
            string file = $"page{i + 1}.xhtml";
            var body = new StringBuilder();
            AppendHtmlBlocks(body, pages[i]);
            if (pages[i].Count == 0) body.Append("<p></p>\n");
            Write(zip, "OEBPS/" + file,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"en\">\n" +
                $"<head><meta charset=\"utf-8\"/><title>{WebUtility.HtmlEncode(title)}, page {i + 1}</title></head>\n<body>\n{body}</body>\n</html>\n");
            manifest.Append($"<item id=\"p{i + 1}\" href=\"{file}\" media-type=\"application/xhtml+xml\"/>\n");
            spine.Append($"<itemref idref=\"p{i + 1}\"/>\n");
            string label = pages[i].FirstOrDefault(b => b.Kind is PdfTextBlockKind.Heading1 or PdfTextBlockKind.Heading2)?.Text ?? $"Page {i + 1}";
            if (label.Length > 80) label = label[..80] + "…";
            nav.Append($"<li><a href=\"{file}\">{WebUtility.HtmlEncode(label)}</a></li>\n");
        }
        Write(zip, "OEBPS/nav.xhtml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"en\">\n" +
            $"<head><meta charset=\"utf-8\"/><title>{WebUtility.HtmlEncode(title)}</title></head>\n<body>\n<nav epub:type=\"toc\" id=\"toc\"><h1>Contents</h1><ol>\n{nav}</ol></nav>\n</body>\n</html>\n");
        Write(zip, "OEBPS/content.opf",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\">\n" +
            "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n" +
            $"<dc:identifier id=\"bookid\">{id}</dc:identifier>\n<dc:title>{WebUtility.HtmlEncode(title)}</dc:title>\n<dc:language>en</dc:language>\n" +
            $"<meta property=\"dcterms:modified\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>\n</metadata>\n" +
            $"<manifest>\n<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n{manifest}</manifest>\n" +
            $"<spine>\n{spine}</spine>\n</package>\n");
        return pages.Count;
    }

    private static void Write(ZipArchive zip, string name, string content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var s = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    // ── Text / Markdown / HTML → PDF ─────────────────────────────────────────

    /// <summary>File types <see cref="ToPdf"/> takes.</summary>
    public static readonly string[] SourceExtensions = { ".txt", ".md", ".markdown", ".html", ".htm" };

    /// <summary>
    /// Makes a PDF from a text, Markdown or HTML file on <paramref name="pageSize"/> paper (A4 if
    /// null). HTML keeps its styling; Markdown gets headings, lists, tables and code; text keeps its lines.
    /// </summary>
    public static void ToPdf(string source, string pdfPath, PageSize? pageSize = null)
    {
        string ext = System.IO.Path.GetExtension(source).ToLowerInvariant();
        string html = ext switch
        {
            ".html" or ".htm" => File.ReadAllText(source),
            ".md" or ".markdown" => Wrap(Markdig.Markdown.ToHtml(File.ReadAllText(source),
                                         Markdig.MarkdownExtensions.UseAdvancedExtensions(new Markdig.MarkdownPipelineBuilder()).Build()), source),
            _ => Wrap("<pre class=\"plain\">" + WebUtility.HtmlEncode(File.ReadAllText(source)) + "</pre>", source),
        };
        var props = new ConverterProperties();
        string? folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(source));
        if (folder != null) props.SetBaseUri(folder + System.IO.Path.DirectorySeparatorChar);   // pictures next to the file

        using var writer = new PdfWriter(pdfPath);
        using var pdf = new PdfDocument(writer);
        pdf.SetDefaultPageSize(pageSize ?? PageSize.A4);
        HtmlConverter.ConvertToPdf(html, pdf, props);
    }

    private static string Wrap(string bodyHtml, string source) =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" + WebUtility.HtmlEncode(System.IO.Path.GetFileNameWithoutExtension(source)) +
        "</title><style>" +
        "@page{margin:2cm}body{font-family:Helvetica,Arial,sans-serif;font-size:11pt;line-height:1.45;color:#222}" +
        "h1{font-size:22pt}h2{font-size:17pt}h3{font-size:13pt}pre,code{font-family:Courier,monospace;font-size:9.5pt}" +
        "pre{background:#f4f4f4;padding:8pt;white-space:pre-wrap}pre.plain{background:none;padding:0;font-family:Helvetica,Arial,sans-serif;font-size:11pt}" +
        "table{border-collapse:collapse}td,th{border:1px solid #bbb;padding:3pt 6pt}blockquote{border-left:3pt solid #ccc;margin-left:0;padding-left:10pt;color:#555}" +
        "</style></head><body>" + bodyHtml + "</body></html>";
}
