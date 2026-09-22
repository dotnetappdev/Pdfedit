namespace PdfEdit.Models;

public class PdfAttachmentInfo
{
    public string Name          { get; set; } = string.Empty;
    public long   FileSizeBytes { get; set; }
    public string Description   { get; set; } = string.Empty;

    public string SizeDisplay => FileSizeBytes switch
    {
        >= 1_000_000 => $"{FileSizeBytes / 1_000_000.0:F1} MB",
        >= 1_000     => $"{FileSizeBytes / 1_000.0:F0} KB",
        _            => $"{FileSizeBytes} B"
    };
}
