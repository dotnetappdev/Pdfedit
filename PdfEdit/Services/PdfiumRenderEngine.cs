using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using iText.Kernel.Pdf;

namespace PdfEdit.Services;

/// <summary>
/// High-quality PDF renderer built on Pdfium — the same engine used by Google Chrome and
/// Microsoft Edge, derived from Adobe's source. Compared to the WinRT fallback it delivers:
///
///   • Sub-pixel LCD font hinting (OptimizeTextForLcd) — same as Adobe Reader
///   • PDF annotations rendered into the page (stamps, watermarks, ink, digital-sig appearances)
///   • Correct ICC colour profiles and blend-mode transparency
///   • High-DPI rendering: accepts dpiScale so the bitmap fills physical pixels without blur
///   • Correct unrotated MediaBox sizes so form-field overlay coordinates are always accurate
///   • White-filled background (transparent fills look wrong over dark WPF backgrounds)
/// </summary>
public sealed class PdfiumRenderEngine : IPdfRenderer
{
    // PDF user-space is 72 pt/inch; WPF logical units are 96 DIP/inch.
    private const double WpfDpi       = 96.0;
    private const double PdfPointDpi  = 72.0;
    public  const double PointsToDips = WpfDpi / PdfPointDpi;  // ≈1.3333

    // Docnet render flags matching Adobe Reader's default screen-view quality
    private const RenderFlags ScreenFlags =
        RenderFlags.RenderAnnotations    // bake stamps, watermarks, ink into bitmap
        | RenderFlags.OptimizeTextForLcd; // sub-pixel RGB hinting (Adobe Reader default)

    private string?  _filePath;
    private byte[]?  _fileBytes;
    private int      _pageCount;

    // Unrotated MediaBox sizes in PDF points (read from iText7 — authoritative per spec)
    private (double W, double H)[] _pageSizePts     = Array.Empty<(double, double)>();
    // Native /Rotate values from the PDF dictionary
    private int[]                  _pageRotations    = Array.Empty<int>();

    private bool _disposed;

    // Docnet singleton — not thread-safe; guard every call with lock(Lib)
    private static readonly IDocLib Lib = Docnet.Core.DocLib.Instance;
    private readonly SemaphoreSlim  _lock = new(1, 1);

    // ── IPdfRenderer ────────────────────────────────────────────────────────────

    public int  PageCount           => _pageCount;
    public bool RendersAnnotations  => true;  // we pass RenderFlags.RenderAnnotations

    // ── Load ────────────────────────────────────────────────────────────────────

    public async Task LoadAsync(string absolutePath)
    {
        await _lock.WaitAsync();
        try
        {
            _filePath  = absolutePath;
            _fileBytes = await File.ReadAllBytesAsync(absolutePath);
            _pageCount = 0;
            _pageSizePts  = Array.Empty<(double, double)>();
            _pageRotations = Array.Empty<int>();

            // Use iText7 for page sizes and /Rotate values — these are the authoritative
            // numbers the form-field coordinate math depends on (MediaBox, unrotated).
            await Task.Run(() => ReadPageMetadataFromPdf(absolutePath));
        }
        finally
        {
            _lock.Release();
        }
    }

    private void ReadPageMetadataFromPdf(string path)
    {
        using var reader = new PdfReader(path);
        using var doc    = new PdfDocument(reader);
        int n = doc.GetNumberOfPages();
        _pageCount     = n;
        _pageSizePts   = new (double, double)[n];
        _pageRotations = new int[n];
        for (int i = 0; i < n; i++)
        {
            var page   = doc.GetPage(i + 1);
            var size   = page.GetPageSize();          // MediaBox, always unrotated
            _pageSizePts[i]   = (size.GetWidth(), size.GetHeight());
            _pageRotations[i] = page.GetRotation();   // 0 / 90 / 180 / 270
        }
    }

    // ── Render ──────────────────────────────────────────────────────────────────

