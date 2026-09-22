using System.Collections.ObjectModel;

namespace PdfEdit.Models;

public class BookmarkItem
{
    public string Title    { get; set; } = string.Empty;
    public int    PageNumber { get; set; }           // 1-based; 0 = unknown
    public ObservableCollection<BookmarkItem> Children { get; } = new();
}
