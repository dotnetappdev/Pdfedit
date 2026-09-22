namespace PdfEdit.Services;

public sealed class AnnotationAction
{
    public required Action Execute     { get; init; }
    public required Action Undo       { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Lightweight undo/redo stack for in-memory annotation operations.
/// Clears automatically when the document changes.
/// </summary>
public sealed class AnnotationUndoService
{
    private readonly Stack<AnnotationAction> _undoStack = new();
    private readonly Stack<AnnotationAction> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public string UndoLabel => _undoStack.TryPeek(out var a) ? $"Undo {a.Description}" : "Undo";
    public string RedoLabel => _redoStack.TryPeek(out var a) ? $"Redo {a.Description}" : "Redo";

    /// <summary>Pushes an action. Does NOT execute it — call this after already performing the action.</summary>
    public void Push(AnnotationAction action) { _undoStack.Push(action); _redoStack.Clear(); }

    public void Undo() { if (!CanUndo) return; var a = _undoStack.Pop(); a.Undo(); _redoStack.Push(a); }
    public void Redo() { if (!CanRedo) return; var a = _redoStack.Pop(); a.Execute(); _undoStack.Push(a); }

    public void Clear() { _undoStack.Clear(); _redoStack.Clear(); }
}
