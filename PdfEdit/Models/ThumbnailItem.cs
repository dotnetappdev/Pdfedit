using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace PdfEdit.Models;

public class ThumbnailItem : INotifyPropertyChanged
{
    private BitmapSource? _thumbnail;
    private bool _isCurrentPage;

    public int PageIndex { get; init; }
    public int PageNumber => PageIndex + 1;

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; OnPropertyChanged(); }
    }

    public bool IsCurrentPage
    {
        get => _isCurrentPage;
        set { _isCurrentPage = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
