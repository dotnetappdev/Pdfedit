using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

public enum ScanOutput { NewPdf, AfterCurrentPage, AtEnd }

/// <summary>
/// Scan dialog with the standard TWAIN controls: scanner (TWAIN or WIA), paper source (flatbed /
/// feeder / duplex), colour mode, resolution, page size, brightness and contrast, the option to use
/// the scanner's own dialog, a Preview scan, and the scanned pages to rotate, reorder or delete
/// before the PDF is made. OCR makes the result searchable.
/// </summary>
public partial class ScanDialog : Window
{
    private readonly ObservableCollection<ScannedPage> _pages = new();
    private CancellationTokenSource? _cts;
    private bool _busy;

    public IReadOnlyList<ScannedPage> Pages => _pages;
    public bool RecogniseText => OcrBox.IsChecked == true;
    public bool BlackAndWhite => ColorBox.SelectedIndex == 2;
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
        Loaded += (_, _) => LoadDevices();
        Closing += (_, e) =>
        {
            if (_busy) { _cts?.Cancel(); e.Cancel = true; StatusText.Text = "Stopping the scanner…"; }
            else SaveSettings();
        };
    }

    private IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    // ── Devices ──────────────────────────────────────────────────────────────

    private void LoadDevices()
    {
        string? last = AppSettings.Current.LastScanner;
        var devices = ScannerService.ListDevices(Hwnd, out var note);
        DeviceBox.ItemsSource = devices;
        DeviceBox.SelectedItem = devices.FirstOrDefault(d => d.Display == last) ?? devices.FirstOrDefault();
        DeviceNote.Text = devices.Count == 0
            ? "No scanners found. Check the scanner is on and its driver is installed — or use \"Add image file…\"."
              + (note != null ? "\n" + note : "")
            : note ?? $"{devices.Count} scanner(s) found.";
        ScanBtn.IsEnabled = PreviewBtn.IsEnabled = devices.Count > 0;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadDevices();

    // ── Settings ─────────────────────────────────────────────────────────────

    private ScanSettings CurrentSettings(bool preview) => new()
    {
        Dpi = preview ? 75 : int.Parse((string)((ComboBoxItem)DpiBox.SelectedItem).Tag),
        Color = ColorBox.SelectedIndex switch { 1 => ScanColorMode.Gray, 2 => ScanColorMode.BlackWhite, _ => ScanColorMode.Color },
        Source = SrcDuplex.IsChecked == true ? ScanPaperSource.Duplex
               : SrcFeeder.IsChecked == true ? ScanPaperSource.Feeder : ScanPaperSource.Flatbed,
        PaperSize = (string)((ComboBoxItem)SizeBox.SelectedItem).Tag,
        Brightness = (int)BrightnessSlider.Value,
        Contrast = (int)ContrastSlider.Value,
        ShowDriverUi = !preview && DriverUiBox.IsChecked == true,
        SinglePage = preview,
    };

    private void RestoreSettings()
    {
        var s = AppSettings.Current;
        foreach (ComboBoxItem item in DpiBox.Items)
            if ((string)item.Tag == s.ScanDpi.ToString()) DpiBox.SelectedItem = item;
        ColorBox.SelectedIndex = Math.Clamp(s.ScanColorMode, 0, 2);
        SrcFeeder.IsChecked = s.ScanSource == 1;
        SrcDuplex.IsChecked = s.ScanSource == 2;
        SrcFlatbed.IsChecked = s.ScanSource is not (1 or 2);
        OcrBox.IsChecked = s.ScanOcr;
        DriverUiBox.IsChecked = s.ScanShowDriverUi;
    }

    private void SaveSettings()
    {
        var s = AppSettings.Current;
        s.ScanDpi = int.Parse((string)((ComboBoxItem)DpiBox.SelectedItem).Tag);
        s.ScanColorMode = ColorBox.SelectedIndex;
        s.ScanSource = SrcDuplex.IsChecked == true ? 2 : SrcFeeder.IsChecked == true ? 1 : 0;
        s.ScanOcr = OcrBox.IsChecked == true;
        s.ScanShowDriverUi = DriverUiBox.IsChecked == true;
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

        SetBusy(true, preview ? "Previewing…" : "Scanning…");
        _cts = new CancellationTokenSource();
        int before = _pages.Count;
        try
        {
            int n = await ScannerService.ScanAsync(device, CurrentSettings(preview), Hwnd, page =>
            {
                ShowPreview(page.Image);
                if (!preview)
                {
                    _pages.Add(page);
                    PageList.SelectedIndex = _pages.Count - 1;
                    StatusText.Text = $"Scanned page {_pages.Count}…";
                }
            }, _cts.Token);
            StatusText.Text = preview
                ? (n > 0 ? "Preview — adjust the settings, then Scan." : "Nothing was scanned.")
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

    private void ShowPreview(BitmapSource? img)
    {
        PreviewImage.Source = img;
        EmptyHint.Visibility = img == null ? Visibility.Visible : Visibility.Collapsed;
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
                    _pages.Add(new ScannedPage(frame, frame.DpiX > 1 && Math.Abs(frame.DpiX - 96) > 0.5 ? frame.DpiX : 150));
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
        if (PageList.SelectedItem is ScannedPage p) ShowPreview(p.Image);
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
        ShowPreview(rotated);
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
        if (_pages.Count == 0) ShowPreview(null);
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
