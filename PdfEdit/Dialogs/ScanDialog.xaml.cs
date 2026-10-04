using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

public enum ScanOutput { NewPdf, AfterCurrentPage, AtEnd }

/// <summary>
/// Scan dialog laid out like Windows Scan: the scanner (every scanner Windows knows, plus TWAIN),
/// then the source, file types, colour modes and resolutions <em>that scanner</em> supports, a page
/// size, brightness and contrast. Preview, drag on the preview to scan just part of the glass, crop
/// scanned pages, rotate / reorder / delete them, then save as a PDF (optionally searchable) or as
/// image files.
/// </summary>
public partial class ScanDialog : Window
{
    private static readonly int[] StandardDpis = { 75, 100, 150, 200, 300, 400, 600, 1200 };

    private readonly ObservableCollection<ScannedPage> _pages = new();
    private CancellationTokenSource? _cts;
    private bool _busy;
    private ScanCapabilities _caps = new();
    private int _capsRequest;

    // Preview / selection state. Selection is in image pixels of the page shown.
    private ScannedPage? _shown;
    private bool _shownIsPreview;
    private Rect? _selection;
    private Point? _dragStart;
    private Rect? _scanRegion;   // inches from the glass's top-left corner

    public IReadOnlyList<ScannedPage> Pages => _pages;
    public bool RecogniseText => FileType == ScanFileType.Pdf && OcrBox.IsChecked == true;
    public ScanColorMode ColorMode => Selected(ColorBox, ScanColorMode.Color);
    public bool BlackAndWhite => ColorMode == ScanColorMode.BlackWhite;
    public ScanFileType FileType => Selected(FileTypeBox, ScanFileType.Pdf);
    public ScanOutput Output => OutAfter.IsChecked == true ? ScanOutput.AfterCurrentPage
                              : OutEnd.IsChecked == true ? ScanOutput.AtEnd : ScanOutput.NewPdf;

    public ScanDialog(bool hasDocument)
    {
        InitializeComponent();
        OutAfter.IsEnabled = OutEnd.IsEnabled = hasDocument;
        PageList.ItemsSource = _pages;
        PageList.ItemTemplate = BuildThumbTemplate();
        _pages.CollectionChanged += (_, _) => UpdatePageCount();
        RestoreSettings();
        ApplyCapabilities(_caps);
        Loaded += async (_, _) => await LoadDevicesAsync();
        Closing += (_, e) =>
        {
            if (_busy) { _cts?.Cancel(); e.Cancel = true; StatusText.Text = "Stopping the scanner…"; }
            else SaveSettings();
        };
    }

    private IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    // ── Devices ──────────────────────────────────────────────────────────────

