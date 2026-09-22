using System.Collections.Concurrent;
using System.Text;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;

namespace PdfEdit.Services;

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
}
