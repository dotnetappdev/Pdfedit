using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>Automation and document-wide tools: batch processing, sanitising.</summary>
public partial class MainViewModel
{
    private ICommand? _batchCommand, _sanitizeCommand, _bulkFillCommand, _detectFieldsCommand;

    /// <summary>
    /// Acrobat Prepare Form's auto-detect: find the boxes, squares and blank lines of a flat form
    /// on every page, name them from their printed labels, and add them as fillable fields.
    /// </summary>
    public ICommand DetectFieldsCommand => _detectFieldsCommand ??= new AsyncRelayCommand(DetectFieldsAsync, () => HasDocument);

    private async Task DetectFieldsAsync()
    {
        if (_document == null || _currentFilePath == null) return;
        string path = _currentFilePath;
        var found = new List<NewField>();
        try
        {
            IsLoading = true;
            for (int i = 0; i < _document.PageCount; i++)
            {
                StatusText = $"Looking for fields: page {i + 1} of {_document.PageCount}…";
                if (GetPageRotation(i) != 0) continue; // detection assumes an upright page
                var (wPt, hPt) = _document.PageSizes[i];
                var bmp = await _renderService.RenderPageAsync(i, 2.0);
                var gray = new FormatConvertedBitmap(bmp, PixelFormats.Gray8, null, 0);
                int w = gray.PixelWidth, h = gray.PixelHeight;
                var lum = new byte[w * h];
                gray.CopyPixels(lum, w, 0);
                double s = w / wPt;
                int page = i + 1;
                var boxes = await Task.Run(() => FieldDetector.Detect(lum, w, h, s));
                var chunks = await Task.Run(() => PageTextLocator.GetChunks(path, page));
                var existing = AllFields.Where(f => f.PageNumber == page).ToList();
                foreach (var b in boxes)
                {
                    double left = b.Left / s, top = hPt - b.Top / s, right = b.Right / s, bottom = hPt - b.Bottom / s;
                    // Already a field there?
                    if (existing.Any(f => f.Left < right && f.Left + f.Width > left && f.Bottom < top && f.Bottom + f.Height > bottom)) continue;
                    double pad = b.IsCheckBox ? 1 : 0.5;
                    found.Add(new NewField
                    {
                        Page = page, IsCheckBox = b.IsCheckBox, Multiline = !b.IsCheckBox && top - bottom > 34,
                        Left = left + pad, Bottom = bottom + pad, Width = right - left - pad * 2, Height = top - bottom - pad * 2,
                        Name = PageTextLocator.LabelFor(chunks, left, bottom, right, top, b.IsCheckBox) ?? "",
                    });
                }
            }
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Field detection failed.", ex); return; }
        finally { IsLoading = false; }

        if (found.Count == 0)
        {
            StatusText = "No empty boxes or blank lines found.";
            Dialogs.AppDialog.ShowInfo("No new fields were found. Detection looks for empty ruled boxes, small squares and blank lines; you can still add fields by hand with Prepare Form.", "Detect fields");
            return;
        }
        var dlg = new Dialogs.DetectFieldsDialog(found) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || dlg.Selected.Count == 0) return;
        var chosen = dlg.Selected.ToList();
        if (await ModifyCurrentFileAsync((i, o) => DetectFieldsService.AddFields(i, o, chosen), $"{chosen.Count} field(s) added"))
            ToastService.Instance.Success($"{chosen.Count} fillable field(s) added.");
    }

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
