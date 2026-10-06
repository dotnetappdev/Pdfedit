namespace PdfEdit.Render;

/// <summary>
/// A rendered page: BGRA pixels (<see cref="Pixels"/>, straight alpha, opaque for whole pages)
/// and/or the drawing backend's own bitmap (<see cref="Native"/>, e.g. a WPF BitmapSource).
/// </summary>
public sealed class RenderedPage
{
    public RenderedPage(int pixelWidth, int pixelHeight, double dpi, byte[]? pixels, object? native = null)
    {
        PixelWidth = pixelWidth; PixelHeight = pixelHeight; Dpi = dpi; Pixels = pixels; Native = native;
    }

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    /// <summary>Dots per inch the page was rendered at (96 × dpiScale).</summary>
    public double Dpi { get; }
    public byte[]? Pixels { get; }
    public object? Native { get; }
}
