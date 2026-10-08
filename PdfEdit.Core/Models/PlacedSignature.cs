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
    /// <summary>
    /// Degrees anticlockwise the signature is turned on the page (0/90/180/270) — the page's rotation
    /// when it was placed on a rotated page, so it reads upright there.
    /// </summary>
    public double Rotation { get; set; }
    // Written to the PDF as /NM "pdfedit:<id>" (see CommentInfo.Id)
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>A picture placed on the page rather than a signature (saved as the stamp's text, "Picture").</summary>
    public bool IsPicture { get; set; }
}
