using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// Scan &amp; OCR: make scanned pages searchable (an invisible text layer, as in the Windows app)
/// and turn camera photos into a PDF. OCR runs on the server with Tesseract.
/// </summary>
public partial class Editor
{
    [Inject] public OcrEngine Ocr { get; set; } = default!;

    /// <summary>
    /// Recognises the text on each upright page and adds it as an invisible layer, so the PDF can
    /// be searched and copied from. Pages that already have text are skipped when asked.
    /// </summary>
    public async Task RunOcrAsync(string languages, bool skipPagesWithText)
    {
        if (Doc == null) return;
        if (!await Ocr.IsAvailableAsync()) { Toast(OcrMissing, "error"); return; }
        await RunAsync("Recognising text…", async () =>
        {
            await CommitPendingAsync();
            var doc = Doc;
            var byPage = new Dictionary<int, List<OcrWord>>();
            int skipped = 0, rotated = 0;
            for (int i = 0; i < PageCount; i++)
            {
                if (Rotation(i) != 0) { rotated++; continue; }
                if (skipPagesWithText && PdfTextExtractorService.GetPageText(doc.CurrentPath, i + 1).Trim().Length > 20) { skipped++; continue; }
                Status($"Recognising text: page {i + 1} of {PageCount}…");
                StateHasChanged();
                var (w, h) = PageSize(i);
                var page = await doc.Renderer.RenderPageAsync(i, 1.0, 3.0 * 72 / 96);   // 3 pixels per point ≈ 216 dpi
                var png = PngEncoder.FromBgra(page.Pixels ?? [], page.PixelWidth, page.PixelHeight);
                var (_, words) = await Ocr.RecognizeAsync(png, page.PixelWidth, page.PixelHeight, w, h, languages);
                if (words.Count > 0) byPage[i + 1] = words;
            }
            if (byPage.Count == 0)
            {
                Toast(skipped + rotated == PageCount ? "Every page already has text, so there was nothing to recognise." : "No text was recognised.", skipped > 0 ? "" : "error");
                Status("OCR found nothing to add");
                return;
            }
            int wordsAdded = 0;
            await Store.ApplyAsync(doc, (src, dest) => wordsAdded = PdfToolsService.AddInvisibleTextLayer(src, dest, byPage));
            LoadValues();
            _observePages = true;
            Status($"Recognised {wordsAdded:N0} words on {byPage.Count} page{(byPage.Count == 1 ? "" : "s")} — the PDF can now be searched");
            Toast($"Text recognised on {byPage.Count} page{(byPage.Count == 1 ? "" : "s")}." +
                  (skipped > 0 ? $" {skipped} already had text." : "") + (rotated > 0 ? $" {rotated} rotated page{(rotated == 1 ? " was" : "s were")} skipped." : ""), "success");
        });
    }

    /// <summary>Photos (from the camera or files) become a PDF, one page each, optionally made searchable.</summary>
    public async Task ScanImagesAsync(IReadOnlyList<IBrowserFile> photos, bool ocr, string languages)
    {
        if (photos.Count == 0) return;
        await CombineFilesAsync(photos);
        if (Doc != null)
        {
            Doc.FileName = $"Scan {DateTime.Now:yyyy-MM-dd HHmm}.pdf";
            if (ocr) await RunOcrAsync(languages, skipPagesWithText: false);
        }
    }

    public const string OcrMissing =
        "OCR isn't set up on this server. Its owner installs Tesseract (for example: apt install tesseract-ocr) " +
        "or sets PdfEdit:Ocr:TesseractPath.";
}
