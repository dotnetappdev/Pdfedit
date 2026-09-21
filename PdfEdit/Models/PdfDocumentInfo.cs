namespace PdfEdit.Models;

public class PdfDocumentInfo
{
    public string FilePath { get; set; } = string.Empty;
    public int PageCount { get; set; }

    // Page sizes in PDF points (72 pts = 1 inch)
    public List<(double Width, double Height)> PageSizes { get; set; } = new();

    public bool HasAcroForm { get; set; }
    public List<FormFieldInfo> FormFields { get; set; } = new();

    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
}
