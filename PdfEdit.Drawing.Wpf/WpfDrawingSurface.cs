using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfEdit.Render.Drawing;

namespace PdfEdit.Drawing.Wpf;

/// <summary>PdfEdit.Render's drawing calls on a WPF DrawingContext.</summary>
public sealed class WpfDrawingSurface : IDrawingSurface
{
    private static readonly System.Windows.Markup.XmlLanguage English = System.Windows.Markup.XmlLanguage.GetLanguage("en-us");
    private readonly DrawingContext _dc;

    public WpfDrawingSurface(DrawingContext dc) => _dc = dc;

    public void PushTransform(Matrix2D m) => _dc.PushTransform(new MatrixTransform(m.ToWpf()));
    public void PushClip(PathData clip) => _dc.PushClip(ToGeometry(clip));
    public void PushOpacity(double opacity) => _dc.PushOpacity(opacity);
    public void Pop() => _dc.Pop();

    public void FillRectangle(RgbaColor color, double x, double y, double width, double height) =>
        _dc.DrawRectangle(Brush(color), null, new Rect(x, y, width, height));

    public void DrawPath(PathData path, RgbaColor? fill, StrokeStyle? stroke) =>
        _dc.DrawGeometry(fill is { } f ? Brush(f) : null, stroke != null ? Pen(stroke) : null, ToGeometry(path));

    public void DrawImage(RasterImage image, double x, double y, double width, double height)
    {
        var bitmap = ToBitmap(image);
        if (bitmap != null) _dc.DrawImage(bitmap, new Rect(x, y, width, height));
    }

    public void DrawGlyph(IGlyphFont? font, string fallbackFamily, char ch, double emSize, RgbaColor color, double xStretch)
    {
        var brush = Brush(color);
        if (font is WpfGlyphFont g && g.Typeface.CharacterToGlyphMap.TryGetValue(ch, out ushort glyphIdx))
        {
            bool stretched = Math.Abs(xStretch - 1) > 0.02;
            if (stretched) _dc.PushTransform(new ScaleTransform(xStretch, 1));
            try
            {
                var glyphRun = new GlyphRun(
                    glyphTypeface:   g.Typeface,
                    bidiLevel:       0,
                    isSideways:      false,
                    renderingEmSize: emSize,
                    pixelsPerDip:    1.0f,
                    glyphIndices:    new[] { glyphIdx },
                    baselineOrigin:  new Point(0, 0),
                    advanceWidths:   new[] { g.Typeface.AdvanceWidths[glyphIdx] * emSize },
                    glyphOffsets:    null,
                    characters:      new[] { ch },
                    deviceFontName:  null,
                    clusterMap:      null,
                    caretStops:      null,
                    language:        English);
                _dc.DrawGlyphRun(brush, glyphRun);
            }
            finally { if (stretched) _dc.Pop(); }
        }
        else
        {
            var ft = new FormattedText(ch.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(fallbackFamily), Math.Max(1, emSize), brush, 1.0);
            // DrawText positions by the top of the line box; shift up so the baseline sits at 0.
            _dc.DrawText(ft, new Point(0, -ft.Baseline));
        }
    }

    // ── Conversions ───────────────────────────────────────────────────────────

    private static SolidColorBrush Brush(RgbaColor c)
    {
        var b = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private static Pen Pen(StrokeStyle s)
    {
        var pen = new Pen(Brush(s.Color), s.Width);
        pen.StartLineCap = pen.EndLineCap = pen.DashCap = (PenLineCap)(int)s.Cap;
        pen.LineJoin = (PenLineJoin)(int)s.Join;
        pen.MiterLimit = s.MiterLimit;
        // WPF dash lengths are multiples of the pen's thickness.
        if (s.Dashes is { Count: > 0 } dashes && s.Width > 0)
            pen.DashStyle = new DashStyle(dashes.Select(d => d / s.Width), s.DashOffset / s.Width);
        pen.Freeze();
        return pen;
    }

    public static Geometry ToGeometry(PathData path)
    {
        var geo = new StreamGeometry { FillRule = path.EvenOdd ? FillRule.EvenOdd : FillRule.Nonzero };
        using (var ctx = geo.Open())
        {
            foreach (var fig in path.Figures)
            {
                ctx.BeginFigure(fig.Start.ToWpf(), fig.IsFilled, fig.IsClosed);
                foreach (var seg in fig.Segments)
                {
                    if (seg.Kind == PathSegmentKind.Line) ctx.LineTo(seg.P1.ToWpf(), seg.IsStroked, false);
                    else ctx.BezierTo(seg.P1.ToWpf(), seg.P2.ToWpf(), seg.P3.ToWpf(), seg.IsStroked, false);
                }
            }
        }
        geo.Freeze();
        return geo;
    }

    public static BitmapSource? ToBitmap(RasterImage image)
    {
        if (image.Native is BitmapSource native) return native;
        if (image.Pixels == null || image.Width <= 0 || image.Height <= 0) return null;
        try
        {
            var wb = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, image.Width, image.Height), image.Pixels, image.Width * 4, 0);
            wb.Freeze();
            return wb;
        }
        catch { return null; }
    }
}

internal static class WpfConversions
{
    public static Point ToWpf(this Point2D p) => new(p.X, p.Y);
    public static Matrix ToWpf(this Matrix2D m) => new(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY);
}
