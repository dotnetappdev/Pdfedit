namespace PdfEdit.Models;

public enum SignatureMode { Draw, Type, Image }

public class SignatureData
{
    public SignatureMode Mode { get; set; }
    public byte[]? ImageBytes { get; set; }
}
