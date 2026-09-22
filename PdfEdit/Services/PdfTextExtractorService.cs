using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace PdfEdit.Services;

public readonly record struct TextMatch(int PageNumber, float Left, float Bottom, float Width, float Height);

public static class PdfTextExtractorService
{
    private static readonly ConcurrentDictionary<(string path, int page), string> _pageCache = new();

    public static string GetPageText(string pdfPath, int pageNumber)
    {
        var key = (pdfPath, pageNumber);
        if (_pageCache.TryGetValue(key, out var cached)) return cached;
        try
        {
            using var reader = new PdfReader(pdfPath);
            using var doc = new PdfDocument(reader);
            if (pageNumber < 1 || pageNumber > doc.GetNumberOfPages()) return string.Empty;
            var text = PdfTextExtractor.GetTextFromPage(doc.GetPage(pageNumber));
            _pageCache[key] = text ?? string.Empty;
            return _pageCache[key];
        }
        catch { return string.Empty; }
    }

    public static string GetDocumentText(string pdfPath, int maxChars = 24000)
    {
        try
        {
            using var reader = new PdfReader(pdfPath);
            using var doc = new PdfDocument(reader);
            var sb = new StringBuilder();
            for (int i = 1; i <= doc.GetNumberOfPages() && sb.Length < maxChars; i++)
            {
                var text = PdfTextExtractor.GetTextFromPage(doc.GetPage(i));
                if (!string.IsNullOrWhiteSpace(text))
                    sb.Append($"[Page {i}]\n{text.Trim()}\n\n");
            }
            var result = sb.ToString();
            return result.Length > maxChars ? result[..maxChars] + "\n[...truncated]" : result;
        }
        catch { return string.Empty; }
    }

    public static void InvalidateCache(string pdfPath)
    {
        foreach (var key in _pageCache.Keys.Where(k => k.path == pdfPath).ToList())
            _pageCache.TryRemove(key, out _);
    }

    // Returns all bounding-box matches for the given query across all pages (or a specific page).
    public static List<TextMatch> FindTextPositions(string pdfPath, string query, int specificPage = 0)
    {
        var results = new List<TextMatch>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        try
        {
            string pattern = Regex.Escape(query);
            using var reader = new PdfReader(pdfPath);
            using var doc    = new PdfDocument(reader);

            int from = specificPage > 0 ? specificPage : 1;
            int to   = specificPage > 0 ? specificPage : doc.GetNumberOfPages();

            for (int pg = from; pg <= to; pg++)
            {
                var strategy  = new RegexBasedLocationExtractionStrategy(pattern);
                var processor = new PdfCanvasProcessor(strategy);
                processor.ProcessPageContent(doc.GetPage(pg));

                foreach (var loc in strategy.GetResultantLocations())
                {
                    var rect = loc.GetRectangle();
                    results.Add(new TextMatch(
                        PageNumber: pg,
                        Left:   (float)rect.GetX(),
                        Bottom: (float)rect.GetY(),
                        Width:  (float)rect.GetWidth(),
                        Height: (float)rect.GetHeight()));
                }
            }
        }
        catch { /* silently skip inaccessible pages */ }

        return results;
    }
}
