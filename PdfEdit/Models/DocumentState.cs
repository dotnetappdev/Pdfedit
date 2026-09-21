namespace PdfEdit.Models;

public class DocumentState
{
    public int LastPageIndex { get; set; }
    public double LastZoom { get; set; } = 1.0;
    public DateTime LastOpened { get; set; } = DateTime.Now;
}

/// <summary>A recent-file entry exposing filename separately so XAML needs no converter.</summary>
public class RecentFileEntry
{
    public string FullPath { get; }
    public string FileName { get; }
    public string Directory { get; }

    public RecentFileEntry(string path)
    {
        FullPath = path;
        FileName = System.IO.Path.GetFileName(path);
        Directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
    }
}
