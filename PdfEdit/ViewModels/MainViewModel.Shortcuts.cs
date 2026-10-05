using System.Windows.Input;

namespace PdfEdit.ViewModels;

public partial class MainViewModel
{
    private ICommand? _undoAny, _redoAny;

    /// <summary>Ctrl+Z: undoes the last page / file change, else the last annotation change.</summary>
    public ICommand UndoAnyCommand => _undoAny ??= new RelayCommand(() =>
    {
        if (UndoCommand.CanExecute(null)) UndoCommand.Execute(null);
        else if (UndoAnnotationCommand.CanExecute(null)) UndoAnnotationCommand.Execute(null);
    }, () => UndoCommand.CanExecute(null) || UndoAnnotationCommand.CanExecute(null));

    public ICommand RedoAnyCommand => _redoAny ??= new RelayCommand(() =>
    {
        if (RedoCommand.CanExecute(null)) RedoCommand.Execute(null);
        else if (RedoAnnotationCommand.CanExecute(null)) RedoAnnotationCommand.Execute(null);
    }, () => RedoCommand.CanExecute(null) || RedoAnnotationCommand.CanExecute(null));
}
