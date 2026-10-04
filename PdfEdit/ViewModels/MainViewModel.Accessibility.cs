using System.Windows;
using System.Windows.Input;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>Accessibility Checker and Create PDF from Office files.</summary>
public partial class MainViewModel
{
    private ICommand? _accessibilityCommand, _createFromOfficeCommand, _exportExcelCommand;

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

    // ── Export tables to Excel ────────────────────────────────────────────────
    public ICommand ExportExcelCommand => _exportExcelCommand ??= new AsyncRelayCommand(async () =>
    {
        if (_currentFilePath == null) return;
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export to Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath) + ".xlsx",
            AddExtension = true,
        };
        if (save.ShowDialog() != true) return;
        bool perPage = PageCount == 1 || Dialogs.AppDialog.ShowConfirm(
            "Put each page on its own sheet, or everything on one sheet?", "Export to Excel", "Sheet per page", "One sheet");
        string src = _currentFilePath, dest = save.FileName;
        try
        {
            StatusText = "Exporting to Excel…";
            int rows = await Task.Run(() => ExcelExportService.Export(src, dest, oneSheet: !perPage));
            StatusText = $"Exported {rows} row(s) to {System.IO.Path.GetFileName(dest)}.";
            if (rows == 0)
            {
                Dialogs.AppDialog.ShowInfo("No text was found to export. If this is a scan, run text recognition first (All tools → Scan & OCR).", "Export to Excel");
                return;
            }
            if (Dialogs.AppDialog.ShowConfirm($"Exported {rows} rows. Open the workbook now?", "Export to Excel", "Open", "Close"))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest) { UseShellExecute = true });
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Export to Excel failed.", ex); }
    }, () => HasDocument);
}
