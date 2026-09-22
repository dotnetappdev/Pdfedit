namespace PdfEdit.Models;

public class StickyNoteAnnotation
{
    public int    PageNumber { get; set; }
    public double Left      { get; set; }
    public double Bottom    { get; set; }
    public string Text      { get; set; } = string.Empty;
    public string Color     { get; set; } = "#FFFF88"; // yellow sticky
    public string Author    { get; set; } = string.Empty;
}
