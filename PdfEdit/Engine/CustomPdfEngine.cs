using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfEdit.Services;

namespace PdfEdit.Engine;

/// <summary>
/// Custom C# PDF rendering engine — no native dependencies.
/// Parses the PDF binary format and rasterises pages using WPF's own drawing APIs
/// (DrawingContext → RenderTargetBitmap).
///
/// Coverage: FlateDecode / JPEG / PNG image streams, the standard 14 PDF fonts,
/// all common content stream operators (paths, text, color, XObjects).
/// Encrypted PDFs and JPEG2000 images are not supported in this initial version.
/// </summary>
public sealed class CustomPdfEngine : IPdfRenderer
{
    private const double PdfDpi  = 72.0;
    private const double WpfDpi  = 96.0;
    public const  double PtsToDips = WpfDpi / PdfDpi;  // ~1.3333

    private byte[]?                    _fileBytes;
    private PdfParser?                 _parser;
    private List<PdfDictionary>?       _pages;
    private (double W, double H)[]     _pageSizePts  = Array.Empty<(double, double)>();
    private int[]                      _pageRotations = Array.Empty<int>();
    private int                        _pageCount;
    private bool                       _disposed;

    private readonly SemaphoreSlim _lock = new(1, 1);

    // ── IPdfRenderer ─────────────────────────────────────────────────────────

    public int  PageCount          => _pageCount;
    public bool RendersAnnotations => false;  // custom engine renders page content only

    public async Task LoadAsync(string absolutePath)
    {
        await _lock.WaitAsync();
        try
        {
            _fileBytes  = await File.ReadAllBytesAsync(absolutePath);
            _parser     = null;
            _pages      = null;
            _pageSizePts  = Array.Empty<(double, double)>();
            _pageRotations = Array.Empty<int>();
            _pageCount  = 0;

            await Task.Run(ParseDocument);
        }
        finally { _lock.Release(); }
    }

    private void ParseDocument()
    {
        if (_fileBytes == null) return;
        _parser = PdfParser.Load(_fileBytes);
        _pages  = _parser.GetPages();
        _pageCount = _pages.Count;
        _pageSizePts   = new (double, double)[_pageCount];
        _pageRotations = new int[_pageCount];

        for (int i = 0; i < _pageCount; i++)
        {
            var page = _pages[i];
            _pageSizePts[i]   = GetMediaBox(page);
            _pageRotations[i] = GetRotation(page);
        }
    }

    public async Task<BitmapSource> RenderPageAsync(int pageIndex, double zoom = 1.0, double dpiScale = 1.0)
    {
        if (_parser == null || _pages == null)
            throw new InvalidOperationException("No document loaded.");

        double scale     = PtsToDips * zoom * dpiScale;
        double bitmapDpi = WpfDpi * dpiScale;

        var (wPts, hPts) = _pageSizePts[pageIndex];
        int rot = _pageRotations[pageIndex];
        bool swapped = rot is 90 or 270;
        int pixelW = Math.Max(1, (int)Math.Round((swapped ? hPts : wPts) * scale));
        int pixelH = Math.Max(1, (int)Math.Round((swapped ? wPts : hPts) * scale));

        var page      = _pages[pageIndex];
        var resources = _parser.GetResources(page);
        var content   = _parser.GetPageContent(page);

        return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            RenderPage(page, resources, content, pixelW, pixelH, wPts, hPts, scale, bitmapDpi, rot));
    }

    private BitmapSource RenderPage(PdfDictionary page, PdfDictionary? resources,
                                     byte[] content, int pixelW, int pixelH,
                                     double wPts, double hPts, double scale, double bitmapDpi, int rot)
    {
        var rtb = new RenderTargetBitmap(pixelW, pixelH, bitmapDpi, bitmapDpi, PixelFormats.Pbgra32);
        var dv  = new DrawingVisual();

        using (var dc = dv.RenderOpen())
        {
            // White page background
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pixelW, pixelH));

            // Apply page rotation (viewer transform)
            if (rot != 0)
            {
                var rotTransform = new RotateTransform(rot, pixelW / 2.0, pixelH / 2.0);
                dc.PushTransform(rotTransform);
            }

            // Render content
            try
            {
                var renderer = new PdfContentRenderer(dc, _parser!, resources, hPts, scale);
                renderer.Render(content);
            }
            catch { /* partial render — show what we have */ }

            if (rot != 0) dc.Pop();
        }

        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // ── Page metadata ─────────────────────────────────────────────────────────

    public (double WidthPts, double HeightPts) GetPageSizeInPoints(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pageSizePts.Length) return (0, 0);
        return _pageSizePts[pageIndex];
    }

    public int GetPageRotation(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pageRotations.Length) return 0;
        return _pageRotations[pageIndex];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (double W, double H) GetMediaBox(PdfDictionary page)
    {
        // MediaBox may be inherited; walk up the page tree
        PdfDictionary? node = page;
        while (node != null)
        {
            var arr = _parser!.ResolveDict(node.Get("MediaBox")) as PdfDictionary;
            // MediaBox is actually a PdfArray, not dict
            var boxObj = node.Get("MediaBox");
            if (_parser!.Resolve(boxObj ?? PdfNull.Instance) is PdfArray box && box.Count >= 4)
            {
                double x0 = GetArrReal(box, 0), y0 = GetArrReal(box, 1);
                double x1 = GetArrReal(box, 2), y1 = GetArrReal(box, 3);
                return (Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            }
            var parentObj = node.Get("Parent");
            node = parentObj != null ? _parser.ResolveDict(parentObj) : null;
        }
        return (595, 842); // A4 fallback
    }

    private int GetRotation(PdfDictionary page)
    {
        PdfDictionary? node = page;
        while (node != null)
        {
            var r = node.Get("Rotate") ?? node.Get("Rotation");
            if (r is PdfInteger ri) return (int)(ri.Value % 360);
            var parentObj = node.Get("Parent");
            node = parentObj != null ? _parser!.ResolveDict(parentObj) : null;
        }
        return 0;
    }

    private static double GetArrReal(PdfArray arr, int i) =>
        arr[i] switch { PdfReal r => r.Value, PdfInteger ii => ii.Value, _ => 0.0 };

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            _fileBytes = null;
            _parser    = null;
            _pages     = null;
            _disposed  = true;
        }
    }
}
