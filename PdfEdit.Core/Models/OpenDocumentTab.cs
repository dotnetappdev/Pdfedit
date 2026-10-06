using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace PdfEdit.Models;

/// <summary>A PDF open in a tab above the document.</summary>
public class OpenDocumentTab : INotifyPropertyChanged
{
    private string _path;
    private bool _isActive;

    public OpenDocumentTab(string path) => _path = path;

    public string Path
    {
        get => _path;
        set { _path = value; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); }
    }

    public string Title => System.IO.Path.GetFileName(_path);

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
