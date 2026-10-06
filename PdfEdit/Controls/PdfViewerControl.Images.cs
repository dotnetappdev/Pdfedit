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
/// Edit Images tool (PDFgear / Acrobat "Edit PDF" for pictures): every picture on the page gets a
/// dashed outline. Click one to select it, drag it to move, drag a corner to resize (Shift: free
/// proportions), and use the bar to Replace, Save or Delete it. Del deletes, Esc deselects.
/// Each change is written into the PDF and can be undone with Ctrl+Z.
/// </summary>
public partial class PdfViewerControl
{
    private static readonly Brush ImageOutline = Freeze(Color.FromRgb(0x0A, 0x84, 0xFF));
    private static readonly Brush ImageHoverFill = Freeze(Color.FromArgb(0x22, 0x0A, 0x84, 0xFF));

    private PageImageInfo? _selectedImage;
    private Border? _imageFrame;
    private Popup? _imageBar;
    private bool _imageDragging;
    private string _imageDragMode = "";        // "move" or a corner: "nw", "ne", "sw", "se"
    private Point _imageDragStart;
    private Rect _imageStartRect, _imageCurrentRect;

    private bool ImageEditActive => _vm?.ActiveTool == ActiveTool.EditImages && _vm.Document != null;

    private void BuildImageEditOverlay(double width, double height)
    {
        CloseImageBar();
        ImageEditCanvas.Children.Clear();
        _imageFrame = null;
        if (!ImageEditActive || double.IsNaN(width) || width <= 0)
        {
            ImageEditCanvas.Visibility = Visibility.Collapsed;
            _selectedImage = null;
            return;
        }
        ImageEditCanvas.Visibility = Visibility.Visible;
        ImageEditCanvas.Width = width;
        ImageEditCanvas.Height = height;
        ImageEditCanvas.MouseLeftButtonDown -= ImageCanvas_EmptyClick;
        ImageEditCanvas.MouseLeftButtonDown += ImageCanvas_EmptyClick;

        var images = _vm!.GetImagesForCurrentPage();
        if (images.Count == 0)
            _vm.StatusText = "Edit Images: there are no pictures on this page that can be edited.";
        foreach (var img in images)
        {
            var r = ImageRectOnScreen(img);
            var outline = new Rectangle
            {
                Width = Math.Max(6, r.Width), Height = Math.Max(6, r.Height),
                Stroke = ImageOutline, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = Brushes.Transparent, Cursor = Cursors.Hand,
                ToolTip = $"Picture {img.PixelWidth} × {img.PixelHeight} px — click to select, drag to move",
            };
            System.Windows.Automation.AutomationProperties.SetName(outline, $"Picture {img.Index + 1}, {img.PixelWidth} by {img.PixelHeight} pixels");
            Canvas.SetLeft(outline, r.Left);
            Canvas.SetTop(outline, r.Top);
            var captured = img;
            outline.MouseEnter += (_, _) => outline.Fill = ImageHoverFill;
            outline.MouseLeave += (_, _) => outline.Fill = Brushes.Transparent;
            outline.MouseLeftButtonDown += (_, e) =>
            {
                SelectImage(captured);
                StartImageDrag("move", e);
            };
            outline.MouseRightButtonUp += (_, e) => { SelectImage(captured); e.Handled = true; };
            ImageEditCanvas.Children.Add(outline);
        }

        // Keep the selection across a reload (after a move, the same picture is still there).
        if (_selectedImage != null && images.FirstOrDefault(i => i.Index == _selectedImage.Index) is { } again) SelectImage(again);
        else _selectedImage = null;
    }

    private Rect ImageRectOnScreen(PageImageInfo img)
    {
        double pageH = _vm!.Document!.PageSizes[_vm.CurrentPageIndex].Height;
        return new Rect(img.Left * Scale, (pageH - img.Bottom - img.Height) * Scale, img.Width * Scale, img.Height * Scale);
    }

