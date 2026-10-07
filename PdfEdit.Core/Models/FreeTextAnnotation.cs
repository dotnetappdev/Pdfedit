
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
    public TextAlign TextAlignment { get; set; } = TextAlign.Left;
    public bool ForceUpperCase { get; set; }

    // Highlight-mode: renders as a semi-transparent colored rectangle instead of text
    public bool IsHighlight { get; set; }
    public string HighlightColor { get; set; } = "#80FFFF00"; // ARGB semi-transparent yellow

    // Locked annotations cannot be moved or deleted via the UI
    public bool IsLocked { get; set; }

    // Extra space between characters, in points (Complete & Sign "character spacing" — used to
    // line typed text up with comb boxes). Written to the PDF as the Tc operator.
    public double CharacterSpacing { get; set; }

    // standard auto-size: the box grows / shrinks to fit the text as you type. New text starts
    // with it on (Add Text); it is off by default so annotations saved by older versions keep their
    // wrapped layout, and it turns off once the box is resized by hand.
    public bool AutoSize { get; set; }

    // When the box is not auto-sized: wrap the text at the box width and grow the box downwards so
    // text that is bigger than the box is never cut off (toolbar "Fit" menu → "Wrap text, grow box").
    public bool GrowToFit { get; set; } = true;

    // Text that is a date (Date stamp, or typed as one): the date and the .NET pattern it is shown
    // in, so the Properties panel can switch format / change day, month, year (null otherwise).
    public DateTime? DateValue { get; set; }

    // Rubber stamp (APPROVED, SIGN HERE …): drawn as a bordered stamp — Text is the title, the
    // subtitle is the "By … at …" line of a dynamic stamp. Saved to the PDF as a /Stamp annotation.
    public bool IsStamp { get; set; }
    public string? StampSubtitle { get; set; }
    public string? DateFormat { get; set; }

    // Author, date, note, replies and review status (Comments panel)
    public CommentInfo Comment { get; set; } = new();
}
