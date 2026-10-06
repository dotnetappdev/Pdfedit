namespace PdfEdit.Render.Drawing;

/// <summary>A point in device pixels (or any 2-D space the caller chooses).</summary>
public readonly record struct Point2D(double X, double Y);

/// <summary>An 8-bit-per-channel colour with straight (not premultiplied) alpha.</summary>
public readonly record struct RgbaColor(byte A, byte R, byte G, byte B)
{
    public static RgbaColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);
    public static RgbaColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    public static readonly RgbaColor Black = FromRgb(0, 0, 0);
    public static readonly RgbaColor White = FromRgb(255, 255, 255);
    public static readonly RgbaColor Transparent = new(0, 255, 255, 255);

    /// <summary>This colour with its alpha multiplied by <paramref name="opacity"/> (0–1).</summary>
    public RgbaColor WithOpacity(double opacity) => new((byte)(opacity * A), R, G, B);
}

/// <summary>
/// A 2-D affine transform [M11 M12 0; M21 M22 0; OffsetX OffsetY 1] for row vectors: the same
/// layout as PDF's [a b c d e f] and WPF's Matrix, so <c>Multiply(a, b)</c> applies a, then b.
/// </summary>
public readonly record struct Matrix2D(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
{
    public static readonly Matrix2D Identity = new(1, 0, 0, 1, 0, 0);

    public static Matrix2D Multiply(Matrix2D a, Matrix2D b) => new(
        a.M11 * b.M11 + a.M12 * b.M21,
        a.M11 * b.M12 + a.M12 * b.M22,
        a.M21 * b.M11 + a.M22 * b.M21,
        a.M21 * b.M12 + a.M22 * b.M22,
        a.OffsetX * b.M11 + a.OffsetY * b.M21 + b.OffsetX,
        a.OffsetX * b.M12 + a.OffsetY * b.M22 + b.OffsetY);

    public static Matrix2D Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Rotation by <paramref name="degrees"/> (clockwise on a y-down screen) about (cx, cy).</summary>
    public static Matrix2D Rotation(double degrees, double cx, double cy)
    {
        double r = degrees * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        return new(cos, sin, -sin, cos, cx - cx * cos + cy * sin, cy - cx * sin - cy * cos);
    }

    public double Determinant => M11 * M22 - M12 * M21;

    public Point2D Transform(Point2D p) => new(p.X * M11 + p.Y * M21 + OffsetX, p.X * M12 + p.Y * M22 + OffsetY);
}

/// <summary>Line ends. Same members and order as WPF's PenLineCap.</summary>
public enum LineCap { Flat, Square, Round, Triangle }

/// <summary>Line corners. Same members and order as WPF's PenLineJoin.</summary>
public enum LineJoin { Miter, Bevel, Round }

/// <summary>How a path's outline is drawn. Lengths are in device pixels.</summary>
public sealed record StrokeStyle(RgbaColor Color, double Width, LineCap Cap, LineJoin Join, double MiterLimit,
    IReadOnlyList<double>? Dashes = null, double DashOffset = 0);
