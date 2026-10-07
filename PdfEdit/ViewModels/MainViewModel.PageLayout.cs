using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>
/// Page layout tools (PDFgear / PDF readers): resize pages, pages per sheet and booklets for printing,
/// and convert to greyscale.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _resizePagesCommand, _printLayoutCommand, _greyscaleCommand;

    /// <summary>Resize Pages…: new paper size, fit or centre, margin, all / current / a range of pages.</summary>
    public ICommand ResizePagesCommand => _resizePagesCommand ??= new AsyncRelayCommand(ResizePagesAsync, () => HasDocument);
    /// <summary>Pages per Sheet / Booklet…: a new PDF laid out for printing.</summary>
    public ICommand PrintLayoutCommand => _printLayoutCommand ??= new AsyncRelayCommand(PrintLayoutAsync, () => HasDocument);
    /// <summary>Convert to Greyscale: text, drawings and pictures in shades of grey (undoable).</summary>
    public ICommand GreyscaleCommand => _greyscaleCommand ??= new AsyncRelayCommand(GreyscaleAsync, () => HasDocument);

    private ICommand? _snapshotCommand;
    /// <summary>Snapshot: the Select Text tool, ready to drag a box and copy or save it as a picture.</summary>
    public ICommand SnapshotCommand => _snapshotCommand ??= new RelayCommand(() =>
    {
        IsDesignMode = false;
        ActiveTool = Models.ActiveTool.SelectText;
        StatusText = "Capture Area: drag a box round the area, then choose Copy image or Save image.";
    }, () => HasDocument);

    private async Task ResizePagesAsync()
    {
        if (_currentFilePath == null || _document == null) return;
        var dlg = new Dialogs.ResizePagesDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        int count = _document.PageCount, current = _currentPageIndex + 1;
        HashSet<int>? only = dlg.Pages switch
        {
            "all" => null,
            "current" => new HashSet<int> { current },
            var range => WatermarkService.ParseRange(range, count).ToHashSet(),
        };
        if (only is { Count: 0 })
        {
            Dialogs.AppDialog.ShowInfo("None of those pages are in this PDF. Try something like 1-3, 5.", "Resize Pages");
            return;
        }
        int done = 0;
        if (await ModifyCurrentFileAsync((i, o) => done = PageLayoutService.ResizePages(i, o, dlg.PageWidth, dlg.PageHeight, dlg.Fit,
                    dlg.MarginPt, only == null ? null : only.Contains, dlg.KeepOrientation), "Pages resized"))
            ToastService.Instance.Success($"{done} page{(done == 1 ? "" : "s")} resized. Ctrl+Z to undo.");
    }

    private async Task PrintLayoutAsync()
    {
        if (_currentFilePath == null) return;
        var dlg = new Dialogs.PrintLayoutDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        string suffix = dlg.IsBooklet ? "booklet" : $"{dlg.Columns * dlg.Rows}-up";
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = dlg.IsBooklet ? "Save booklet" : "Save pages per sheet",
            Filter = "PDF files (*.pdf)|*.pdf",
            FileName = $"{Path.GetFileNameWithoutExtension(_currentFilePath)} ({suffix}).pdf",
            InitialDirectory = Path.GetDirectoryName(_currentFilePath),
        };
        if (save.ShowDialog() != true) return;
        if (string.Equals(save.FileName, _currentFilePath, StringComparison.OrdinalIgnoreCase))
        {
            Dialogs.AppDialog.ShowInfo("Choose a different name: the open PDF can't be replaced by its own print layout.", "Pages per Sheet");
            return;
        }

        string src = _currentFilePath, dest = save.FileName;
        try
        {
            int sheets = await Task.Run(() => dlg.IsBooklet
                ? PageLayoutService.Booklet(src, dest, dlg.SheetWidth, dlg.SheetHeight)
                : PageLayoutService.NUp(src, dest, new NUpOptions(dlg.Columns, dlg.Rows, dlg.SheetWidth, dlg.SheetHeight,
                                                                   dlg.Borders, ColumnsFirst: dlg.ColumnsFirst)));
            StatusText = $"{Path.GetFileName(dest)} created: {sheets} {(dlg.IsBooklet ? "printed sides" : "sheets")}.";
            ToastService.Instance.Success(dlg.IsBooklet
                ? "Booklet created. Print it double-sided, flipping on the short edge."
                : $"Created {sheets} sheet{(sheets == 1 ? "" : "s")}.");
            if (dlg.OpenResult) await OpenFileAsync(dest);
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("The print layout couldn't be created.", ex);
        }
    }

    private async Task GreyscaleAsync()
    {
        if (_currentFilePath == null) return;
        if (!Dialogs.AppDialog.ShowConfirm(
                "Turn this PDF into shades of grey? Text, drawings and pictures are converted; text stays selectable. " +
                "Gradients and patterns keep their colour.\n\nCtrl+Z undoes it.",
                "Convert to Greyscale", "Convert", "Cancel"))
            return;
        int pages = 0;
        if (await ModifyCurrentFileAsync((i, o) => pages = PageLayoutService.Greyscale(i, o, GreyImage), "Converted to greyscale"))
            ToastService.Instance.Success("Converted to greyscale. Ctrl+Z to undo.");
    }

    /// <summary>Decodes an image (JPEG, PNG, TIFF …), makes it 8-bit grey and encodes it again (JPEG for photos, PNG otherwise).</summary>
    internal static byte[]? GreyImage(byte[] encoded)
    {
        try
        {
            using var input = new MemoryStream(encoded);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var grey = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);
            BitmapEncoder encoder = decoder is JpegBitmapDecoder ? new JpegBitmapEncoder { QualityLevel = 88 } : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(grey));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch { return null; }   // a format Windows can't decode: keep the colour image
    }
}
