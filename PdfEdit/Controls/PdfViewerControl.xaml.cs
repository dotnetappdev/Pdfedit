using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PdfEdit.Dialogs;
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

    // Currently focused/selected annotation (for in-place formatting)
    private FreeTextAnnotation? _focusedAnnotation;
    private TextBox? _focusedAnnotationTb;

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
            _vm.AnnotationFormattingChanged -= OnAnnotationFormattingChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = DataContext as MainViewModel;
        if (_vm != null)
        {
            _vm.PageChanged += RefreshPage;
            _vm.DocumentLoaded += OnDocumentLoaded;
            _vm.AnnotationFormattingChanged += OnAnnotationFormattingChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnDocumentLoaded()
    {
        PlaceholderPanel.Visibility = Visibility.Collapsed;
        PdfScrollViewer.Visibility = Visibility.Visible;
        RefreshPage();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When VM font properties change and we have a focused annotation TextBox, update it live
        if (_focusedAnnotationTb == null || _focusedAnnotation == null) return;

        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CurrentFontSize):
                if (_vm != null)
                    _focusedAnnotationTb.FontSize = _vm.CurrentFontSize * Scale / PdfRenderService.PointsToDips;
                break;
            case nameof(MainViewModel.CurrentFontFamily):
                if (_vm != null)
                    _focusedAnnotationTb.FontFamily = new FontFamily(_vm.CurrentFontFamily);
                break;
            case nameof(MainViewModel.CurrentFontBold):
                if (_vm != null)
                    _focusedAnnotationTb.FontWeight = _vm.CurrentFontBold ? FontWeights.Bold : FontWeights.Normal;
                break;
            case nameof(MainViewModel.CurrentFontItalic):
                if (_vm != null)
                    _focusedAnnotationTb.FontStyle = _vm.CurrentFontItalic ? FontStyles.Italic : FontStyles.Normal;
                break;
            case nameof(MainViewModel.CurrentFontUnderline):
                if (_vm != null)
                    _focusedAnnotationTb.TextDecorations = _vm.CurrentFontUnderline
                        ? TextDecorations.Underline : null;
                break;
            case nameof(MainViewModel.CurrentFontColor):
                if (_vm != null)
                    _focusedAnnotationTb.Foreground = ParseBrush(_vm.CurrentFontColor);
                break;
        }
    }

    private void OnAnnotationFormattingChanged()
    {
        // Annotation model updated — refresh the visual TextBox if focused
        if (_focusedAnnotationTb == null || _focusedAnnotation == null || _vm == null) return;
        ApplyAnnotationFormatting(_focusedAnnotation, _focusedAnnotationTb);
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

            int rotation = _vm.GetPageRotation(_vm.CurrentPageIndex);
            PageBorder.LayoutTransform = rotation == 0
                ? Transform.Identity
                : new RotateTransform(rotation);

            _vm.RefreshCurrentPageFields();
            BuildFieldOverlay(_vm.CurrentPageFields, _vm.HighlightFields);
            BuildAnnotationOverlay(_vm.GetAnnotationsForCurrentPage());
            BuildSignatureOverlay(_vm.GetSignaturesForCurrentPage());
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
            Width = vertical ? h : w,
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
            tb.LayoutTransform = new RotateTransform(-90);

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
            ToolTip = $"Signature field: {field.Name} — click to sign",
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
        border.MouseLeftButtonDown += (_, e) =>
        {
            _vm!.SelectedField = field;
            // Open signature dialog and place on field bounds
            var sig = OpenSignatureDialog();
            if (sig != null)
            {
                int pageNum = _vm.CurrentPageIndex + 1;
                if (pageNum >= 1 && pageNum <= _vm.Document!.PageSizes.Count)
                {
                    double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
                    // Convert back to PDF coords — field position
                    var placed = new PlacedSignature
                    {
                        PageNumber = pageNum,
                        Left = field.Left,
                        Bottom = field.Bottom,
                        Width = field.Width,
                        Height = field.Height,
                        ImageBytes = sig.ImageBytes!,
                    };
                    _vm.AddPlacedSignature(placed);
                    PlaceSignatureVisual(placed, pageH);
                    AnnotationCanvas.Children.Add(BuildSignatureImage(placed, pageH));
                    _vm.StatusText = "Signature placed on field.";
                }
            }
            e.Handled = true;
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

    // ── Free-text annotation overlay ──────────────────────────────────────────

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
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 200)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 70, 130, 180)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            ToolTip = "Annotation — right-click to delete",
        };

        ApplyAnnotationFormatting(ann, tb);

        if (ann.IsVertical)
            tb.LayoutTransform = new RotateTransform(-90);

        tb.TextChanged += (_, _) => ann.Text = tb.Text;

        tb.GotFocus += (_, _) =>
        {
            _focusedAnnotation = ann;
            _focusedAnnotationTb = tb;
            if (_vm != null) _vm.SelectedAnnotation = ann;
            tb.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215));
            tb.BorderThickness = new Thickness(2);
        };
        tb.LostFocus += (_, _) =>
        {
            if (_focusedAnnotationTb == tb)
            {
                _focusedAnnotation = null;
                _focusedAnnotationTb = null;
            }
            tb.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 70, 130, 180));
            tb.BorderThickness = new Thickness(1);
        };

        // Right-click context menu for deletion
        var cm = new ContextMenu();
        var deleteItem = new MenuItem { Header = "Delete Annotation" };
        deleteItem.Click += (_, _) =>
        {
            _vm!.RemoveFreeTextAnnotation(ann);
            AnnotationCanvas.Children.Remove(tb);
        };
        cm.Items.Add(deleteItem);
        tb.ContextMenu = cm;

        // Ctrl+double-click also deletes
        tb.MouseDoubleClick += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
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

    private void ApplyAnnotationFormatting(FreeTextAnnotation ann, TextBox tb)
    {
        double displayFontSize = ann.FontSize * Scale / PdfRenderService.PointsToDips;
        tb.FontSize = Math.Max(8, displayFontSize);
        tb.FontFamily = new FontFamily(ann.FontFamily);
        tb.FontWeight = ann.IsBold ? FontWeights.Bold : FontWeights.Normal;
        tb.FontStyle = ann.IsItalic ? FontStyles.Italic : FontStyles.Normal;
        tb.TextDecorations = ann.IsUnderline ? TextDecorations.Underline : null;
        tb.Foreground = ParseBrush(ann.FontColor);
    }

    // ── Signature overlay ─────────────────────────────────────────────────────

    private void BuildSignatureOverlay(IEnumerable<PlacedSignature> sigs)
    {
        if (_vm?.Document == null) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

        foreach (var sig in sigs)
        {
            var imgCtrl = BuildSignatureImage(sig, pageH);
            AnnotationCanvas.Children.Add(imgCtrl);
        }
    }

    private UIElement BuildSignatureImage(PlacedSignature sig, double pageHeightPts)
    {
        double x = sig.Left * Scale;
        double y = (pageHeightPts - sig.Bottom - sig.Height) * Scale;
        double w = sig.Width * Scale;
        double h = sig.Height * Scale;

        BitmapImage? bmp = null;
        try
        {
            using var ms = new MemoryStream(sig.ImageBytes);
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
        }
        catch { /* ignore */ }

        var img = new Image
        {
            Width = w,
            Height = h,
            Source = bmp,
            Stretch = Stretch.Fill,
            ToolTip = "Signature — right-click to delete",
        };

        var cm = new ContextMenu();
        var delItem = new MenuItem { Header = "Delete Signature" };
        delItem.Click += (_, _) =>
        {
            _vm!.RemovePlacedSignature(sig);
            AnnotationCanvas.Children.Remove(img);
        };
        cm.Items.Add(delItem);
        img.ContextMenu = cm;

        Canvas.SetLeft(img, x);
        Canvas.SetTop(img, y);
        return img;
    }

    private void PlaceSignatureVisual(PlacedSignature sig, double pageHeightPts)
    {
        // Already handled by BuildSignatureImage — used for immediate placement after dialog
    }

    // ── Mouse: annotation placement, date stamp, signature, pan ─────────────

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

        if (tool is ActiveTool.AddText or ActiveTool.VerticalText or ActiveTool.DateStamp)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (posOnPage.X < 0 || posOnPage.Y < 0 ||
                posOnPage.X > AnnotationCanvas.Width ||
                posOnPage.Y > AnnotationCanvas.Height)
                return;

            FinalizeAnnotationBox();

            if (tool == ActiveTool.DateStamp)
            {
                string dateText = DateTime.Now.ToString("MMMM d, yyyy");
                PlaceNewAnnotationBox(posOnPage, false, dateText);
            }
            else
            {
                PlaceNewAnnotationBox(posOnPage, tool == ActiveTool.VerticalText, null);
            }
            e.Handled = true;
            return;
        }

        if (tool == ActiveTool.Signature)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (posOnPage.X < 0 || posOnPage.Y < 0 ||
                posOnPage.X > AnnotationCanvas.Width ||
                posOnPage.Y > AnnotationCanvas.Height)
                return;

            PlaceSignatureAtPoint(posOnPage);
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

        // Delete focused annotation with Delete key
        if (e.Key == Key.Delete && _focusedAnnotation != null)
        {
            var ann = _focusedAnnotation;
            var tb = _focusedAnnotationTb;
            _focusedAnnotation = null;
            _focusedAnnotationTb = null;
            _vm!.RemoveFreeTextAnnotation(ann);
            if (tb != null) AnnotationCanvas.Children.Remove(tb);
            e.Handled = true;
        }
    }

    // ── Free-text TextBox placement ───────────────────────────────────────────

    private void PlaceNewAnnotationBox(Point posOnCanvas, bool vertical, string? prefilledText)
    {
        if (_vm?.Document == null) return;

        double defaultW = vertical ? 24 * Scale : 140 * Scale;
        double defaultH = vertical ? 100 * Scale : 28 * Scale;

        double displayFontSize = _vm.CurrentFontSize * Scale / PdfRenderService.PointsToDips;

        var tb = new TextBox
        {
            Width = defaultW,
            Height = defaultH,
            Text = prefilledText ?? string.Empty,
            FontSize = Math.Max(8, displayFontSize),
            FontFamily = new FontFamily(_vm.CurrentFontFamily),
            FontWeight = _vm.CurrentFontBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = _vm.CurrentFontItalic ? FontStyles.Italic : FontStyles.Normal,
            TextDecorations = _vm.CurrentFontUnderline ? TextDecorations.Underline : null,
            Foreground = ParseBrush(_vm.CurrentFontColor),
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 200)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
            BorderThickness = new Thickness(1),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(2),
            ToolTip = vertical ? "Vertical text — click away to commit"
                               : "Type here, then click away to commit",
        };

        if (vertical)
            tb.LayoutTransform = new RotateTransform(-90);

        Canvas.SetLeft(tb, posOnCanvas.X);
        Canvas.SetTop(tb, posOnCanvas.Y);
        AnnotationCanvas.Children.Add(tb);

        _activeAnnotationBox = tb;

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
            FontSize = _vm.CurrentFontSize,
            FontFamily = _vm.CurrentFontFamily,
            IsBold = _vm.CurrentFontBold,
            IsItalic = _vm.CurrentFontItalic,
            IsUnderline = _vm.CurrentFontUnderline,
            FontColor = _vm.CurrentFontColor,
        };

        tb.LostFocus += (_, _) => FinalizeAnnotationBox();

        tb.Focus();
        Keyboard.Focus(tb);

        // Select all so pre-filled text (like a date) is ready to confirm or edit
        if (!string.IsNullOrEmpty(prefilledText))
            tb.SelectAll();

        if (_vm != null) _vm.StatusText = vertical
            ? "Vertical text: type, then click away to place."
            : prefilledText != null ? $"Date stamp: '{prefilledText}' — click away to place."
            : "Add text: type, then click away to place.";
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

        // Register in VM; the annotation model carries all font props set at placement time
        _vm?.FreeTextAnnotations.Add(ann);

        if (_vm != null) _vm.StatusText = $"Annotation placed: \"{text}\"";
    }

    // ── Signature placement ───────────────────────────────────────────────────

    private void PlaceSignatureAtPoint(Point posOnCanvas)
    {
        if (_vm?.Document == null) return;

        var sigData = OpenSignatureDialog();
        if (sigData?.ImageBytes == null || sigData.ImageBytes.Length == 0) return;

        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

        // Default signature size: 200×60 display pixels → convert to PDF pts
        double dispW = 200 * Scale;
        double dispH = 60 * Scale;

        double pdfX = posOnCanvas.X / Scale;
        double pdfY = pageH - (posOnCanvas.Y / Scale) - (dispH / Scale);
        double pdfW = dispW / Scale;
        double pdfH = dispH / Scale;

        var sig = new PlacedSignature
        {
            PageNumber = pageNum,
            Left = pdfX,
            Bottom = pdfY,
            Width = pdfW,
            Height = pdfH,
            ImageBytes = sigData.ImageBytes,
        };

        _vm.AddPlacedSignature(sig);
        var imgCtrl = BuildSignatureImage(sig, pageH);
        AnnotationCanvas.Children.Add(imgCtrl);

        _vm.StatusText = "Signature placed. Right-click to delete.";
    }

    private SignatureData? OpenSignatureDialog()
    {
        var dlg = new SignatureDialog
        {
            Owner = Window.GetWindow(this)
        };
        return dlg.ShowDialog() == true ? dlg.Result : null;
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

    private static Brush ParseBrush(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return Brushes.Black;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch { return Brushes.Black; }
    }
}
