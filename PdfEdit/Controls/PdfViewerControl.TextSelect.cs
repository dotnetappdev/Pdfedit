using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Controls;

/// <summary>
/// Select Text tool (S): drag over text or any area of the page. The text underneath is picked
/// out and a small bar appears beside it, like PDFgear's and the usual: Copy, Highlight, and AI
/// actions (Explain, Summarise, Rewrite, Translate, Ask), plus "Ask about this area", which sends
/// a picture of the area for charts, scans and photos.
/// </summary>
public partial class PdfViewerControl
{
    private bool _isSelectingText;
    private Point _selectStart;
    private Rectangle? _selectBand;
    private readonly List<Rectangle> _selectionMarks = new();
    private Popup? _selectionBar;
    private (string Path, int Page, List<TextChunk> Chunks)? _chunkCache;

    private bool BeginTextSelect(MouseButtonEventArgs e)
    {
        if (_vm?.ActiveTool != ActiveTool.SelectText) return false;
        var pos = e.GetPosition(HlAnnotCanvas);
        if (!IsOnPage(pos)) return false;
        ClearTextSelection();
        _isSelectingText = true;
        _selectStart = pos;
        _selectBand = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(40, 0x3B, 0x82, 0xF6)),
            Stroke = new SolidColorBrush(Color.FromArgb(200, 0x3B, 0x82, 0xF6)),
            StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 },
        };
        Canvas.SetLeft(_selectBand, pos.X);
        Canvas.SetTop(_selectBand, pos.Y);
        HlAnnotCanvas.Children.Add(_selectBand);
        CaptureMouse();
        e.Handled = true;
        return true;
    }

    private bool UpdateTextSelect(MouseEventArgs e)
    {
        if (!_isSelectingText || _selectBand == null || e.LeftButton != MouseButtonState.Pressed) return false;
        var pos = e.GetPosition(HlAnnotCanvas);
        Canvas.SetLeft(_selectBand, Math.Min(pos.X, _selectStart.X));
        Canvas.SetTop(_selectBand, Math.Min(pos.Y, _selectStart.Y));
        _selectBand.Width = Math.Abs(pos.X - _selectStart.X);
        _selectBand.Height = Math.Abs(pos.Y - _selectStart.Y);
        return true;
    }

    private bool EndTextSelect(MouseButtonEventArgs e)
    {
        if (!_isSelectingText) return false;
        _isSelectingText = false;
        ReleaseMouseCapture();
        e.Handled = true;
        if (_selectBand == null || _vm?.Document == null || _vm.CurrentFilePath == null) return true;

        var band = new Rect(Canvas.GetLeft(_selectBand), Canvas.GetTop(_selectBand), _selectBand.Width, _selectBand.Height);
        int page = _vm.CurrentPageIndex + 1;
        double pageH = _vm.Document.PageSizes[page - 1].Height;
        // Area in PDF points (origin bottom-left).
        var area = new Rect(band.X / Scale, pageH - band.Bottom / Scale, band.Width / Scale, band.Height / Scale);
        bool click = band.Width < 4 && band.Height < 4;

        List<TextChunk> chunks;
        try { chunks = ChunksFor(_vm.CurrentFilePath, page); }
        catch { chunks = new(); }

        // A click picks the line under the pointer; a drag picks what the box touches.
        var picked = chunks.Where(c =>
        {
            var r = new Rect(c.X0, c.Bottom, Math.Max(1, c.X1 - c.X0), Math.Max(1, c.Top - c.Bottom));
            if (click) return r.Contains(area.Location);
            var overlap = Rect.Intersect(r, area);
            return !overlap.IsEmpty && overlap.Width * overlap.Height >= 0.3 * r.Width * r.Height;
        }).OrderByDescending(c => Math.Round(c.MidY / 3)).ThenBy(c => c.X0).ToList();

        if (click && picked.Count == 0) { ClearTextSelection(); return true; }
        if (click) HlAnnotCanvas.Children.Remove(_selectBand);

        // Show what was picked.
        foreach (var c in picked)
        {
            var mark = new Rectangle
            {
                Width = (c.X1 - c.X0) * Scale, Height = (c.Top - c.Bottom) * Scale,
                Fill = new SolidColorBrush(Color.FromArgb(70, 0x3B, 0x82, 0xF6)), IsHitTestVisible = false,
            };
            Canvas.SetLeft(mark, c.X0 * Scale);
            Canvas.SetTop(mark, (pageH - c.Top) * Scale);
            HlAnnotCanvas.Children.Add(mark);
            _selectionMarks.Add(mark);
        }

        string text = JoinLines(picked);
        var bounds = click ? picked.Select(c => new Rect(c.X0 * Scale, (pageH - c.Top) * Scale, (c.X1 - c.X0) * Scale, (c.Top - c.Bottom) * Scale))
                                   .Aggregate(Rect.Empty, (a, b) => { a.Union(b); return a; })
                           : band;
        ShowSelectionBar(text, picked, click ? null : area, page, bounds);
        return true;
    }

    private List<TextChunk> ChunksFor(string path, int page)
    {
        if (_chunkCache is { } c && c.Path == path && c.Page == page) return c.Chunks;
        var chunks = PageTextLocator.GetChunks(path, page);
        _chunkCache = (path, page, chunks);
        return chunks;
    }

    private static string JoinLines(List<TextChunk> picked)
    {
        var sb = new System.Text.StringBuilder();
        double? lastY = null;
        foreach (var c in picked)
        {
            if (lastY != null) sb.Append(Math.Abs(lastY.Value - c.MidY) > 3 ? "\n" : " ");
            sb.Append(c.Text);
            lastY = c.MidY;
        }
        return sb.ToString().Trim();
    }

    private void ShowSelectionBar(string text, List<TextChunk> picked, Rect? area, int page, Rect bounds)
    {
        var bar = new WrapPanel { Margin = new Thickness(4), MaxWidth = 460 };
        bool hasText = text.Length > 0;

        void Add(string glyph, string label, string tip, Action click, bool enabled = true)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Margin = new Thickness(0, 1, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = sp, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2), ToolTip = tip, IsEnabled = enabled, Cursor = Cursors.Hand };
            System.Windows.Automation.AutomationProperties.SetName(b, label);
            b.Click += (_, _) => { CloseSelectionBar(); click(); };
            bar.Children.Add(b);
        }

        if (hasText)
        {
            Add("", "Copy", "Copy the text", () => { try { Clipboard.SetText(text); _vm!.StatusText = "Text copied."; } catch { } ClearTextSelection(); });
            Add("", "Highlight", "Highlight this text", () => HighlightPicked(picked, page));
            Add("", "Explain", "Explain this in plain language", () => _ = _vm!.AskAboutSelectionAsync("explain", text, null, page));
            Add("", "Summarise", "Sum this up", () => _ = _vm!.AskAboutSelectionAsync("summarise", text, null, page));
            Add("", "Rewrite", "Rewrite this more clearly", () => _ = _vm!.AskAboutSelectionAsync("rewrite", text, null, page));
            Add("", "Translate", "Translate into your language", () => _ = _vm!.AskAboutSelectionAsync("translate", text, null, page));
            Add("", "Ask…", "Ask your own question about this", () => _vm!.StartQuestionAboutSelection(text, page));
        }
        if (area is { } snap)
        {
            Add("\uE8C8", "Copy image", "Copy this area as a picture (snapshot) to paste into other apps", () => _ = SnapshotAsync(snap, page, save: false));
            Add("\uE74E", "Save image…", "Save this area as a PNG picture", () => _ = SnapshotAsync(snap, page, save: true));
        }
        if (area is { } a)
            Add("", "Ask about this area", "Send a picture of this area to the AI: charts, tables, scans, photos", () => _ = AskAboutAreaAsync(a, page));
        if (bar.Children.Count == 0) { ClearTextSelection(); return; }

        var border = new Border { Child = bar, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(2) };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 };

        _selectionBar = new Popup
        {
            Child = border, PlacementTarget = HlAnnotCanvas, Placement = PlacementMode.Relative,
            HorizontalOffset = bounds.Left, VerticalOffset = bounds.Bottom + 6,
            StaysOpen = true, AllowsTransparency = true, IsOpen = true,
        };
    }

    private void HighlightPicked(List<TextChunk> picked, int page)
    {
        if (_vm?.Document == null) return;
        double pageH = _vm.Document.PageSizes[page - 1].Height;
        foreach (var c in picked)
        {
            var hl = new HighlightAnnotation
            {
                Left = c.X0, Bottom = c.Bottom - 1, Width = c.X1 - c.X0, Height = Math.Max(6, c.Top - c.Bottom + 2),
                Color = _vm.CurrentHighlightColor, Opacity = _vm.CurrentHighlightOpacity, Kind = HighlightKind.Highlight,
            };
            _vm.AddHighlightAnnotation(hl);
            PlaceHighlightAnnotationVisual(hl, pageH);
        }
        ClearTextSelection();
        _vm.StatusText = "Highlighted.";
    }

    private async Task AskAboutAreaAsync(Rect area, int page)
    {
        if (_vm == null) return;
        ClearTextSelection();
        byte[]? png = await _vm.CapturePageAreaAsync(page - 1, area);
        if (png == null) { _vm.StatusText = "Couldn't capture that area."; return; }
        await _vm.AskAboutSelectionAsync("image", "", png, page);
    }

    /// <summary>Snapshot: the area as a sharp picture (3× zoom, about 216 dpi), copied or saved as PNG.</summary>
    private async Task SnapshotAsync(Rect area, int page, bool save)
    {
        if (_vm == null) return;
        ClearTextSelection();
        byte[]? png = await _vm.CapturePageAreaAsync(page - 1, area, zoom: 3.0, maxSide: 6000);
        if (png == null) { _vm.StatusText = "Couldn't capture that area."; return; }
        try
        {
            if (save)
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save snapshot", Filter = "PNG image (*.png)|*.png",
                    FileName = $"{System.IO.Path.GetFileNameWithoutExtension(_vm.CurrentFilePath)} page {page} snapshot.png",
                };
                if (dlg.ShowDialog() != true) return;
                await System.IO.File.WriteAllBytesAsync(dlg.FileName, png);
                _vm.StatusText = $"Capture saved to {System.IO.Path.GetFileName(dlg.FileName)}.";
            }
            else
            {
                using var ms = new System.IO.MemoryStream(png);
                var bmp = System.Windows.Media.Imaging.BitmapFrame.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.None,
                                                                           System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                Clipboard.SetImage(bmp);
                _vm.StatusText = "Capture copied: paste it into another app.";
                ToastService.Instance.Success("Capture copied to the clipboard.");
            }
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("The snapshot couldn't be copied or saved.", ex); }
    }

    private void CloseSelectionBar()
    {
        if (_selectionBar != null) { _selectionBar.IsOpen = false; _selectionBar = null; }
    }

    /// <summary>Removes the selection band, marks and bar (also on page change, scroll and tool change).</summary>
    private void ClearTextSelection()
    {
        CloseSelectionBar();
        if (_selectBand != null) { HlAnnotCanvas.Children.Remove(_selectBand); _selectBand = null; }
        foreach (var m in _selectionMarks) HlAnnotCanvas.Children.Remove(m);
        _selectionMarks.Clear();
    }
}
