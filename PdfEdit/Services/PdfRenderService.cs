using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PdfEdit.Services;

/// <summary>
/// Renders PDF pages to WPF BitmapSource using Windows.Data.Pdf (built-in Windows PDF engine).
/// Scale factor: PDF points * (96/72) = WPF DIPs at 100% zoom.
/// </summary>
public class PdfRenderService : IPdfRenderer
{
    private PdfDocument? _pdfDoc;
    private bool _disposed;
    // Serialize renders so thumbnail and main-view renders never race on _pdfDoc
    private readonly SemaphoreSlim _renderLock = new(1, 1);

    public const double PointsPerInch = 72.0;
    public const double DipsPerInch = 96.0;
    public const double PointsToDips = DipsPerInch / PointsPerInch; // ~1.333

    public int PageCount => (int)(_pdfDoc?.PageCount ?? 0);

    public async Task LoadAsync(string absolutePath)
    {
        // Drain any in-flight renders before swapping the document
        await _renderLock.WaitAsync();
        try
        {
            _pdfDoc = null;
            var file = await StorageFile.GetFileFromPathAsync(absolutePath);
            _pdfDoc = await PdfDocument.LoadFromFileAsync(file);
        }
        finally
        {
            _renderLock.Release();
        }
    }

    /// <summary>
    /// Renders a page to a frozen BitmapSource at the specified zoom level.
    /// zoom 1.0 = 96 DPI (matches WPF default DIP units).
    /// </summary>
    public async Task<BitmapSource> RenderPageAsync(int pageIndex, double zoom = 1.0)
    {
        await _renderLock.WaitAsync();
        try
        {
            if (_pdfDoc == null)
                throw new InvalidOperationException("No document loaded.");

            using var page = _pdfDoc.GetPage((uint)pageIndex);

            var opts = new PdfPageRenderOptions
            {
                DestinationWidth  = (uint)Math.Max(1, Math.Round(page.Size.Width  * zoom)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * zoom)),
            };

            using var ras = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(ras, opts);

            // Copy to managed stream so BitmapImage can cache it after the WinRT stream closes
            var ms = new MemoryStream();
            await ras.AsStream().CopyToAsync(ms);
            ms.Seek(0, SeekOrigin.Begin);

            return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return (BitmapSource)bmp;
            });
        }
        finally
        {
            _renderLock.Release();
        }
    }

    /// <summary>
    /// Returns page size in PDF points (72 pts = 1 inch).
    /// Windows.Data.Pdf returns size in DIPs (96 DPI), so we convert back.
    /// </summary>
    public (double WidthPts, double HeightPts) GetPageSizeInPoints(int pageIndex)
    {
        // Intentionally not locked — called synchronously on the UI thread after load,
        // when no render task can be in flight.
        if (_pdfDoc == null || pageIndex >= PageCount) return (0, 0);
        using var page = _pdfDoc.GetPage((uint)pageIndex);
        return (page.Size.Width / PointsToDips, page.Size.Height / PointsToDips);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _pdfDoc = null;
            _disposed = true;
        }
    }
}
