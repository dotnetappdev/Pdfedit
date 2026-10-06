namespace PdfEdit.Render.Drawing;

/// <summary>
/// Everything the PDF engine draws with. Coordinates are device pixels, y down. Each Push must be
/// matched by one <see cref="Pop"/>. Implement this (with <see cref="IDrawingBackend"/>) to render
/// pages with another UI framework or graphics library.
/// </summary>
public interface IDrawingSurface
{
    void PushTransform(Matrix2D transform);
    void PushClip(PathData clip);
    void PushOpacity(double opacity);
    void Pop();

    void FillRectangle(RgbaColor color, double x, double y, double width, double height);

    /// <summary>Fills and/or strokes a path (either may be null).</summary>
    void DrawPath(PathData path, RgbaColor? fill, StrokeStyle? stroke);

    /// <summary>Draws an image stretched into the given rectangle.</summary>
    void DrawImage(RasterImage image, double x, double y, double width, double height);

    /// <summary>
    /// Draws one character with its baseline origin at (0, 0), y down, at <paramref name="emSize"/>
    /// pixels per em. When <paramref name="font"/> has the glyph, it is stretched horizontally by
    /// <paramref name="xStretch"/>; otherwise the character is drawn in <paramref name="fallbackFamily"/>.
    /// </summary>
    void DrawGlyph(IGlyphFont? font, string fallbackFamily, char ch, double emSize, RgbaColor color, double xStretch);
}

/// <summary>A system font the backend can draw glyphs with.</summary>
public interface IGlyphFont
{
    string Family { get; }
    /// <summary>The glyph's advance width in ems, if the font has a glyph for <paramref name="ch"/>.</summary>
    bool TryGetAdvanceWidth(char ch, out double emWidth);
}

/// <summary>The fonts installed where the page is drawn.</summary>
public interface IFontProvider
{
    /// <summary>The installed family matching <paramref name="name"/> (ignoring case), or null.</summary>
    string? FindInstalledFamily(string name);

    /// <summary>A face of <paramref name="family"/> (weight 100–900), or null if it can't be loaded.</summary>
    IGlyphFont? GetFont(string family, int weight, bool italic);
}

/// <summary>
/// Creates drawing surfaces and decodes images for one UI framework or graphics library.
/// PdfEdit.Drawing.Wpf provides the WPF one.
/// </summary>
public interface IDrawingBackend
{
    IFontProvider Fonts { get; }

    /// <summary>Draws a page of <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/> on the calling thread.</summary>
    RenderedPage Render(int pixelWidth, int pixelHeight, double dpi, Action<IDrawingSurface> draw);

    /// <summary>Draws a page on whichever thread the backend needs (for WPF, the UI thread).</summary>
    Task<RenderedPage> RenderAsync(int pixelWidth, int pixelHeight, double dpi, Action<IDrawingSurface> draw);

    /// <summary>
    /// Decodes a JPEG (or other encoded image). With <paramref name="needPixels"/>, the result has
    /// <see cref="RasterImage.Pixels"/>; otherwise it may carry only the backend's own image.
    /// </summary>
    RasterImage? DecodeImage(byte[] encoded, bool needPixels);
}