    private async Task LoadDevicesAsync()
    {
        DeviceNote.Text = "Looking for scanners…";
        ScanBtn.IsEnabled = PreviewBtn.IsEnabled = false;
        string? last = AppSettings.Current.LastScanner;
        var (devices, note) = await ScannerService.ListDevicesAsync(Hwnd);
        DeviceBox.ItemsSource = devices;
        DeviceBox.SelectedItem = devices.FirstOrDefault(d => d.Display == last) ?? devices.FirstOrDefault();
        DeviceNote.Text = devices.Count == 0
            ? "No scanners found. Check the scanner is on and connected (or on the same network), and that it's listed in Windows Settings → Bluetooth & devices → Printers & scanners — or use \"Add image file…\"."
              + (note != null ? "\n" + note : "")
            : note ?? "";
        ScanBtn.IsEnabled = PreviewBtn.IsEnabled = devices.Count > 0;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadDevicesAsync();

    private async void DeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not ScanDevice device) return;
        int request = ++_capsRequest;
        var caps = await ScannerService.GetCapabilitiesAsync(device);
        if (request == _capsRequest) ApplyCapabilities(caps);
    }

    /// <summary>Offers only the sources, file types, colour modes and resolutions the scanner supports.</summary>
    private void ApplyCapabilities(ScanCapabilities caps)
    {
        _caps = caps;
        var s = AppSettings.Current;

        var sources = new List<(string, ScanPaperSource)>();
        if (caps.Flatbed) sources.Add(("Flatbed", ScanPaperSource.Flatbed));
        if (caps.Feeder) sources.Add(("Feeder", ScanPaperSource.Feeder));
        if (caps.Duplex) sources.Add(("Feeder — both sides", ScanPaperSource.Duplex));
        if (sources.Count == 0) sources.Add(("Auto", ScanPaperSource.Flatbed));
        Fill(SourceBox, sources, (ScanPaperSource)Math.Clamp(s.ScanSource, 0, 2));

        Fill(FileTypeBox, caps.FileTypes.Select(t => (FileTypeName(t), t)), (ScanFileType)Math.Clamp(s.ScanFileType, 0, 4));

        Fill(ColorBox, caps.ColorModes.Select(c => (c switch
        {
            ScanColorMode.Gray => "Greyscale",
            ScanColorMode.BlackWhite => "Black and white",
            _ => "Colour",
        }, c)), (ScanColorMode)Math.Clamp(s.ScanColorMode, 0, 2));

        var dpis = StandardDpis.Where(d => d >= caps.MinDpi && d <= caps.MaxDpi).ToList();
        if (dpis.Count == 0) dpis.Add(caps.MinDpi);
        Fill(DpiBox, dpis.Select(d => (d switch
        {
            75 => "75 (draft)",
            200 => "200 (documents)",
            300 => "300 (best for OCR)",
            600 => "600 (photos)",
            _ => d.ToString(),
        }, d)), dpis.OrderBy(d => Math.Abs(d - s.ScanDpi)).First());

        AutoCropBox.Visibility = caps.CanAutoCrop ? Visibility.Visible : Visibility.Collapsed;
        UpdateOutputOptions();
    }

    private static string FileTypeName(ScanFileType t) => t switch
    {
        ScanFileType.Png => "PNG",
        ScanFileType.Jpeg => "JPEG",
        ScanFileType.Tiff => "TIFF",
        ScanFileType.Bmp => "Bitmap",
        _ => "PDF",
    };

    private static void Fill<T>(ComboBox box, IEnumerable<(string Text, T Value)> items, T preferred) where T : notnull
    {
        T keep = box.SelectedItem is ComboBoxItem { Tag: T current } ? current : preferred;
        box.Items.Clear();
        foreach (var (text, value) in items) box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        var all = box.Items.Cast<ComboBoxItem>().ToList();
        box.SelectedItem = all.FirstOrDefault(i => Equals(i.Tag, keep))
                        ?? all.FirstOrDefault(i => Equals(i.Tag, preferred))
                        ?? all.FirstOrDefault();
    }

    private static T Selected<T>(ComboBox box, T fallback) =>
        box.SelectedItem is ComboBoxItem { Tag: T v } ? v : fallback;

    private void FileTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateOutputOptions();

    private void UpdateOutputOptions()
    {
        if (PdfOptions == null) return;   // still loading the XAML
        var type = FileType;
        bool pdf = type == ScanFileType.Pdf;
        PdfOptions.Visibility = pdf ? Visibility.Visible : Visibility.Collapsed;
        ImageOptionsNote.Visibility = pdf ? Visibility.Collapsed : Visibility.Visible;
        ImageOptionsNote.Text = type == ScanFileType.Tiff
            ? "Pages are saved to one multi-page TIFF file."
            : $"Each page is saved as its own {FileTypeName(type)} file (name.ext, name (2).ext, …).";
        DoneBtn.Content = pdf ? "Done" : "Save…";
    }

    private void SizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A page size replaces any area dragged on the preview.
        if (AreaText == null) return;
        if (_scanRegion != null) { _scanRegion = null; if (_shownIsPreview) ClearSelection(); }
        UpdateAreaText();
    }

    // ── Settings ─────────────────────────────────────────────────────────────

    private string PaperSize => (string)((ComboBoxItem)SizeBox.SelectedItem).Tag;

    private ScanSettings CurrentSettings(bool preview) => new()
    {
        // A preview covers the whole glass, so an area can be chosen on it.
        Dpi = preview ? Math.Max(75, _caps.MinDpi) : Selected(DpiBox, 200),
        Color = preview && ColorMode == ScanColorMode.BlackWhite ? ScanColorMode.Gray : ColorMode,
        Source = Selected(SourceBox, ScanPaperSource.Flatbed),
        PaperSize = preview ? "Auto" : PaperSize,
        Region = preview ? null : _scanRegion,
        AutoCrop = !preview && _caps.CanAutoCrop && AutoCropBox.IsChecked == true,
        Brightness = (int)BrightnessSlider.Value,
        Contrast = (int)ContrastSlider.Value,
        ShowDriverUi = !preview && DriverUiBox.IsChecked == true,
        SinglePage = preview,
    };

    private void RestoreSettings()
    {
        var s = AppSettings.Current;
        foreach (ComboBoxItem item in SizeBox.Items)
            if ((string)item.Tag == s.ScanPaperSize) SizeBox.SelectedItem = item;
        OcrBox.IsChecked = s.ScanOcr;
        DriverUiBox.IsChecked = s.ScanShowDriverUi;
        AutoCropBox.IsChecked = s.ScanAutoCrop;
    }

    private void SaveSettings()
    {
        var s = AppSettings.Current;
        s.ScanDpi = Selected(DpiBox, s.ScanDpi);
        s.ScanColorMode = (int)ColorMode;
        s.ScanSource = (int)Selected(SourceBox, ScanPaperSource.Flatbed);
        s.ScanFileType = (int)FileType;
        s.ScanPaperSize = PaperSize;
        s.ScanOcr = OcrBox.IsChecked == true;
        s.ScanShowDriverUi = DriverUiBox.IsChecked == true;
        if (_caps.CanAutoCrop) s.ScanAutoCrop = AutoCropBox.IsChecked == true;
        if (DeviceBox.SelectedItem is ScanDevice d) s.LastScanner = d.Display;
        s.Save();
    }

    // ── Scanning ─────────────────────────────────────────────────────────────

    private async void Preview_Click(object sender, RoutedEventArgs e) => await RunScanAsync(preview: true);
    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunScanAsync(preview: false);

    private async Task RunScanAsync(bool preview)
    {
        if (_busy) { _cts?.Cancel(); return; }
        if (DeviceBox.SelectedItem is not ScanDevice device) return;

        var settings = CurrentSettings(preview);
        // If the driver ignores the scan area it sends the whole glass — crop to the area here.
        Rect? fit = settings.AutoCrop || settings.ShowDriverUi ? null
                  : settings.Region ?? (settings.Source == ScanPaperSource.Flatbed && ScannerService.PaperInches(settings.PaperSize) is { } p
                        ? new Rect(0, 0, p.Width, p.Height) : null);

        SetBusy(true, preview ? "Previewing…" : "Scanning…");
        _cts = new CancellationTokenSource();
        int before = _pages.Count;
        try
        {
            int n = await ScannerService.ScanAsync(device, settings, Hwnd, page =>
            {
                if (preview) { ShowPage(page, isPreview: true); return; }
                if (fit is { } r) page = ScannerService.FitToRegion(page, r);
                _pages.Add(page);
                PageList.SelectedIndex = _pages.Count - 1;
                StatusText.Text = $"Scanned page {_pages.Count}…";
            }, _cts.Token, status => StatusText.Text = status);
            StatusText.Text = preview
                ? (n > 0 ? "Preview — drag to choose the area, then Scan." : "Nothing was scanned.")
                : n > 0 ? $"Added {_pages.Count - before} page(s)." : "Nothing was scanned.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Scan cancelled."; }
        catch (Exception ex)
        {
            StatusText.Text = "Scan failed.";
            AppDialog.ShowError($"Scanning with \"{device.Name}\" failed.", ex);
        }
        finally { SetBusy(false, null); }
    }

    private void SetBusy(bool busy, string? status)
    {
        _busy = busy;
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ScanBtn.Content = busy ? "Stop" : "Scan";
        PreviewBtn.IsEnabled = !busy;
        DoneBtn.IsEnabled = !busy && _pages.Count > 0;
        DeviceBox.IsEnabled = !busy;
        if (status != null) StatusText.Text = status;
    }

    // ── Preview, scan area and crop ──────────────────────────────────────────

    private void ShowPage(ScannedPage? page, bool isPreview)
    {
        _shown = page;
        _shownIsPreview = isPreview && page != null;
        PreviewImage.Source = page?.Image;
        EmptyHint.Visibility = page == null ? Visibility.Visible : Visibility.Collapsed;
        PreviewSurface.Width = page?.Image.PixelWidth ?? 1;
        PreviewSurface.Height = page?.Image.PixelHeight ?? 1;
        SelectionRect.StrokeThickness = Math.Max(1, (page?.Image.PixelWidth ?? 300) / 300.0);
        SelectionRect.StrokeDashArray = new DoubleCollection { 4, 2 };

        // On a new preview, show the area already chosen.
        _selection = null;
        if (_shownIsPreview && _scanRegion is { } r && page!.Dpi > 1)
            _selection = new Rect(r.X * page.Dpi, r.Y * page.Dpi, r.Width * page.Dpi, r.Height * page.Dpi);
        DrawSelection();
    }

    private void Selection_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_shown == null || _busy) return;
        _dragStart = e.GetPosition(SelectionCanvas);
        SelectionCanvas.CaptureMouse();
        _selection = new Rect(_dragStart.Value, _dragStart.Value);
        DrawSelection();
    }

    private void Selection_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || _shown == null) return;
        var pt = e.GetPosition(SelectionCanvas);
        pt.X = Math.Clamp(pt.X, 0, _shown.Image.PixelWidth);
        pt.Y = Math.Clamp(pt.Y, 0, _shown.Image.PixelHeight);
        _selection = new Rect(start, pt);
        DrawSelection();
    }

    private void Selection_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart == null) return;
        _dragStart = null;
        SelectionCanvas.ReleaseMouseCapture();
        if (_selection is { } sel && (sel.Width < 8 || sel.Height < 8)) _selection = null;   // a click clears

        if (_shownIsPreview && _shown != null)
        {
            double dpi = _shown.Dpi > 1 ? _shown.Dpi : 75;
            _scanRegion = _selection is { } r ? new Rect(r.X / dpi, r.Y / dpi, r.Width / dpi, r.Height / dpi) : null;
            if (_scanRegion != null && PaperSize != "Auto")
            {
                SizeBox.SelectionChanged -= SizeBox_SelectionChanged;
                SizeBox.SelectedIndex = 0;   // the dragged area replaces the page size
                SizeBox.SelectionChanged += SizeBox_SelectionChanged;
            }
        }
        DrawSelection();
    }

    private void DrawSelection()
    {
        if (_selection is { } r)
        {
            Canvas.SetLeft(SelectionRect, r.X);
            Canvas.SetTop(SelectionRect, r.Y);
            SelectionRect.Width = r.Width;
            SelectionRect.Height = r.Height;
            SelectionRect.Visibility = Visibility.Visible;
        }
        else SelectionRect.Visibility = Visibility.Collapsed;

        CropBtn.IsEnabled = _selection != null && !_shownIsPreview && PageList.SelectedIndex >= 0;
        ClearAreaBtn.IsEnabled = _selection != null || _scanRegion != null;
        UpdateAreaText();
    }

    private void UpdateAreaText()
    {
        if (_scanRegion is { } r)
            AreaText.Text = $"Scan area: {r.Width:0.0} × {r.Height:0.0} in ({r.Width * 25.4:0} × {r.Height * 25.4:0} mm) from the preview.";
        else if (_selection != null && !_shownIsPreview)
            AreaText.Text = "Crop page to keep just the selected part.";
        else if (PaperSize != "Auto")
            AreaText.Text = $"Scan area: {((ComboBoxItem)SizeBox.SelectedItem).Content}.";
        else
            AreaText.Text = "Scan area: whole page. Preview, then drag on the preview to scan just part of the glass — or drag on a scanned page to crop it.";
    }

    private void ClearSelection()
    {
        _selection = null;
        DrawSelection();
    }

    private void ClearArea_Click(object sender, RoutedEventArgs e)
    {
        _scanRegion = null;
        ClearSelection();
    }

    private void Crop_Click(object sender, RoutedEventArgs e)
    {
        int i = PageList.SelectedIndex;
        if (i < 0 || _selection is not { } r) return;
        var page = _pages[i];
        var px = new Int32Rect((int)r.X, (int)r.Y,
            (int)Math.Min(r.Width, page.Image.PixelWidth - (int)r.X), (int)Math.Min(r.Height, page.Image.PixelHeight - (int)r.Y));
        if (px.Width < 1 || px.Height < 1) return;
        _pages[i] = ScannerService.Crop(page, px);
        PageList.SelectedIndex = i;
        ShowPage(_pages[i], isPreview: false);
        StatusText.Text = $"Page {i + 1} cropped.";
    }

    private void AddImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add images as pages",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.gif|All files|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var file in dlg.FileNames)
        {
            try
            {
                var decoder = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                foreach (var frame in decoder.Frames)   // multi-page TIFFs give several pages
                {
                    frame.Freeze();
                    _pages.Add(new ScannedPage(frame, ScannerService.ImageDpi(frame, 150)));
                }
            }
            catch (Exception ex) { AppDialog.ShowError($"Could not read {System.IO.Path.GetFileName(file)}.", ex); }
        }
        if (_pages.Count > 0) PageList.SelectedIndex = _pages.Count - 1;
    }

    // ── Pages ────────────────────────────────────────────────────────────────

    private void UpdatePageCount()
    {
        PagesHead.Text = $"PAGES ({_pages.Count})";
        DoneBtn.IsEnabled = !_busy && _pages.Count > 0;
    }

    private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageList.SelectedItem is ScannedPage p) ShowPage(p, isPreview: false);
    }

    private void Rotate(double angle)
    {
        int i = PageList.SelectedIndex;
        if (i < 0) return;
        var p = _pages[i];
        var rotated = new TransformedBitmap(p.Image, new RotateTransform(angle));
        rotated.Freeze();
        _pages[i] = p with { Image = rotated };
        PageList.SelectedIndex = i;
        ShowPage(_pages[i], isPreview: false);
    }

    private void RotateLeft_Click(object sender, RoutedEventArgs e) => Rotate(-90);
    private void RotateRight_Click(object sender, RoutedEventArgs e) => Rotate(90);

    private void Move(int delta)
    {
        int i = PageList.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= _pages.Count) return;
        _pages.Move(i, j);
        PageList.SelectedIndex = j;
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        int i = PageList.SelectedIndex;
        if (i < 0) return;
        _pages.RemoveAt(i);
        if (_pages.Count == 0) ShowPage(null, isPreview: false);
        else PageList.SelectedIndex = Math.Min(i, _pages.Count - 1);
    }

    private static DataTemplate BuildThumbTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 4, 2, 4));
        var img = new FrameworkElementFactory(typeof(Image));
        img.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("Image"));
        img.SetValue(FrameworkElement.HeightProperty, 120.0);
        img.SetValue(Image.StretchProperty, Stretch.Uniform);
        panel.AppendChild(img);
        var dpi = new FrameworkElementFactory(typeof(TextBlock));
        dpi.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Dpi") { StringFormat = "{0:0} dpi" });
        dpi.SetValue(TextBlock.FontSizeProperty, 10.0);
        dpi.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        panel.AppendChild(dpi);
        return new DataTemplate { VisualTree = panel };
    }

    // ── Done / Cancel ────────────────────────────────────────────────────────

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        if (_pages.Count == 0) return;
        SaveSettings();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { _cts?.Cancel(); return; }
        DialogResult = false;
    }
}
