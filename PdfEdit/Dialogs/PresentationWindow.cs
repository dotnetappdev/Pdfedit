using PdfEdit.Drawing.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>
/// Slide show (PDFgear's slide mode, the usual full screen): the document full screen, one page
/// at a time on black. → Space PgDn Enter or click: next · ← PgUp Backspace: previous ·
/// Home / End · type a number and Enter to jump · B: black screen · Esc: leave.
/// </summary>
public sealed class PresentationWindow : Window
{
    private readonly IPdfRenderer _renderer;
    private readonly IReadOnlyList<(double Width, double Height)> _sizes;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _counter = new()
    {
        Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24),
    };
    private readonly Border _black = new() { Background = Brushes.Black, Visibility = Visibility.Collapsed };
    private readonly Dictionary<int, BitmapSource> _cache = new();
    private readonly System.Windows.Threading.DispatcherTimer _hideCounter;
    private string _typed = "";
    private int _render;

    /// <summary>The page the show ended on (0-based).</summary>
    public int PageIndex { get; private set; }

    public PresentationWindow(IPdfRenderer renderer, IReadOnlyList<(double Width, double Height)> sizes, int startIndex)
    {
        _renderer = renderer;
        _sizes = sizes;
        PageIndex = Math.Clamp(startIndex, 0, sizes.Count - 1);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.None;
        Title = "Slide show";
        var grid = new Grid();
        grid.Children.Add(_image);
        grid.Children.Add(_counter);
        grid.Children.Add(_black);
        Content = grid;

        _hideCounter = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _hideCounter.Tick += (_, _) => { _hideCounter.Stop(); _counter.Visibility = Visibility.Collapsed; };

        Loaded += (_, _) => _ = ShowPageAsync();
        KeyDown += OnKey;
        MouseLeftButtonUp += (_, _) => Go(PageIndex + 1);
        MouseRightButtonUp += (_, _) => Go(PageIndex - 1);
        MouseWheel += (_, e) => Go(PageIndex + (e.Delta < 0 ? 1 : -1));
        MouseMove += (_, _) => { Cursor = Cursors.Arrow; ShowCounter(); };
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Escape: Close(); return;
            case Key.Right or Key.Down or Key.Space or Key.PageDown or Key.N: Go(PageIndex + 1); return;
            case Key.Left or Key.Up or Key.PageUp or Key.Back or Key.P: Go(PageIndex - 1); return;
            case Key.Home: Go(0); return;
            case Key.End: Go(_sizes.Count - 1); return;
            case Key.B or Key.OemPeriod: _black.Visibility = _black.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; return;
            case Key.Enter:
                if (int.TryParse(_typed, out int n)) Go(n - 1); else Go(PageIndex + 1);
                _typed = "";
                return;
        }
        if (e.Key is >= Key.D0 and <= Key.D9) _typed += (char)('0' + (e.Key - Key.D0));
        else if (e.Key is >= Key.NumPad0 and <= Key.NumPad9) _typed += (char)('0' + (e.Key - Key.NumPad0));
        else e.Handled = false;
    }

    private void Go(int index)
    {
        index = Math.Clamp(index, 0, _sizes.Count - 1);
        _black.Visibility = Visibility.Collapsed;
        if (index == PageIndex && _image.Source != null) return;
        PageIndex = index;
        _ = ShowPageAsync();
    }

    private void ShowCounter()
    {
        _counter.Text = $"{PageIndex + 1} / {_sizes.Count}";
        _counter.Visibility = Visibility.Visible;
        _hideCounter.Stop();
        _hideCounter.Start();
    }

    private async Task ShowPageAsync()
    {
        int index = PageIndex, ticket = ++_render;
        ShowCounter();
        var bmp = await RenderAsync(index);
        if (ticket != _render) return;
        if (bmp != null) _image.Source = bmp;
        // Get the next page ready.
        if (index + 1 < _sizes.Count) _ = RenderAsync(index + 1);
        foreach (var k in _cache.Keys.Where(k => Math.Abs(k - index) > 2).ToList()) _cache.Remove(k);
    }

    private async Task<BitmapSource?> RenderAsync(int index)
    {
        if (_cache.TryGetValue(index, out var hit)) return hit;
        try
        {
            double sw = ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth;
            double sh = ActualHeight > 0 ? ActualHeight : SystemParameters.PrimaryScreenHeight;
            var (w, h) = _sizes[index];
            double zoom = Math.Min(sw / (w * RendererFactory.PointsToDips), sh / (h * RendererFactory.PointsToDips));
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var bmp = await _renderer.RenderPageAsync(index, zoom, dpi);
            _cache[index] = bmp;
            return bmp;
        }
        catch { return null; }
    }
}
