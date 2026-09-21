namespace PdfEdit.Models;

/// <summary>
/// A user-placed signature image annotation.
/// Coordinates are in PDF points (72 pts = 1 inch), Y from bottom-left.
/// </summary>
public class PlacedSignature
{
    public int PageNumber { get; set; }   // 1-based
    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
}
