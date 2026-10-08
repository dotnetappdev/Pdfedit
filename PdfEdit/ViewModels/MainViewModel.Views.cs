using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>Reading views (night mode, two pages, slide show), extracting images and export to PowerPoint.</summary>
public partial class MainViewModel
{
    private ICommand? _slideShowCommand, _extractImagesCommand, _exportPptCommand, _toggleNightCommand, _toggleTwoPageCommand;

    public bool NightMode
    {
        get => AppSettings.Current.NightMode;
        set { if (AppSettings.Current.NightMode == value) return; AppSettings.Current.NightMode = value; AppSettings.Current.Save(); OnPropertyChanged(); PageChanged?.Invoke(); }
    }

    public bool TwoPageView
    {
        get => AppSettings.Current.TwoPageView;
        set { if (AppSettings.Current.TwoPageView == value) return; AppSettings.Current.TwoPageView = value; AppSettings.Current.Save(); OnPropertyChanged(); PageChanged?.Invoke(); }
    }

    private bool _isAutoScrolling;
    /// <summary>Auto-scroll: the page moves up by itself and turns at the bottom (Esc or a click stops it).</summary>
    public bool IsAutoScrolling
    {
        get => _isAutoScrolling;
        set
        {
            if (_isAutoScrolling == value || (value && !HasDocument)) return;
            _isAutoScrolling = value;
            KeepAwake.Set("autoscroll", value);
            OnPropertyChanged();
            StatusText = value ? $"Auto-scroll on ({AutoScrollLabel}). Faster / Slower to change, Esc or a click to stop." : "Auto-scroll off.";
        }
    }

    /// <summary>Screen pixels a second (10 to 400).</summary>
    public double AutoScrollSpeed
    {
        get => Math.Clamp(AppSettings.Current.AutoScrollSpeed, 10, 400);
        set
        {
            value = Math.Clamp(value, 10, 400);
            if (Math.Abs(AppSettings.Current.AutoScrollSpeed - value) < 0.5) return;
            AppSettings.Current.AutoScrollSpeed = value;
            AppSettings.Current.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoScrollLabel));
            if (_isAutoScrolling) StatusText = $"Auto-scroll speed: {AutoScrollLabel}.";
        }
    }

    public string AutoScrollLabel => $"{AutoScrollSpeed / 40:0.##}×";

    private ICommand? _toggleAutoScrollCommand, _autoScrollFasterCommand, _autoScrollSlowerCommand;
    public ICommand ToggleAutoScrollCommand => _toggleAutoScrollCommand ??= new RelayCommand(() => IsAutoScrolling = !IsAutoScrolling, () => HasDocument);
    public ICommand AutoScrollFasterCommand => _autoScrollFasterCommand ??= new RelayCommand(() => AutoScrollSpeed *= 1.25);
    public ICommand AutoScrollSlowerCommand => _autoScrollSlowerCommand ??= new RelayCommand(() => AutoScrollSpeed /= 1.25);

    public ICommand ToggleNightModeCommand => _toggleNightCommand ??= new RelayCommand(() => NightMode = !NightMode);
    public ICommand ToggleTwoPageViewCommand => _toggleTwoPageCommand ??= new RelayCommand(() => TwoPageView = !TwoPageView);

    public ICommand SlideShowCommand => _slideShowCommand ??= new RelayCommand(() =>
    {
        if (_document == null) return;
        var show = new Dialogs.PresentationWindow(_renderService, _document.PageSizes, _currentPageIndex);
        KeepAwake.Set("slideshow", true);
        try { show.ShowDialog(); }
        finally { KeepAwake.Set("slideshow", false); }
        CurrentPageIndex = show.PageIndex;
    }, () => HasDocument);

    public ICommand ExtractImagesCommand => _extractImagesCommand ??= new AsyncRelayCommand(async () =>
    {
        if (_currentFilePath == null) return;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder for the images",
            InitialDirectory = Path.GetDirectoryName(_currentFilePath),
        };
        if (dlg.ShowDialog() != true) return;
        string src = _currentFilePath, folder = Path.Combine(dlg.FolderName, Path.GetFileNameWithoutExtension(_currentFilePath) + " images");
        try
        {
            StatusText = "Extracting images…";
            var (saved, skipped) = await Task.Run(() => ImageExtractService.ExtractAll(src, folder));
            StatusText = $"{saved} image(s) saved.";
            if (saved == 0)
            {
                Dialogs.AppDialog.ShowInfo(skipped > 0 ? $"{skipped} image(s) use a format PdfEdit can't save." : "This PDF has no images (scanned pages and vector drawings aren't separate pictures).", "Extract images");
                try { if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); } catch { }
                return;
            }
            ToastService.Instance.Success($"Saved {saved} image(s){(skipped > 0 ? $"; {skipped} couldn't be read" : "")}.");
            System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Extracting images failed.", ex); }
    }, () => HasDocument);

    public ICommand ExportPowerPointCommand => _exportPptCommand ??= new AsyncRelayCommand(async () =>
    {
        if (_currentFilePath == null || _document == null) return;
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export to PowerPoint", Filter = "PowerPoint Presentation (*.pptx)|*.pptx",
            FileName = Path.GetFileNameWithoutExtension(_currentFilePath) + ".pptx", AddExtension = true,
        };
        if (save.ShowDialog() != true) return;
        string src = _currentFilePath;
        try
        {
            var slides = new List<PptxSlide>();
            for (int i = 0; i < _document.PageCount; i++)
            {
                StatusText = $"Export to PowerPoint: page {i + 1} of {_document.PageCount}…";
                var bmp = await _renderService.RenderPageAsync(i, 2.0, 1.0);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var ms = new MemoryStream();
                enc.Save(ms);
                string text = "";
                int page = i + 1;
                try { text = await Task.Run(() => string.Join(" ", PageTextLocator.GetChunks(src, page).Select(c => c.Text))); } catch { }
                var (w, h) = _document.PageSizes[i];
                slides.Add(new PptxSlide(ms.ToArray(), w, h, text));
            }
            string dest = save.FileName;
            await Task.Run(() => PptxExportService.Export(dest, slides));
            StatusText = $"Exported {slides.Count} slide(s) to {Path.GetFileName(dest)}.";
            if (Dialogs.AppDialog.ShowConfirm($"Exported {slides.Count} slides. Open the presentation now?", "Export to PowerPoint", "Open", "Close"))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Export failed.";
            Dialogs.AppDialog.ShowError("Export to PowerPoint failed.", ex);
        }
    }, () => HasDocument);
}
