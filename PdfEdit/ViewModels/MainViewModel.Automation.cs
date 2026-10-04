using System.Windows;
using System.Windows.Input;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>Automation and document-wide tools: batch processing, sanitising.</summary>
public partial class MainViewModel
{
    private ICommand? _batchCommand, _sanitizeCommand, _bulkFillCommand;

    /// <summary>Bulk fill: one filled copy of this form per spreadsheet row.</summary>
    public ICommand BulkFillCommand => _bulkFillCommand ??= new RelayCommand(() =>
    {
        if (_currentFilePath == null) return;
        if (AllFields.Count == 0)
        {
            Dialogs.AppDialog.ShowInfo("This PDF has no fillable fields. Add fields with Prepare Form (or Detect Fields) and save first.", "Bulk fill");
            return;
        }
        new Dialogs.BulkFillDialog(_currentFilePath) { Owner = Application.Current.MainWindow }.ShowDialog();
    }, () => HasDocument);

    /// <summary>Batch processing (Acrobat's Action Wizard) over many PDFs.</summary>
    public ICommand BatchCommand => _batchCommand ??= new RelayCommand(() =>
    {
        var dlg = new Dialogs.BatchDialog(_currentFilePath) { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    });

    /// <summary>Remove hidden information from the open PDF (undoable).</summary>
    public ICommand SanitizeCommand => _sanitizeCommand ??= new AsyncRelayCommand(async () =>
    {
        var dlg = new Dialogs.SanitizeDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;
        var opt = dlg.Options;
        string what = "";
        if (await ModifyCurrentFileAsync((i, o) => what = SanitizeService.Sanitize(i, o, opt), "Hidden information removed"))
            ToastService.Instance.Success($"Removed {what}.");
    }, () => HasDocument);
}
