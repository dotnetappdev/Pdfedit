namespace PdfEdit.Models;

public class PdfMetadataInfo
{
    public string Title    { get; set; } = string.Empty;
    public string Author   { get; set; } = string.Empty;
    public string Subject  { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public string Creator  { get; set; } = string.Empty;
    public string Producer { get; set; } = string.Empty;
    public int    PageCount { get; set; }
    public long   FileSizeBytes { get; set; }
}
