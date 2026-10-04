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

    public string FontName   { get; set; } = "Helvetica-Bold";   // a standard PDF font
    public float  FontSize   { get; set; } = 72;
    public string Color      { get; set; } = "#FF808080";         // ARGB or RGB hex
    public float  Opacity    { get; set; } = 0.25f;

    /// <summary>Corner to corner on each page (ignores <see cref="AngleDeg"/>).</summary>
    public bool   Diagonal   { get; set; } = true;
    /// <summary>Counter-clockwise degrees as seen on screen.</summary>
    public float  AngleDeg   { get; set; } = 45;

    public WatermarkPosition Position { get; set; } = WatermarkPosition.Center;

    /// <summary>True: behind the page's text and form fields (a background); false: on top.</summary>
    public bool   Behind     { get; set; } = true;

    public WatermarkPages Pages { get; set; } = WatermarkPages.All;
    public string PageRange  { get; set; } = "";   // e.g. "1-3, 5"

    // Kept for older callers
    public bool AllPages { get => Pages == WatermarkPages.All; set { if (value) Pages = WatermarkPages.All; } }
}
