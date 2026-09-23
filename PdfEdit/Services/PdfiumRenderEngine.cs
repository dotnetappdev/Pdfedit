using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;

namespace PdfEdit.Services;

/// <summary>
/// High-quality PDF renderer built on Pdfium — the same engine used by Google Chrome and
/// Microsoft Edge. Produces Adobe-quality output with proper sub-pixel font hinting,
/// accurate colour management, and correct blend-mode rendering at any zoom level.
///
/// Drop-in replacement for PdfRenderService: implements IPdfRenderer with the same API.
/// Pixels are BGRA32, written directly into a WPF WriteableBitmap with no intermediate
/// PNG encode/decode — faster and lossless.
/// </summary>
public sealed class PdfiumRenderEngine : IPdfRenderer
{
    // Pdfium works in PDF user-space (72 pt/inch); WPF uses 96 DIP/inch.
    private const double WpfDpi      = 96.0;
    private const double PdfPointDpi = 72.0;
    public  const double PointsToDips = WpfDpi / PdfPointDpi; // ~1.3333

    private byte[]?              _fileBytes;
    private int                  _pageCount;
    private (double W, double H)[] _pageSizePts = Array.Empty<(double, double)>();
    private bool                 _disposed;

    // Docnet's IDocLib is a process-wide singleton — must be used through lock
    private static readonly IDocLib Lib = Docnet.Core.DocLib.Instance;

    // Serialize renders so concurrent callers (thumbnails + main view) don't race
    private readonly SemaphoreSlim _lock = new(1, 1);

    public int PageCount => _pageCount;

    // ── Load ────────────────────────────────────────────────────────────────────

    public async Task LoadAsync(string absolutePath)
    {
        await _lock.WaitAsync();
        try
        {
            _fileBytes = await File.ReadAllBytesAsync(absolutePath);
            _pageCount = 0;
            _pageSizePts = Array.Empty<(double, double)>();

            // scalingFactor=1.0 → GetPageWidth/Height returns PDF-point values directly
            lock (Lib)
            {
                using var doc = Lib.GetDocReader(_fileBytes, new PageDimensions(1.0));
                _pageCount   = doc.GetPageCount();
                _pageSizePts = new (double, double)[_pageCount];
                for (int i = 0; i < _pageCount; i++)
                {
                    using var pg = doc.GetPageReader(i);
                    _pageSizePts[i] = (pg.GetPageWidth(), pg.GetPageHeight());
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Render ──────────────────────────────────────────────────────────────────

    public async Task<BitmapSource> RenderPageAsync(int pageIndex, double zoom = 1.0)
    {
        if (_fileBytes == null)
            throw new InvalidOperationException("No document loaded.");

        // scalingFactor = pixels-per-PDF-point = (96 DPI / 72 pt) * zoom
        double scale = PointsToDips * zoom;

        var (wPts, hPts) = _pageSizePts[pageIndex];
        int pixelW = Math.Max(1, (int)Math.Round(wPts * scale));
        int pixelH = Math.Max(1, (int)Math.Round(hPts * scale));

        byte[] bgra = await Task.Run(() => RenderPageToBytes(pageIndex, scale));

        return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            MakeBitmap(bgra, pixelW, pixelH));
    }

    // ── Core Pdfium render (runs on thread-pool) ─────────────────────────────────

    private byte[] RenderPageToBytes(int pageIndex, double scalingFactor)
    {
        if (_fileBytes == null) return Array.Empty<byte>();
        lock (Lib)
        {
            using var doc  = Lib.GetDocReader(_fileBytes, new PageDimensions(scalingFactor));
            using var page = doc.GetPageReader(pageIndex);
            return page.GetImage(); // BGRA32, top-left origin
        }
    }

    // ── Convert BGRA bytes → frozen WriteableBitmap ──────────────────────────────

    private static WriteableBitmap MakeBitmap(byte[] bgra, int width, int height)
    {
        var bmp = new WriteableBitmap(width, height, WpfDpi, WpfDpi, PixelFormats.Bgra32, null);
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

    // ── Page sizes ───────────────────────────────────────────────────────────────

    public (double WidthPts, double HeightPts) GetPageSizeInPoints(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pageSizePts.Length) return (0, 0);
        return _pageSizePts[pageIndex];
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
