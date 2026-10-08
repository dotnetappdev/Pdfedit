using PdfEdit.Models;

namespace PdfEdit.Services;

/// <summary>The batch OCR step in the Windows app: renders the pages and reads them with Tesseract.</summary>
public static class BatchOcr
{
    /// <summary>OCR pages that have no text and add an invisible text layer.</summary>
    public static async Task OcrAsync(string src, string dest, CancellationToken ct)
    {
        const double zoom = 2.0; // same as the single-file OCR
        using var renderer = RendererFactory.Create();
        await renderer.LoadAsync(src);
        var byPage = new Dictionary<int, List<OcrWord>>();
        for (int i = 0; i < renderer.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (PdfTextExtractorService.GetPageText(src, i + 1).Trim().Length > 20) continue;
            var (w, h) = renderer.GetPageSizeInPoints(i);
            var bmp = await renderer.RenderPageAsync(i, zoom);
            var (_, words) = await OcrService.RecognizeAsync(bmp, w, h);
            byPage[i + 1] = words;
        }
        await Task.Run(() => PdfToolsService.AddInvisibleTextLayer(src, dest, byPage), ct);
    }
}