    public async Task<BitmapSource> RenderPageAsync(int pageIndex, double zoom = 1.0, double dpiScale = 1.0)
    {
        if (_fileBytes == null)
            throw new InvalidOperationException("No document loaded.");

        // scalingFactor = PDF-points → physical pixels
        // = (96 DIP/pt-inch ratio) × zoom × dpiScale
        double scalingFactor = PointsToDips * zoom * dpiScale;
        double bitmapDpi     = WpfDpi * dpiScale;

        var (wPts, hPts) = _pageSizePts[pageIndex];

        // Account for pages that are rotated 90° or 270° — Pdfium swaps W/H when rendering
        int rot = _pageRotations[pageIndex];
        bool swapped = (rot == 90 || rot == 270);
        int pixelW = Math.Max(1, (int)Math.Round((swapped ? hPts : wPts) * scalingFactor));
        int pixelH = Math.Max(1, (int)Math.Round((swapped ? wPts : hPts) * scalingFactor));

        byte[] bgra = await Task.Run(() => RenderPageToBytes(pageIndex, scalingFactor));

        return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            MakeBitmap(bgra, pixelW, pixelH, bitmapDpi));
    }

    // ── Pdfium render (thread-pool) ──────────────────────────────────────────────

    private byte[] RenderPageToBytes(int pageIndex, double scalingFactor)
    {
        if (_fileBytes == null) return Array.Empty<byte>();
        lock (Lib)
        {
            using var doc  = Lib.GetDocReader(_fileBytes, new PageDimensions(scalingFactor));
            using var page = doc.GetPageReader(pageIndex);

            // Pre-fill white — Pdfium renders transparent backgrounds on pages that don't
            // specify a background colour (most PDFs). Adobe Reader fills white by default.
            return FillWhite(page.GetImage(ScreenFlags), page.GetPageWidth(), page.GetPageHeight());
        }
    }

    // ── Pre-fill white background ────────────────────────────────────────────────

    private static byte[] FillWhite(byte[] bgra, int width, int height)
    {
        // BGRA: blend each pixel over white (0xFF, 0xFF, 0xFF)
        // dst = src_alpha/255 * src_rgb + (1 - src_alpha/255) * 255
        for (int i = 0; i < bgra.Length; i += 4)
        {
            byte alpha = bgra[i + 3];
            if (alpha == 255) continue;       // fully opaque — nothing to do
            if (alpha == 0)                   // fully transparent → white
            {
                bgra[i]     = 255;
                bgra[i + 1] = 255;
                bgra[i + 2] = 255;
                bgra[i + 3] = 255;
                continue;
            }
            float a = alpha / 255f;
            float inv = 1f - a;
            bgra[i]     = (byte)Math.Min(255, (int)(bgra[i]     * a + 255 * inv));
            bgra[i + 1] = (byte)Math.Min(255, (int)(bgra[i + 1] * a + 255 * inv));
            bgra[i + 2] = (byte)Math.Min(255, (int)(bgra[i + 2] * a + 255 * inv));
            bgra[i + 3] = 255;
        }
        return bgra;
    }

    // ── Build frozen WriteableBitmap ─────────────────────────────────────────────

    private static WriteableBitmap MakeBitmap(byte[] bgra, int width, int height, double dpi)
    {
        var bmp = new WriteableBitmap(width, height, dpi, dpi, PixelFormats.Bgra32, null);
        bmp.Lock();
        try
        {
            Marshal.Copy(bgra, 0, bmp.BackBuffer, bgra.Length);
            bmp.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            bmp.Unlock();
        }
        bmp.Freeze();
        return bmp;
    }

    // ── Page metadata ────────────────────────────────────────────────────────────

    /// <summary>Unrotated MediaBox size in PDF points — always portrait regardless of /Rotate.</summary>
    public (double WidthPts, double HeightPts) GetPageSizeInPoints(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pageSizePts.Length) return (0, 0);
        return _pageSizePts[pageIndex];
    }

    /// <summary>Native /Rotate value from the PDF dictionary (0, 90, 180, or 270).</summary>
    public int GetPageRotation(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pageRotations.Length) return 0;
        return _pageRotations[pageIndex];
    }

    // ── Dispose ──────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            _fileBytes = null;
            _disposed  = true;
        }
    }
}
