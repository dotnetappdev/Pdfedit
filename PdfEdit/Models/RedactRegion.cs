namespace PdfEdit.Models;

/// <summary>
/// A pending redaction box drawn by the user. Coordinates in PDF points, Y from bottom-left.
/// </summary>
public class RedactRegion
{
    public int    PageNumber { get; set; }
    public double Left      { get; set; }
    public double Bottom    { get; set; }
    public double Width     { get; set; }
    public double Height    { get; set; }
}
