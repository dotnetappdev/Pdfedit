using System.Windows;

namespace PdfEdit.Models;

/// <summary>
/// A user-placed free-text annotation (not tied to an AcroForm field).
/// Coordinates are in PDF points (72 pts = 1 inch), Y from bottom-left.
/// </summary>
public class FreeTextAnnotation
{
    public int PageNumber { get; set; }   // 1-based, matches iText7

    // PDF-space coordinates (points, bottom-left origin)
    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public string Text { get; set; } = string.Empty;
    public double FontSize { get; set; } = 12;
    public bool IsVertical { get; set; }
    public string FontColor { get; set; } = "#000000";
    public string FontFamily { get; set; } = "Arial";
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public bool IsUnderline { get; set; }
    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;
    public bool ForceUpperCase { get; set; }
}
