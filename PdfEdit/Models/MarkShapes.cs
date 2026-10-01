using System.Windows;

namespace PdfEdit.Models;

/// <summary>
/// Vector shapes for the Fill &amp; Sign marks (✓ ✕ ● ○ —), drawn like Acrobat's rather than as
/// font glyphs: on screen and in the saved PDF they share these strokes. Coordinates are in a
/// unit square, x right / y down.
/// </summary>
public static class MarkShapes
{
    public enum Kind { Check, Cross, Dot, Circle, Line }

    public static Kind? FromGlyph(string? text) => text switch
    {
        "✓" => Kind.Check,
        "✕" => Kind.Cross,
        "●" => Kind.Dot,
        "○" => Kind.Circle,
        "—" => Kind.Line,
        _ => null,
    };

    /// <summary>Open polylines to stroke (empty for the dot / circle).</summary>
    public static Point[][] Strokes(Kind kind) => kind switch
    {
        // Acrobat's tick: short down-stroke, long up-stroke
        Kind.Check => new[] { new[] { new Point(0.14, 0.54), new Point(0.40, 0.80), new Point(0.88, 0.20) } },
        Kind.Cross => new[]
        {
            new[] { new Point(0.20, 0.20), new Point(0.80, 0.80) },
            new[] { new Point(0.80, 0.20), new Point(0.20, 0.80) },
        },
        Kind.Line => new[] { new[] { new Point(0.08, 0.5), new Point(0.92, 0.5) } },
        _ => Array.Empty<Point[]>(),
    };

    /// <summary>Stroke width as a fraction of the square's side.</summary>
    public static double StrokeWidth(Kind kind) => kind switch
    {
        Kind.Check  => 0.14,
        Kind.Cross  => 0.12,
        Kind.Circle => 0.08,
        Kind.Line   => 0.10,
        _           => 0,
    };

    /// <summary>Circle centre (0.5, 0.5) radius, for the dot (filled) and circle (stroked).</summary>
    public static double Radius(Kind kind) => kind switch
    {
        Kind.Dot    => 0.26,
        Kind.Circle => 0.40,
        _           => 0,
    };

    /// <summary>The line mark spans the box's width; the others sit in a centred square.</summary>
    public static bool FillsWidth(Kind kind) => kind == Kind.Line;
}
