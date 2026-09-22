namespace PdfEdit.Models;

/// <summary>
/// A dragged highlight (or underline / strikethrough) annotation.
/// Coordinates are in PDF points (72 pts = 1 inch), Y from bottom-left.
/// </summary>
public class HighlightAnnotation
{
    public int    PageNumber { get; set; }   // 1-based
    public double Left      { get; set; }
    public double Bottom    { get; set; }
    public double Width     { get; set; }
    public double Height    { get; set; }
    public string Color     { get; set; } = "#FFFF00";  // hex colour of the highlight
    public float  Opacity   { get; set; } = 0.4f;
    public HighlightKind Kind { get; set; } = HighlightKind.Highlight;
}

public enum HighlightKind { Highlight, Underline, Strikethrough }
