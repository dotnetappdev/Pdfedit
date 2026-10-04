using System.Windows;
using System.Windows.Input;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>Accessibility Checker and Create PDF from Office files.</summary>
public partial class MainViewModel
{
    private ICommand? _accessibilityCommand, _createFromOfficeCommand;

    // ── Accessibility ─────────────────────────────────────────────────────────
    public ICommand AccessibilityCheckCommand => _accessibilityCommand ??= new RelayCommand(() =>
    {
        if (_currentFilePath == null) return;
        var dlg = new Dialogs.AccessibilityDialog(
            () => _currentFilePath,
            async opt =>
            {
                string what = "";
                bool ok = await ModifyCurrentFileAsync((i, o) => what = AccessibilityService.Fix(i, o, opt), "Accessibility fixes");
                if (ok) ToastService.Instance.Success(what + ".");
                return ok;
            })
        { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    }, () => HasDocument);

    // ── Create PDF from Word, Excel, PowerPoint ───────────────────────────────
    public ICommand CreatePdfFromOfficeCommand => _createFromOfficeCommand ??= new AsyncRelayCommand(async () =>
    {
        var open = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Create PDF from Word, Excel or PowerPoint files",
            Filter = OfficeConversionService.FileFilter,
            Multiselect = true,
        };
        if (open.ShowDialog() != true || open.FileNames.Length == 0) return;
        await ConvertOfficeFilesAsync(open.FileNames);
    });

    /// <summary>Converts Office files to PDFs saved next to them, then opens the first.</summary>
    public async Task ConvertOfficeFilesAsync(IReadOnlyList<string> files)
    {
        if (OfficeConversionService.AvailableConverter() == null)
        {
            Dialogs.AppDialog.ShowInfo("Converting Word, Excel and PowerPoint files needs Microsoft Office or the free LibreOffice (libreoffice.org) installed on this PC.",
                "Create PDF");
            return;
        }
        var made = new List<string>();
        var failed = new List<string>();
        for (int i = 0; i < files.Count; i++)
        {
            string src = files[i];
            StatusText = $"Converting {System.IO.Path.GetFileName(src)} ({i + 1} of {files.Count})…";
            string dest = OfficeConversionService.OutputPathFor(src);
            try
            {
                await Task.Run(() => OfficeConversionService.Convert(src, dest));
                made.Add(dest);
            }
            catch (Exception ex)
            {
                failed.Add($"{System.IO.Path.GetFileName(src)}: {ex.Message}");
            }
        }
        StatusText = made.Count > 0 ? $"Created {made.Count} PDF(s)." : "Ready";
        if (failed.Count > 0)
            Dialogs.AppDialog.ShowInfo("These files couldn't be converted:\n\n" + string.Join("\n", failed), "Create PDF");
        if (made.Count == 0) return;
        if (_currentFilePath != null) SaveDocumentState();
        await LoadDocumentAsync(made[0]);
        ToastService.Instance.Success(made.Count == 1
            ? $"Created {System.IO.Path.GetFileName(made[0])}."
            : $"Created {made.Count} PDFs next to the originals; opened the first.");
    }
}
