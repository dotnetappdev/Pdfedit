using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using PdfEdit.Models;
using PdfEdit.Services;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

public partial class PdfViewerControl : UserControl
{
    private MainViewModel? _vm;

    // Points-to-pixels: pts × Scale = display pixels at current zoom
    private double Scale => PdfRenderService.PointsToDips * (_vm?.Zoom ?? 1.0);

    // Temporary annotation TextBox being placed
    private TextBox? _activeAnnotationBox;
    private FreeTextAnnotation? _pendingAnnotation;

    // Pan state
    private bool _isPanning;
    private Point _panStart;
    private double _scrollHStart, _scrollVStart;

    public PdfViewerControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
        KeyDown += OnKeyDown;
        AllowDrop = true;
        Focusable = true;
    }

    // ── Setup ────────────────────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.PageChanged -= RefreshPage;
            _vm.DocumentLoaded -= OnDocumentLoaded;
        }
        _vm = DataContext as MainViewModel;
        if (_vm != null)
        {
            _vm.PageChanged += RefreshPage;
            _vm.DocumentLoaded += OnDocumentLoaded;
        }
    }

    private void OnDocumentLoaded()
    {
        PlaceholderPanel.Visibility = Visibility.Collapsed;
        PdfScrollViewer.Visibility = Visibility.Visible;
        RefreshPage();
    }

    // ── Page rendering ───────────────────────────────────────────────────────

    public async void RefreshPage()
    {
        if (_vm?.Document == null) return;

        FinalizeAnnotationBox();
        ShowLoading(true);
        try
        {
            var bmp = await _vm.RenderService.RenderPageAsync(_vm.CurrentPageIndex, _vm.Zoom);
            PageImage.Source = bmp;

            double w = bmp.PixelWidth;
            double h = bmp.PixelHeight;
            FieldOverlayCanvas.Width = w;
            FieldOverlayCanvas.Height = h;
            HighlightCanvas.Width = w;
            HighlightCanvas.Height = h;
            AnnotationCanvas.Width = w;
            AnnotationCanvas.Height = h;

            // Apply page rotation as a LayoutTransform on the entire page container
            int rotation = _vm.GetPageRotation(_vm.CurrentPageIndex);
            PageBorder.LayoutTransform = rotation == 0
                ? Transform.Identity
                : new RotateTransform(rotation);

            _vm.RefreshCurrentPageFields();
            BuildFieldOverlay(_vm.CurrentPageFields, _vm.HighlightFields);
            BuildAnnotationOverlay(_vm.GetAnnotationsForCurrentPage());
        }
        catch (Exception ex)
        {
            if (_vm != null) _vm.StatusText = $"Render error: {ex.Message}";
        }
        finally
        {
            ShowLoading(false);
        }
    }

    // ── AcroForm field overlay ───────────────────────────────────────────────

    private void BuildFieldOverlay(IEnumerable<FormFieldInfo> fields, bool highlight)
    {
        FieldOverlayCanvas.Children.Clear();
        HighlightCanvas.Children.Clear();

        if (_vm?.Document == null) return;

        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageHeightPts = _vm.Document.PageSizes[pageNum - 1].Height;

        bool verticalMode = _vm.ActiveTool == ActiveTool.VerticalText;

        foreach (var field in fields)
        {
            double x = field.Left * Scale;
            double y = (pageHeightPts - field.Bottom - field.Height) * Scale;
            double w = field.Width * Scale;
            double h = field.Height * Scale;

            if (highlight)
                AddHighlight(x, y, w, h, field.IsRequired);

            if (!field.IsReadOnly)
                AddFieldControl(field, x, y, w, h, verticalMode);
        }
    }

    private void AddHighlight(double x, double y, double w, double h, bool isRequired)
    {
        var rect = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = isRequired
                ? new SolidColorBrush(Color.FromArgb(40, 255, 80, 80))
                : new SolidColorBrush(Color.FromArgb(40, 30, 130, 255)),
            Stroke = isRequired
                ? new SolidColorBrush(Color.FromArgb(120, 255, 80, 80))
                : new SolidColorBrush(Color.FromArgb(120, 30, 130, 255)),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        HighlightCanvas.Children.Add(rect);
    }

    private void AddFieldControl(FormFieldInfo field, double x, double y, double w, double h, bool verticalText)
    {
        UIElement? ctrl = field.FieldType switch
        {
            FieldType.Text => BuildTextBox(field, w, h, verticalText),
            FieldType.Checkbox => BuildCheckBox(field, w, h),
            FieldType.RadioButton => BuildRadioButton(field, w, h),
            FieldType.ComboBox => BuildComboBox(field, w, h),
            FieldType.ListBox => BuildListBox(field, w, h),
            FieldType.Signature => BuildSignatureBox(field, w, h),
            _ => null
        };

        if (ctrl == null) return;
        Canvas.SetLeft(ctrl, x);
        Canvas.SetTop(ctrl, y);
        FieldOverlayCanvas.Children.Add(ctrl);
    }

    private TextBox BuildTextBox(FormFieldInfo field, double w, double h, bool vertical)
    {
        var tb = new TextBox
        {
            Width = vertical ? h : w,      // swap dims when vertical
            Height = vertical ? w : h,
            Text = _vm!.FieldValues.TryGetValue(field.Name, out var v) ? v : field.Value,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = Math.Max(8, (vertical ? w : h) * 0.6),
            VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn = field.IsMultiline,
            TextWrapping = field.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Padding = new Thickness(2, 0, 2, 0),
            ToolTip = string.IsNullOrEmpty(field.Tooltip) ? field.Name : field.Tooltip,
        };

        if (vertical)
        {
            // Rotate text top-to-bottom; LayoutTransform keeps hit-testing correct
            tb.LayoutTransform = new RotateTransform(-90);
            // After rotation the canvas anchor becomes the new top-left — shift compensates
            Canvas.SetLeft(tb, 0);
            Canvas.SetTop(tb, 0);
        }

        tb.TextChanged += (_, _) => _vm!.UpdateFieldValue(field.Name, tb.Text);
        tb.GotFocus += (_, _) => { _vm!.SelectedField = field; HighlightActiveField(tb); };
        return tb;
    }

    private CheckBox BuildCheckBox(FormFieldInfo field, double w, double h)
    {
        var cb = new CheckBox
        {
            Width = w, Height = h,
            IsChecked = field.Value is "Yes" or "true" or "On" or "1",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = field.Name,
        };
        cb.Checked += (_, _) => _vm!.UpdateFieldValue(field.Name, "Yes");
        cb.Unchecked += (_, _) => _vm!.UpdateFieldValue(field.Name, "Off");
        cb.GotFocus += (_, _) => _vm!.SelectedField = field;
        return cb;
    }

    private RadioButton BuildRadioButton(FormFieldInfo field, double w, double h)
    {
        var rb = new RadioButton
        {
            Width = w, Height = h,
            GroupName = field.RadioGroup ?? field.Name,
            IsChecked = field.Value == "Yes",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = field.Name,
        };
        rb.Checked += (_, _) => _vm!.UpdateFieldValue(field.Name, "Yes");
        rb.Unchecked += (_, _) => _vm!.UpdateFieldValue(field.Name, "Off");
        rb.GotFocus += (_, _) => _vm!.SelectedField = field;
        return rb;
    }

    private ComboBox BuildComboBox(FormFieldInfo field, double w, double h)
    {
        var cb = new ComboBox
        {
            Width = w, Height = h,
            FontSize = Math.Max(8, h * 0.55),
            ToolTip = field.Name,
        };
        foreach (var opt in field.Options) cb.Items.Add(opt);
        var cur = _vm!.FieldValues.TryGetValue(field.Name, out var cv) ? cv : field.Value;
        cb.SelectedItem = cur;
        if (cb.SelectedItem == null && field.Options.Count > 0) cb.SelectedIndex = 0;
        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is string val) _vm.UpdateFieldValue(field.Name, val); };
        cb.GotFocus += (_, _) => _vm.SelectedField = field;
        return cb;
    }

    private ListBox BuildListBox(FormFieldInfo field, double w, double h)
    {
        var lb = new ListBox
        {
            Width = w, Height = h,
            FontSize = Math.Max(8, h * 0.4),
            ToolTip = field.Name,
        };
        foreach (var opt in field.Options) lb.Items.Add(opt);
        var cur = _vm!.FieldValues.TryGetValue(field.Name, out var cv) ? cv : field.Value;
        lb.SelectedItem = cur;
        lb.SelectionChanged += (_, _) => { if (lb.SelectedItem is string val) _vm.UpdateFieldValue(field.Name, val); };
        lb.GotFocus += (_, _) => _vm.SelectedField = field;
        return lb;
    }

    private Border BuildSignatureBox(FormFieldInfo field, double w, double h)
    {
        var border = new Border
        {
            Width = w, Height = h,
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 200)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(20, 100, 100, 200)),
            ToolTip = $"Signature: {field.Name}",
            Cursor = Cursors.Pen,
        };
        border.Child = new TextBlock
        {
            Text = "Click to Sign",
            Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 200)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = Math.Max(8, h * 0.35),
            FontStyle = FontStyles.Italic,
        };
        border.MouseLeftButtonDown += (_, _) =>
        {
            _vm!.SelectedField = field;
            MessageBox.Show("Digital signature support coming soon.\nType your name to indicate signing.",
                "Signature", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        return border;
    }

    private void HighlightActiveField(TextBox tb)
    {
        tb.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 100));
        tb.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        tb.BorderThickness = new Thickness(1);
        tb.LostFocus += (_, _) =>
        {
            tb.Background = Brushes.Transparent;
            tb.BorderBrush = Brushes.Transparent;
            tb.BorderThickness = new Thickness(0);
        };
    }

    // ── Free-text annotation overlay (placed by AddText / VerticalText tool) ─

    private void BuildAnnotationOverlay(IEnumerable<FreeTextAnnotation> annotations)
    {
        AnnotationCanvas.Children.Clear();

        if (_vm?.Document == null) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageHeightPts = _vm.Document.PageSizes[pageNum - 1].Height;

        foreach (var ann in annotations)
            PlaceAnnotationVisual(ann, pageHeightPts);
    }

    private void PlaceAnnotationVisual(FreeTextAnnotation ann, double pageHeightPts)
    {
        double x = ann.Left * Scale;
        double y = (pageHeightPts - ann.Bottom - ann.Height) * Scale;
        double w = ann.Width * Scale;
        double h = ann.Height * Scale;

        var tb = new TextBox
        {
            Width = ann.IsVertical ? h : w,
            Height = ann.IsVertical ? w : h,
            Text = ann.Text,
            FontSize = ann.FontSize * Scale / PdfRenderService.PointsToDips,
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 200)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 70, 130, 180)),
            BorderThickness = new Thickness(1),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Black,
            Padding = new Thickness(2),
            ToolTip = "Free text annotation — double-click to delete",
        };

        if (ann.IsVertical)
            tb.LayoutTransform = new RotateTransform(-90);

        tb.TextChanged += (_, _) => ann.Text = tb.Text;

        // Double-click to remove annotation
        tb.MouseDoubleClick += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Right || Keyboard.Modifiers == ModifierKeys.Control)
            {
                _vm!.RemoveFreeTextAnnotation(ann);
                AnnotationCanvas.Children.Remove(tb);
                e.Handled = true;
            }
        };

        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        AnnotationCanvas.Children.Add(tb);
    }

    // ── Mouse: annotation placement and hand-tool pan ─────────────────────────

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null) return;

        var tool = _vm.ActiveTool;

        if (tool == ActiveTool.Hand)
        {
            _isPanning = true;
            _panStart = e.GetPosition(this);
            _scrollHStart = PdfScrollViewer.HorizontalOffset;
            _scrollVStart = PdfScrollViewer.VerticalOffset;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            return;
        }

        if (tool is ActiveTool.AddText or ActiveTool.VerticalText)
        {
            // Only place annotation if click is on the page canvas area
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (posOnPage.X < 0 || posOnPage.Y < 0 ||
                posOnPage.X > AnnotationCanvas.Width ||
                posOnPage.Y > AnnotationCanvas.Height)
                return;

            FinalizeAnnotationBox();
            PlaceNewAnnotationBox(posOnPage, tool == ActiveTool.VerticalText);
            e.Handled = true;
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(this);
            PdfScrollViewer.ScrollToHorizontalOffset(_scrollHStart + (_panStart.X - pos.X));
            PdfScrollViewer.ScrollToVerticalOffset(_scrollVStart + (_panStart.Y - pos.Y));
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_vm != null) _vm.Zoom += e.Delta > 0 ? 0.1 : -0.1;
            e.Handled = true;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) FinalizeAnnotationBox();
    }

    // ── Free-text TextBox placement ───────────────────────────────────────────

    private void PlaceNewAnnotationBox(Point posOnCanvas, bool vertical)
    {
        if (_vm?.Document == null) return;

        double defaultW = vertical ? 24 * Scale : 120 * Scale;
        double defaultH = vertical ? 80 * Scale : 24 * Scale;

        var tb = new TextBox
        {
            Width = defaultW,
            Height = defaultH,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 200)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
            BorderThickness = new Thickness(1),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Black,
            Padding = new Thickness(2),
            ToolTip = vertical ? "Vertical text — press Escape or click away to commit"
                               : "Text — press Escape or click away to commit",
        };

        if (vertical)
            tb.LayoutTransform = new RotateTransform(-90);

        Canvas.SetLeft(tb, posOnCanvas.X);
        Canvas.SetTop(tb, posOnCanvas.Y);
        AnnotationCanvas.Children.Add(tb);

        _activeAnnotationBox = tb;

        // Build a pending annotation to be registered on commit
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageHeightPts = _vm.Document.PageSizes[pageNum - 1].Height;

        double pdfX = posOnCanvas.X / Scale;
        double pdfY = pageHeightPts - (posOnCanvas.Y / Scale) - (defaultH / Scale);
        double pdfW = defaultW / Scale;
        double pdfH = defaultH / Scale;

        _pendingAnnotation = new FreeTextAnnotation
        {
            PageNumber = pageNum,
            Left = pdfX,
            Bottom = pdfY,
            Width = pdfW,
            Height = pdfH,
            IsVertical = vertical,
            FontSize = 12,
        };

        // Commit when focus leaves
        tb.LostFocus += (_, _) => FinalizeAnnotationBox();

        tb.Focus();
        Keyboard.Focus(tb);
        if (_vm != null) _vm.StatusText = vertical
            ? "Vertical text tool: type your text, then click away to place."
            : "Add text tool: type your text, then click away to place.";
    }

    private void FinalizeAnnotationBox()
    {
        if (_activeAnnotationBox == null || _pendingAnnotation == null) return;

        var tb = _activeAnnotationBox;
        var ann = _pendingAnnotation;
        _activeAnnotationBox = null;
        _pendingAnnotation = null;

        string text = tb.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            AnnotationCanvas.Children.Remove(tb);
            return;
        }

        ann.Text = text;
        _vm?.AddFreeTextAnnotation(ann.Left, ann.Bottom, ann.Width, ann.Height,
                                    text, ann.IsVertical);

        if (_vm != null) _vm.StatusText = $"Text annotation placed: \"{text}\"";
    }

    // ── Drag & Drop ───────────────────────────────────────────────────────────

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var pdf = files.FirstOrDefault(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            if (pdf != null && _vm != null) await _vm.OpenFileAsync(pdf);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ShowLoading(bool show)
        => LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
}
