using System.IO;
using System.Text;
using System.Text.Json;
using iText.IO.Font;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout.Element;
using iText.Layout.Layout;

namespace PdfEdit.Services;

/// <summary>A run of text on a page that's translated as one piece (a paragraph, a heading, a table cell).</summary>
public sealed class TranslationBlock
{
    public int Page { get; init; }
    public double Left, Bottom, Right, Top;
    public double LineHeight;
    public StringBuilder Text { get; } = new();
    public string? Translation { get; set; }
}

/// <summary>
/// Translate a PDF and keep its layout (UPDF / PDFgear style): the text is grouped into
/// paragraphs, translated by the AI in batches, and each paragraph is covered and rewritten in
/// place in the new language, shrinking the type where the translation runs longer.
/// </summary>
public static class TranslateService
{
    /// <summary>Paragraph-sized blocks for every page (or one page).</summary>
    public static List<TranslationBlock> GetBlocks(string pdfPath, int onlyPage = 0)
    {
        int pages;
        using (var pdf = new PdfDocument(new PdfReader(pdfPath))) pages = pdf.GetNumberOfPages();
        var blocks = new List<TranslationBlock>();
        for (int p = onlyPage > 0 ? onlyPage : 1; p <= (onlyPage > 0 ? onlyPage : pages); p++)
        {
            var chunks = PageTextLocator.GetChunks(pdfPath, p).OrderByDescending(c => Math.Round(c.MidY)).ThenBy(c => c.X0).ToList();
            var pageBlocks = new List<TranslationBlock>();
            foreach (var c in chunks)
            {
                double h = Math.Max(4, c.Top - c.Bottom);
                // The line just below the end of an open block, overlapping it sideways, similar size.
                var block = pageBlocks.LastOrDefault(b =>
                    b.Bottom - c.Top < h * 0.9 && b.Bottom - c.Top > -h * 0.4 &&
                    Math.Min(b.Right, c.X1) - Math.Max(b.Left, c.X0) > Math.Min(b.Right - b.Left, c.X1 - c.X0) * 0.3 &&
                    Math.Abs(b.LineHeight - h) < Math.Max(2, h * 0.35));
                if (block == null)
                {
                    block = new TranslationBlock { Page = p, Left = c.X0, Right = c.X1, Bottom = c.Bottom, Top = c.Top, LineHeight = h };
                    block.Text.Append(c.Text);
                    pageBlocks.Add(block);
                    continue;
                }
                string joined = block.Text.ToString();
                if (joined.EndsWith('-') && !joined.EndsWith(" -")) block.Text.Length--;   // hyphenated line break
                else block.Text.Append(' ');
                block.Text.Append(c.Text);
                block.Left = Math.Min(block.Left, c.X0);
                block.Right = Math.Max(block.Right, c.X1);
                block.Bottom = Math.Min(block.Bottom, c.Bottom);
            }
            blocks.AddRange(pageBlocks.Where(b => b.Text.ToString().Any(char.IsLetter)));
        }
        return blocks;
    }

    /// <summary>Translates the blocks in batches through <paramref name="complete"/> (prompt → reply).</summary>
    public static async Task TranslateAsync(List<TranslationBlock> blocks, string language,
        Func<string, CancellationToken, Task<string>> complete, IProgress<(int Done, int Total)> progress, CancellationToken ct)
    {
        int done = 0;
        foreach (var batch in Batches(blocks, 5000))
        {
            ct.ThrowIfCancellationRequested();
            var items = batch.Select((b, i) => new { i, t = b.Text.ToString() }).ToList();
            string prompt =
                $"Translate the \"t\" of each item into {language}. Keep names, numbers, dates, codes, e-mail addresses and URLs as they are, " +
                "keep about the same length, and keep the tone. Reply with only a JSON array of objects {\"i\": number, \"t\": translation}, " +
                "one for every item, in the same order, and nothing else.\n\n" + JsonSerializer.Serialize(items);
            string reply = await complete(prompt, ct);
            foreach (var (i, t) in ParseReply(reply))
                if (i >= 0 && i < batch.Count && !string.IsNullOrWhiteSpace(t)) batch[i].Translation = t;
            done += batch.Count;
            progress.Report((done, blocks.Count));
        }
    }

    private static IEnumerable<List<TranslationBlock>> Batches(List<TranslationBlock> blocks, int maxChars)
    {
        var batch = new List<TranslationBlock>();
        int chars = 0;
        foreach (var b in blocks)
        {
            if (batch.Count > 0 && (chars + b.Text.Length > maxChars || batch.Count >= 80)) { yield return batch; batch = new(); chars = 0; }
            batch.Add(b);
            chars += b.Text.Length;
        }
        if (batch.Count > 0) yield return batch;
    }

