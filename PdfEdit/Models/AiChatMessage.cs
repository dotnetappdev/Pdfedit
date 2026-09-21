using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PdfEdit.Models;

public class AiChatMessage : INotifyPropertyChanged
{
    private string _content = string.Empty;

    public string Role { get; set; } = "user";

    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }

    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsUser => Role == "user";
    public string TimeDisplay => Timestamp.ToString("HH:mm");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
