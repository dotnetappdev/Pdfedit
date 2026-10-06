using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfEdit.Render;

namespace PdfEdit.Drawing.Wpf;

/// <summary>Gives the WPF app BitmapSources from one of PdfEdit.Render's UI-free renderers.</summary>
public sealed class WpfPdfRenderer : IPdfRenderer
{
    private readonly IPageRenderer _inner;

    public WpfPdfRenderer(IPageRenderer inner) => _inner = inner;

    public int PageCount => _inner.PageCount;
    public bool RendersAnnotations => _inner.RendersAnnotations;
    public Task LoadAsync(string absolutePath) => _inner.LoadAsync(absolutePath);
    public (double WidthPts, double HeightPts) GetPageSizeInPoints(int pageIndex) => _inner.GetPageSizeInPoints(pageIndex);
    public int GetPageRotation(int pageIndex) => _inner.GetPageRotation(pageIndex);
    public void Dispose() => _inner.Dispose();

    public async Task<BitmapSource> RenderPageAsync(int pageIndex, double zoom = 1.0, double dpiScale = 1.0)
    {
        var page = await _inner.RenderPageAsync(pageIndex, zoom, dpiScale);
        if (page.Native is BitmapSource native) return native;
        if (Application.Current?.Dispatcher is { } dispatcher)
            return await dispatcher.InvokeAsync(() => page.ToBitmapSource());
        return page.ToBitmapSource();
    }
}

public static class RenderedPageExtensions
{
    /// <summary>A frozen BitmapSource for a rendered page (its own WPF bitmap, or one made from its pixels).</summary>
    public static BitmapSource ToBitmapSource(this RenderedPage page)
    {
        if (page.Native is BitmapSource native) return native;
        var bmp = new WriteableBitmap(page.PixelWidth, page.PixelHeight, page.Dpi, page.Dpi, PixelFormats.Bgra32, null);
        if (page.Pixels is { } px)
        {
            // WritePixels honours the back buffer's stride (Marshal.Copy into BackBuffer does not)
            int stride = page.PixelWidth * 4;
            int rows = Math.Min(page.PixelHeight, px.Length / stride);
            if (rows > 0) bmp.WritePixels(new Int32Rect(0, 0, page.PixelWidth, rows), px, stride, 0);
        }
        bmp.Freeze();
        return bmp;
    }
}
