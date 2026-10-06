namespace PdfEdit.Models;

public enum TextEditKind { Insert, Replace }

/// <summary>
/// Acrobat "Insert text" (a caret where text should go) and "Replace text" (struck-through text
/// plus a caret with the replacement). Coordinates in PDF points, Y from bottom-left: for Insert
/// the caret's box, for Replace the struck-through area. The text to insert / replace with is the
/// comment's note.
/// </summary>
public class TextEditMark
{
    public int PageNumber { get; set; }
    public TextEditKind Kind { get; set; }
    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Color { get; set; } = "#1565C0";
    public CommentInfo Comment { get; set; } = new();
}
