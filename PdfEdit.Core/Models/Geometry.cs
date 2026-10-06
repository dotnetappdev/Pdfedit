namespace PdfEdit.Models;

/// <summary>
/// A point in PDF or page coordinates. Used instead of a UI framework's point type so the models
/// stay free of WPF; the WPF app converts with <c>new Point(p.X, p.Y)</c>.
/// </summary>
public readonly record struct PointD(double X, double Y);

/// <summary>
/// Text alignment for annotations. Same names and order as WPF's <c>TextAlignment</c>, so values
/// saved by older versions read back the same, and the app converts with a cast.
/// </summary>
public enum TextAlign { Left, Right, Center, Justify }