    private void ImageCanvas_EmptyClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != ImageEditCanvas) return;
        DeselectImage();
        e.Handled = true;
    }

    private void SelectImage(PageImageInfo img)
    {
        _selectedImage = img;
        if (_imageFrame != null) ImageEditCanvas.Children.Remove(_imageFrame);

        var r = ImageRectOnScreen(img);
        _imageCurrentRect = r;
        var grid = new Grid();
        grid.Children.Add(new Rectangle { Stroke = ImageOutline, StrokeThickness = 2, Fill = Brushes.Transparent, Cursor = Cursors.SizeAll });
        foreach (var (corner, ha, va, cursor) in new[]
                 {
                     ("nw", HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE),
                     ("ne", HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW),
                     ("sw", HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW),
                     ("se", HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE),
                 })
        {
            var handle = new Rectangle
            {
                Width = 10, Height = 10, Fill = Brushes.White, Stroke = ImageOutline, StrokeThickness = 1.5,
                HorizontalAlignment = ha, VerticalAlignment = va, Margin = new Thickness(-5), Cursor = cursor,
                ToolTip = "Drag to resize (hold Shift to change the proportions)",
            };
            string c = corner;
            handle.MouseLeftButtonDown += (_, e) => StartImageDrag(c, e);
            grid.Children.Add(handle);
        }
        grid.MouseLeftButtonDown += (_, e) => { if (!e.Handled) StartImageDrag("move", e); };
        grid.MouseMove += ImageDrag_Move;
        grid.MouseLeftButtonUp += ImageDrag_Up;

        _imageFrame = new Border { Child = grid, Width = r.Width, Height = r.Height };
        Canvas.SetLeft(_imageFrame, r.Left);
        Canvas.SetTop(_imageFrame, r.Top);
        ImageEditCanvas.Children.Add(_imageFrame);
        ShowImageBar(img);
        if (_vm != null)
            _vm.StatusText = $"Picture selected ({img.PixelWidth} × {img.PixelHeight} px). Drag to move, drag a corner to resize, Del to delete.";
        Focus();
    }

    private void DeselectImage()
    {
        _selectedImage = null;
        CloseImageBar();
        if (_imageFrame != null) { ImageEditCanvas.Children.Remove(_imageFrame); _imageFrame = null; }
    }

    private void StartImageDrag(string mode, MouseButtonEventArgs e)
    {
        if (_imageFrame == null) return;
        _imageDragging = true;
        _imageDragMode = mode;
        _imageDragStart = e.GetPosition(ImageEditCanvas);
        _imageStartRect = _imageCurrentRect;
        Mouse.Capture((IInputElement)_imageFrame.Child, CaptureMode.SubTree);
        CloseImageBar();
        e.Handled = true;
    }

    private void ImageDrag_Move(object sender, MouseEventArgs e)
    {
        if (!_imageDragging || _imageFrame == null) return;
        var p = e.GetPosition(ImageEditCanvas);
        double dx = p.X - _imageDragStart.X, dy = p.Y - _imageDragStart.Y;
        var r = _imageStartRect;
        if (_imageDragMode == "move")
            r.Offset(dx, dy);
        else
        {
            double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
            if (_imageDragMode.Contains('w')) left += dx; else right += dx;
            if (_imageDragMode.Contains('n')) top += dy; else bottom += dy;
            double w = Math.Max(8, right - left), h = Math.Max(8, bottom - top);
            if (Keyboard.Modifiers != ModifierKeys.Shift)
            {
                // Keep the picture's proportions: follow whichever side moved more.
                double aspect = _imageStartRect.Width / Math.Max(1, _imageStartRect.Height);
                if (w / aspect > h) h = w / aspect; else w = h * aspect;
            }
            left = _imageDragMode.Contains('w') ? right - w : left;
            top = _imageDragMode.Contains('n') ? bottom - h : top;
            r = new Rect(left, top, w, h);
        }
        _imageCurrentRect = r;
        Canvas.SetLeft(_imageFrame, r.Left);
        Canvas.SetTop(_imageFrame, r.Top);
        _imageFrame.Width = r.Width;
        _imageFrame.Height = r.Height;
    }

    private void ImageDrag_Up(object sender, MouseButtonEventArgs e)
    {
        if (!_imageDragging) return;
        _imageDragging = false;
        Mouse.Capture(null);
        e.Handled = true;
        if (_selectedImage is not { } img || _vm?.Document == null) return;
        var r = _imageCurrentRect;
        if (Math.Abs(r.Left - _imageStartRect.Left) < 1 && Math.Abs(r.Top - _imageStartRect.Top) < 1
            && Math.Abs(r.Width - _imageStartRect.Width) < 1 && Math.Abs(r.Height - _imageStartRect.Height) < 1)
        {
            ShowImageBar(img);   // just a click
            return;
        }
        double pageH = _vm.Document.PageSizes[_vm.CurrentPageIndex].Height;
        double left = r.Left / Scale, width = r.Width / Scale, height = r.Height / Scale;
        double bottom = pageH - r.Bottom / Scale;
        _ = _vm.MoveResizeImageAsync(img, left, bottom, width, height);
    }

    private void ShowImageBar(PageImageInfo img)
    {
        CloseImageBar();
        if (_imageFrame == null) return;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3) };
        void Add(string glyph, string label, string tip, Action click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Margin = new Thickness(0, 1, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = sp, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2), ToolTip = tip, Cursor = Cursors.Hand };
            System.Windows.Automation.AutomationProperties.SetName(b, label);
            b.Click += (_, _) => { CloseImageBar(); click(); };
            bar.Children.Add(b);
        }
        Add("", "Replace…", "Put another picture in its place, fitted to the same box", () => _ = _vm!.ReplaceImageAsync(img));
        Add("", "Save image…", "Save this picture as a file", () => _ = _vm!.SaveImageAsync(img));
        Add("", "Copy", "Copy this picture to the clipboard", () => _ = _vm!.CopyImageAsync(img));
        Add("", "Delete", "Take this picture off the page (Del)", () => _ = _vm!.DeleteImageAsync(img));

        var border = new Border { Child = bar, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(2) };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 };
        _imageBar = new Popup
        {
            Child = border, PlacementTarget = ImageEditCanvas, Placement = PlacementMode.Relative,
            HorizontalOffset = _imageCurrentRect.Left, VerticalOffset = _imageCurrentRect.Bottom + 8,
            StaysOpen = true, AllowsTransparency = true, IsOpen = true,
        };
    }

    private void CloseImageBar()
    {
        if (_imageBar != null) { _imageBar.IsOpen = false; _imageBar = null; }
    }

    /// <summary>Del deletes the selected picture, Esc deselects. Returns true when handled.</summary>
    private bool HandleImageEditKey(KeyEventArgs e)
    {
        if (!ImageEditActive || _selectedImage is not { } img) return false;
        if (e.Key == Key.Delete) { CloseImageBar(); _ = _vm!.DeleteImageAsync(img); return true; }
        if (e.Key == Key.Escape) { DeselectImage(); return true; }
        return false;
    }
}
