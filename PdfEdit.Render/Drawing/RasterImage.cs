namespace PdfEdit.Render.Drawing;

/// <summary>
/// A decoded image: BGRA pixels with straight alpha (<see cref="Pixels"/>), and/or the drawing
/// backend's own decoded image (<see cref="Native"/>, e.g. a WPF BitmapSource), so a JPEG that
/// needs no changes is never copied.
/// </summary>
public sealed class RasterImage
{
    public RasterImage(int width, int height, byte[]? pixels, object? native = null)
    {
        Width = width; Height = height; Pixels = pixels; Native = native;
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>BGRA, 4 bytes per pixel, rows top to bottom, straight alpha. Null when only <see cref="Native"/> is set.</summary>
    public byte[]? Pixels { get; }
    public object? Native { get; }
}
