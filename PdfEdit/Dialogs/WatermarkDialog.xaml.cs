using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfEdit.Models;

namespace PdfEdit.Dialogs;

/// <summary>
/// Watermark &amp; background (Acrobat's Edit PDF → Watermark): text or image, diagonal or at an
/// angle, centred / top / bottom / tiled, behind the text and fields or on top, on all / the
/// current / a range of pages — with a live preview on the current page.
/// </summary>
public partial class WatermarkDialog : Window
{
    private static WatermarkOptions? _last;   // remembered for the session

    private readonly double _pageW, _pageH;   // page as seen, in points
    private BitmapSource? _image;

    public WatermarkOptions Options { get; private set; } = new();
    public bool RemoveRequested { get; private set; }

    public WatermarkDialog() : this(null, 612, 792, false) { }

    public WatermarkDialog(BitmapSource? page, double pageWidthPt, double pageHeightPt, bool hasExisting)
    {
        InitializeComponent();
        (_pageW, _pageH) = (pageWidthPt, pageHeightPt);
        // The rendered page may be turned (/Rotate): match its proportions.
        if (page != null && (page.Width > page.Height) != (_pageW > _pageH)) (_pageW, _pageH) = (_pageH, _pageW);

        PreviewRoot.Width = 600;
        PreviewRoot.Height = 600 * _pageH / _pageW;
        PreviewCanvas.Width = PreviewRoot.Width;
        PreviewCanvas.Height = PreviewRoot.Height;
        PreviewImage.Source = page;
        RemoveBtn.Visibility = hasExisting ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (hex, name) in new[] { ("#808080", "Grey"), ("#000000", "Black"), ("#C62828", "Red"), ("#1F4FB5", "Blue"),
                                            ("#1B7A2E", "Green"), ("#D35400", "Orange"), ("#6A1B9A", "Purple") })
        {
            var b = new Button
            {
                Width = 18, Height = 18, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)), ToolTip = name,
            };
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.Click += (_, _) => ColorBox.Text = hex;
            Swatches.Children.Add(b);
        }

        if (_last != null) Restore(_last);
        TextCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdatePreview()));
        Loaded += (_, _) => UpdatePreview();
    }

    private void Restore(WatermarkOptions o)
    {
        TextCombo.Text = o.Text;
        foreach (ComboBoxItem it in FontBox.Items) if ((string)it.Tag == o.FontName) FontBox.SelectedItem = it;
        SizeSlider.Value = o.FontSize;
        ColorBox.Text = o.Color.Length == 9 ? "#" + o.Color[3..] : o.Color;
        OpacitySlider.Value = o.Opacity * 100;
        DiagonalBox.IsChecked = o.Diagonal;
        AngleSlider.Value = o.AngleDeg;
        PositionBox.SelectedIndex = (int)o.Position;
        (o.Behind ? LayerBehind : LayerTop).IsChecked = true;
        if (!string.IsNullOrEmpty(o.ImagePath) && System.IO.File.Exists(o.ImagePath))
        {
            LoadImage(o.ImagePath);
            KindImage.IsChecked = true;
            ScaleSlider.Value = o.ImageScale * 100;
        }
    }

    /// <summary>Starts with this text (and colour), e.g. a stamp used as a page background.</summary>
    public void UseText(string text, string? color)
    {
        KindText.IsChecked = true;
        TextCombo.Text = text;
        if (!string.IsNullOrWhiteSpace(color)) ColorBox.Text = color.Length == 9 ? "#" + color[3..] : color;
    }

    private void Changed(object sender, RoutedEventArgs e) => UpdatePreview();

    private void RangeBox_GotFocus(object sender, RoutedEventArgs e) => PagesRange.IsChecked = true;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a watermark image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        LoadImage(dlg.FileName);
        KindImage.IsChecked = true;
        UpdatePreview();
    }

    private void LoadImage(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            _image = bmp;
            ImagePathBox.Text = path;
        }
        catch (Exception ex)
        {
            AppDialog.ShowError("Could not open that image.", ex);
        }
    }

    private bool IsImage => KindImage.IsChecked == true;

    private Color CurrentColor()
    {
        try { return (Color)ColorConverter.ConvertFromString(ColorBox.Text.Trim()); }
        catch { return Colors.Gray; }
    }

    private void UpdatePreview()
    {
        if (!IsLoaded) return;
        TextPanel.Visibility = IsImage ? Visibility.Collapsed : Visibility.Visible;
        ImagePanel.Visibility = IsImage ? Visibility.Visible : Visibility.Collapsed;
        PreviewCanvas.Children.Clear();

        double k = PreviewCanvas.Width / _pageW;                // preview DIPs per point
        double angle = DiagonalBox.IsChecked == true ? Math.Atan2(_pageH, _pageW) * 180 / Math.PI : AngleSlider.Value;
        double opacity = OpacitySlider.Value / 100;
        var pos = (WatermarkPosition)Math.Max(0, PositionBox.SelectedIndex);

        // Item size in points (same rules as WatermarkService).
        double itemW, itemH;
        FrameworkElement Make()
        {
            if (IsImage)
                return new Image { Source = _image, Width = itemW * k, Height = itemH * k, Stretch = Stretch.Fill, Opacity = opacity };
            string font = (string)((ComboBoxItem)FontBox.SelectedItem).Tag;
            return new TextBlock
            {
                Text = TextCombo.Text,
                FontFamily = new FontFamily(font.StartsWith("Times") ? "Times New Roman" : font.StartsWith("Courier") ? "Courier New" : "Arial"),
                FontWeight = font.EndsWith("Bold") ? FontWeights.Bold : FontWeights.Normal,
                FontSize = Math.Max(1, itemH / 0.72 * k),
                Foreground = new SolidColorBrush(CurrentColor()),
                Opacity = opacity,
            };
        }

        if (IsImage)
        {
            if (_image == null) return;
            itemW = ScaleSlider.Value / 100 * _pageW;
            itemH = itemW * _image.PixelHeight / Math.Max(1, _image.PixelWidth);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(TextCombo.Text)) return;
            var probe = new FormattedText(TextCombo.Text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Arial Black"), SizeSlider.Value, Brushes.Black, 1.0);
            itemW = probe.WidthIncludingTrailingWhitespace * 0.92;
            itemH = SizeSlider.Value * 0.72;
            double diag = Math.Sqrt(_pageW * _pageW + _pageH * _pageH);
            if (pos != WatermarkPosition.Tiled && DiagonalBox.IsChecked == true && itemW > diag * 0.9)
            {
                double f = diag * 0.9 / itemW;
                itemW *= f; itemH *= f;
            }
        }

        var centres = new List<(double X, double Y)>();   // points, origin top-left (WPF)
        double margin = Math.Max(18, _pageH * 0.04);
        switch (pos)
        {
            case WatermarkPosition.Top: centres.Add((_pageW / 2, margin + itemH / 2)); break;
            case WatermarkPosition.Bottom: centres.Add((_pageW / 2, _pageH - margin - itemH / 2)); break;
            case WatermarkPosition.Tiled:
            {
                double stepX = itemW + Math.Max(40, itemH * 2), stepY = itemH * 4 + 40;
                int row = 0;
                for (double y = stepY / 2; y < _pageH + stepY; y += stepY, row++)
                    for (double x = row % 2 == 0 ? 0 : stepX / 2; x < _pageW + stepX; x += stepX)
                        centres.Add((x, _pageH - y));
                break;
            }
            default: centres.Add((_pageW / 2, _pageH / 2)); break;
        }

        foreach (var (cx, cy) in centres)
        {
            var el = Make();
            el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = el.DesiredSize;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = new RotateTransform(-angle);
            Canvas.SetLeft(el, cx * k - size.Width / 2);
            Canvas.SetTop(el, cy * k - size.Height / 2);
            PreviewCanvas.Children.Add(el);
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (IsImage && _image == null) { AppDialog.ShowInfo("Choose an image for the watermark first.", "Watermark"); return; }
        if (!IsImage && string.IsNullOrWhiteSpace(TextCombo.Text)) { AppDialog.ShowInfo("Enter the watermark text.", "Watermark"); return; }
        var c = CurrentColor();
        Options = new WatermarkOptions
        {
            Text = TextCombo.Text.Trim(),
            ImagePath = IsImage ? ImagePathBox.Text : null,
            ImageScale = (float)(ScaleSlider.Value / 100),
            FontName = (string)((ComboBoxItem)FontBox.SelectedItem).Tag,
            FontSize = (float)SizeSlider.Value,
            Color = $"#{c.R:X2}{c.G:X2}{c.B:X2}",
            Opacity = (float)(OpacitySlider.Value / 100),
            Diagonal = DiagonalBox.IsChecked == true,
            AngleDeg = (float)AngleSlider.Value,
            Position = (WatermarkPosition)Math.Max(0, PositionBox.SelectedIndex),
            Behind = LayerBehind.IsChecked == true,
            Pages = PagesCurrent.IsChecked == true ? WatermarkPages.Current
                  : PagesRange.IsChecked == true ? WatermarkPages.Range : WatermarkPages.All,
            PageRange = RangeBox.Text,
        };
        _last = Options;
        DialogResult = true;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        RemoveRequested = true;
        DialogResult = true;
    }
}
