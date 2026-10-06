using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfEdit.Render;
using PdfEdit.Render.Drawing;

namespace PdfEdit.Drawing.Wpf;

/// <summary>
/// PdfEdit.Render's drawing backend for WPF: pages are drawn on a DrawingVisual and rendered to a
/// RenderTargetBitmap (on the UI thread), with the system fonts and WPF's image decoders.
/// </summary>
public sealed class WpfDrawingBackend : IDrawingBackend
{
    public static WpfDrawingBackend Instance { get; } = new();

    public IFontProvider Fonts { get; } = new WpfFontProvider();

    public RenderedPage Render(int pixelWidth, int pixelHeight, double dpi, Action<IDrawingSurface> draw)
    {
        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            draw(new WpfDrawingSurface(dc));
        rtb.Render(dv);
        rtb.Freeze();
        return new RenderedPage(pixelWidth, pixelHeight, dpi, null, rtb);
    }

    public async Task<RenderedPage> RenderAsync(int pixelWidth, int pixelHeight, double dpi, Action<IDrawingSurface> draw)
    {
        // WPF drawing happens on the UI thread.
        if (Application.Current?.Dispatcher is { } dispatcher)
            return await dispatcher.InvokeAsync(() => Render(pixelWidth, pixelHeight, dpi, draw));
        return Render(pixelWidth, pixelHeight, dpi, draw);
    }

    public RasterImage? DecodeImage(byte[] encoded, bool needPixels)
    {
        var bmp = new BitmapImage();
        using (var ms = new MemoryStream(encoded))
        {
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
        }
        bmp.Freeze();
        if (!needPixels) return new RasterImage(bmp.PixelWidth, bmp.PixelHeight, null, bmp);

        var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        var px = new byte[w * h * 4];
        conv.CopyPixels(px, w * 4, 0);
        return new RasterImage(w, h, px);
    }
}
