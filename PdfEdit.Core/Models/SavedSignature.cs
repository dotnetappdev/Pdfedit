namespace PdfEdit.Models;

public class SavedSignature
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "My Signature";
    public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
}
