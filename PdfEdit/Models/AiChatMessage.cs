using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PdfEdit.Models;

public class AiChatMessage : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private bool _isStreaming;

    public string Role { get; set; } = "user";

    /// <summary>The reply exactly as the model sent it (including the actions block).</summary>
    public string Raw { get; set; } = string.Empty;

    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowTools)); }
    }

    /// <summary>True while the reply is still arriving.</summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        set { _isStreaming = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowTools)); }
    }

    /// <summary>Suggested next questions (shown as chips under the reply).</summary>
    public ObservableCollection<string> FollowUps { get; } = new();

    /// <summary>Changes the assistant proposes; each is applied only when the user clicks Apply.</summary>
    public ObservableCollection<AiActionItem> Actions { get; } = new();

    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsUser => Role == "user";
    public bool ShowTools => !IsUser && !IsStreaming && Content.Length > 0;
    public string TimeDisplay => Timestamp.ToString("HH:mm");

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public enum AiActionState { Pending, Applied, Failed, Undone }

/// <summary>One change proposed by the AI (fill a field, highlight text, rotate a page …).</summary>
public class AiActionItem : INotifyPropertyChanged
{
    private AiActionState _state;
    private string _message = "";

    public string Action { get; init; } = "";
    public Dictionary<string, string> Args { get; init; } = new();
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "";
    public Action? UndoAction { get; set; }

    public AiActionState State
    {
        get => _state;
        set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPending)); OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(StateText)); }
    }
    public string Message { get => _message; set { _message = value; OnPropertyChanged(); OnPropertyChanged(nameof(StateText)); } }
    public bool IsPending => State == AiActionState.Pending;
    public bool CanUndo => State == AiActionState.Applied && UndoAction != null;
    public string StateText => State switch
    {
        AiActionState.Applied => "✓ Applied" + (Message.Length > 0 ? $" — {Message}" : ""),
        AiActionState.Failed => "✕ " + Message,
        AiActionState.Undone => "Undone",
        _ => "",
    };

    public string Arg(string key) => Args.TryGetValue(key, out var v) ? v : "";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