    private static IEnumerable<(int, string)> ParseReply(string reply)
    {
        int a = reply.IndexOf('['), z = reply.LastIndexOf(']');
        if (a < 0 || z <= a) yield break;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(reply[a..(z + 1)]); } catch { yield break; }
        using (doc)
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                int i = el.TryGetProperty("i", out var iv) && iv.TryGetInt32(out int n) ? n : -1;
                string? t = el.TryGetProperty("t", out var tv) ? tv.GetString() : null;
                if (t != null) yield return (i, t);
            }
    }

    /// <summary>Writes a copy of the PDF with each translated block covered and rewritten.</summary>
    public static void Write(string sourcePath, string destPath, List<TranslationBlock> blocks, string language)
    {
        using var pdf = new PdfDocument(new PdfReader(sourcePath), new PdfWriter(destPath));
        var font = FontFor(language);
        foreach (var group in blocks.Where(b => b.Translation != null).GroupBy(b => b.Page))
        {
            var page = pdf.GetPage(group.Key);
            var pdfCanvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdf);
            foreach (var b in group)
            {
                var rect = new Rectangle((float)b.Left - 1, (float)b.Bottom - 2, (float)(b.Right - b.Left) + 2, (float)(b.Top - b.Bottom) + 3);
                pdfCanvas.SaveState().SetFillColor(ColorConstants.WHITE).Rectangle(rect).Fill().RestoreState();

                var canvas = new iText.Layout.Canvas(pdfCanvas, rect);
                float size = (float)Math.Clamp(b.LineHeight * 0.95, 4, 72);
                Paragraph para;
                while (true)
                {
                    para = new Paragraph(b.Translation!).SetFont(font).SetFontSize(size).SetMultipliedLeading(1.05f)
                        .SetMargin(0).SetPadding(0).SetFontColor(ColorConstants.BLACK);
                    var renderer = para.CreateRendererSubTree().SetParent(canvas.GetRenderer());
                    var result = renderer.Layout(new LayoutContext(new LayoutArea(1, rect)));
                    if (result.GetStatus() == LayoutResult.FULL || size <= 4) break;
                    size *= 0.92f;
                }
                canvas.Add(para);
                canvas.Close();
            }
        }
        pdf.GetDocumentInfo().SetTitle($"{System.IO.Path.GetFileNameWithoutExtension(sourcePath)} ({language})");
    }

    /// <summary>
    /// A font that has the letters of the target language: Windows fonts, or the usual Linux / macOS
    /// ones (DejaVu, Liberation, Noto, WenQuanYi) on a server. Falls back to Helvetica (Latin only).
    /// </summary>
    private static PdfFont FontFor(string language)
    {
        string l = language.ToLowerInvariant();
        string[] candidates =
            l.Contains("chinese") || l.StartsWith("zh")
                ? (l.Contains("traditional") || l.Contains("taiwan") || l.Contains("hong kong")
                    ? new[] { "msjh.ttc,0", "mingliu.ttc,0", "NotoSansCJK-Regular.ttc,3", "NotoSansTC-Regular.otf", "wqy-microhei.ttc,0" }
                    : new[] { "msyh.ttc,0", "simsun.ttc,0", "NotoSansCJK-Regular.ttc,2", "NotoSansSC-Regular.otf", "wqy-microhei.ttc,0", "wqy-zenhei.ttc,0" })
            : l.Contains("japanese") || l.StartsWith("ja") ? new[] { "YuGothM.ttc,0", "msgothic.ttc,0", "meiryo.ttc,0", "NotoSansCJK-Regular.ttc,0", "NotoSansJP-Regular.otf" }
            : l.Contains("korean") || l.StartsWith("ko") ? new[] { "malgun.ttf", "NotoSansCJK-Regular.ttc,1", "NotoSansKR-Regular.otf" }
            : l.Contains("thai") ? new[] { "leelawad.ttf", "tahoma.ttf", "NotoSansThai-Regular.ttf" }
            : l.Contains("hindi") || l.Contains("marathi") || l.Contains("nepali") ? new[] { "Nirmala.ttf", "mangal.ttf", "NotoSansDevanagari-Regular.ttf" }
            : new[] { "arial.ttf", "segoeui.ttf", "tahoma.ttf", "DejaVuSans.ttf", "LiberationSans-Regular.ttf", "NotoSans-Regular.ttf" };
        foreach (var c in candidates)
        {
            var parts = c.Split(',');
            if (FindFont(parts[0]) is not { } path) continue;
            try
            {
                return PdfFontFactory.CreateFont(parts.Length > 1 ? $"{path},{parts[1]}" : path, PdfEncodings.IDENTITY_H,
                    PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
            }
            catch { }
        }
        return PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
    }

    private static Dictionary<string, string>? _fontFiles;

    /// <summary>A font file by name, from the system's font folders (searched once).</summary>
    private static string? FindFont(string fileName)
    {
        if (_fontFiles == null)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                "/usr/share/fonts", "/usr/local/share/fonts", System.IO.Path.Combine(home, ".fonts"),
                System.IO.Path.Combine(home, ".local", "share", "fonts"), "/System/Library/Fonts", "/Library/Fonts",
            };
            foreach (var folder in folders.Where(f => f.Length > 0 && Directory.Exists(f)))
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
                        map.TryAdd(System.IO.Path.GetFileName(f), f);
                }
                catch { }
            }
            _fontFiles = map;
        }
        return _fontFiles.TryGetValue(fileName, out var path) ? path : null;
    }
}
