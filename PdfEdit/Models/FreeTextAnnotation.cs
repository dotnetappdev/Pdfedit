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
    public double RotationAngle { get; set; } = 0.0;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsVertical => Math.Abs(RotationAngle - (-90.0)) < 0.5;
    public string FontColor { get; set; } = "#000000";
    public string FontFamily { get; set; } = "Arial";
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public bool IsUnderline { get; set; }
    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;
    public bool ForceUpperCase { get; set; }

    // Highlight-mode: renders as a semi-transparent colored rectangle instead of text
    public bool IsHighlight { get; set; }
    public string HighlightColor { get; set; } = "#80FFFF00"; // ARGB semi-transparent yellow

    // Locked annotations cannot be moved or deleted via the UI
    public bool IsLocked { get; set; }

    // Extra space between characters, in points (Acrobat Fill & Sign "character spacing" — used to
    // line typed text up with comb boxes). Written to the PDF as the Tc operator.
    public double CharacterSpacing { get; set; }

    // Acrobat-style auto-size: the box grows / shrinks to fit the text as you type. New text starts
    // with it on (Add Text); it is off by default so annotations saved by older versions keep their
    // wrapped layout, and it turns off once the box is resized by hand.
    public bool AutoSize { get; set; }

    // Author, date, note, replies and review status (Comments panel)
    public CommentInfo Comment { get; set; } = new();
}
