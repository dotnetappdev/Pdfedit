namespace PdfEdit.Models;

public enum WatermarkPosition { Center, Top, Bottom, Tiled }
public enum WatermarkPages { All, Current, Range }

public class WatermarkOptions
{
    // Text or image (ImagePath set = image watermark)
    public string Text       { get; set; } = "DRAFT";
    public string? ImagePath { get; set; }
    /// <summary>Image width as a share of the page width (0.1 – 1).</summary>
    public float  ImageScale { get; set; } = 0.5f;

    public string FontName   { get; set; } = "Helvetica-Bold";   // a standard PDF font (see FontNameFor)

    /// <summary>The colour used when none is chosen ("Automatic"): mid grey.</summary>
    public const string AutomaticColor = "#808080";

    /// <summary>
    /// The standard PDF font for a family ("Helvetica", "Times" or "Courier") with bold and / or
    /// italic, e.g. Times + bold + italic → "Times-BoldItalic".
    /// </summary>
    public static string FontNameFor(string family, bool bold, bool italic) => family switch
    {
        "Times" => bold && italic ? "Times-BoldItalic" : bold ? "Times-Bold" : italic ? "Times-Italic" : "Times-Roman",
        "Courier" => bold && italic ? "Courier-BoldOblique" : bold ? "Courier-Bold" : italic ? "Courier-Oblique" : "Courier",
        _ => bold && italic ? "Helvetica-BoldOblique" : bold ? "Helvetica-Bold" : italic ? "Helvetica-Oblique" : "Helvetica",
    };

    /// <summary>The family, bold and italic of a standard font name (the reverse of <see cref="FontNameFor"/>).</summary>
    public static (string Family, bool Bold, bool Italic) SplitFontName(string name) =>
        (name.StartsWith("Times") ? "Times" : name.StartsWith("Courier") ? "Courier" : "Helvetica",
         name.Contains("Bold"), name.Contains("Italic") || name.Contains("Oblique"));
    public float  FontSize   { get; set; } = 72;
    public string Color      { get; set; } = "#FF808080";         // ARGB or RGB hex
    public float  Opacity    { get; set; } = 0.25f;

    /// <summary>Which way the watermark runs, like Word's Diagonal / Horizontal layouts.</summary>
    public WatermarkLayout Layout { get; set; } = WatermarkLayout.DiagonalUp;

    /// <summary>Corner to corner on each page. Kept for older callers: true is <see cref="WatermarkLayout.DiagonalUp"/>.</summary>
    public bool Diagonal
    {
        get => Layout is WatermarkLayout.DiagonalUp or WatermarkLayout.DiagonalDown;
        set
        {
            if (value && !Diagonal) Layout = WatermarkLayout.DiagonalUp;
            else if (!value && Diagonal) Layout = WatermarkLayout.Custom;
        }
    }

    /// <summary>Counter-clockwise degrees as seen on screen, for <see cref="WatermarkLayout.Custom"/>.</summary>
    public float  AngleDeg   { get; set; } = 45;

    /// <summary>The angle (counter-clockwise degrees, as seen) on a page <paramref name="w"/> × <paramref name="h"/>.</summary>
    public double AngleFor(double w, double h) => Layout switch
    {
        WatermarkLayout.DiagonalUp => Math.Atan2(h, w) * 180 / Math.PI,
        WatermarkLayout.DiagonalDown => -Math.Atan2(h, w) * 180 / Math.PI,
        WatermarkLayout.Horizontal => 0,
        WatermarkLayout.VerticalUp => 90,
        WatermarkLayout.VerticalDown => -90,
        _ => AngleDeg,
    };

    /// <summary>Length of the line through the page's centre at <paramref name="angleDeg"/>, edge to edge.</summary>
    public static double LineAcross(double w, double h, double angleDeg)
    {
        double a = angleDeg * Math.PI / 180, c = Math.Abs(Math.Cos(a)), s = Math.Abs(Math.Sin(a));
        return Math.Min(c < 1e-6 ? double.MaxValue : w / c, s < 1e-6 ? double.MaxValue : h / s);
    }

    public WatermarkPosition Position { get; set; } = WatermarkPosition.Center;

    /// <summary>True: behind the page's text and form fields (a background); false: on top.</summary>
    public bool   Behind     { get; set; } = true;

    public WatermarkPages Pages { get; set; } = WatermarkPages.All;
    public string PageRange  { get; set; } = "";   // e.g. "1-3, 5"

    // Kept for older callers
    public bool AllPages { get => Pages == WatermarkPages.All; set { if (value) Pages = WatermarkPages.All; } }
}

/// <summary>Which way a watermark runs across the page.</summary>
public enum WatermarkLayout
{
    /// <summary>Bottom-left corner to top-right corner.</summary>
    DiagonalUp,
    /// <summary>Top-left corner to bottom-right corner.</summary>
    DiagonalDown,
    Horizontal,
    /// <summary>Up the page, reading from bottom to top.</summary>
    VerticalUp,
    /// <summary>Down the page, reading from top to bottom.</summary>
    VerticalDown,
    /// <summary>At <see cref="WatermarkOptions.AngleDeg"/>.</summary>
    Custom,
}
