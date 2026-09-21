using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    private double Scale => PdfRenderService.PointsToDips * (_vm?.Zoom ?? 1.0);

    // Acrobat-style form-field appearance brushes (frozen for reuse across fields).
    private static readonly Brush FieldFillBrush = Freeze(Color.FromArgb(60, 90, 160, 255));
    private static readonly Brush FieldBorderBrush = Freeze(Color.FromArgb(150, 70, 130, 200));
    private static readonly Brush FieldFocusBrush = Freeze(Color.FromArgb(90, 120, 180, 255));
    private static readonly Brush FieldFocusBorderBrush = Freeze(Color.FromArgb(230, 30, 120, 220));

    private static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // Active annotation TextBox being placed
    private TextBox? _activeAnnotationBox;
    private FreeTextAnnotation? _pendingAnnotation;

    // Currently focused/selected annotation
    private FreeTextAnnotation? _focusedAnnotation;
    private TextBox? _focusedAnnotationTb;

    // Canvas-based annotation toolbar (replaces floating Popup)
    private Border? _annotToolbar;
    private const double ToolbarH = 26;

    // Annotation drag state
    private bool _isDraggingAnnotation;
    private Point _dragStartPos;
    private double _dragStartLeft, _dragStartTop;

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

        _annotToolbar = BuildAnnotationToolbar();
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
        if (_focusedAnnotationTb == null || _focusedAnnotation == null || _vm == null) return;

        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CurrentFontSize):
                _focusedAnnotationTb.FontSize = _vm.CurrentFontSize * Scale / PdfRenderService.PointsToDips;
                _focusedAnnotation.FontSize = _vm.CurrentFontSize;
                break;
            case nameof(MainViewModel.CurrentFontFamily):
                _focusedAnnotationTb.FontFamily = new FontFamily(_vm.CurrentFontFamily);
                break;
            case nameof(MainViewModel.CurrentFontBold):
                _focusedAnnotationTb.FontWeight = _vm.CurrentFontBold ? FontWeights.Bold : FontWeights.Normal;
                break;
            case nameof(MainViewModel.CurrentFontItalic):
                _focusedAnnotationTb.FontStyle = _vm.CurrentFontItalic ? FontStyles.Italic : FontStyles.Normal;
                break;
            case nameof(MainViewModel.CurrentFontUnderline):
                _focusedAnnotationTb.TextDecorations = _vm.CurrentFontUnderline ? TextDecorations.Underline : null;
                break;
            case nameof(MainViewModel.CurrentFontColor):
                _focusedAnnotationTb.Foreground = ParseBrush(_vm.CurrentFontColor);
                break;
            case nameof(MainViewModel.CurrentTextAlignment):
                _focusedAnnotationTb.TextAlignment = _vm.CurrentTextAlignment;
                break;
        }
    }

    private void OnAnnotationFormattingChanged()
    {
        if (_focusedAnnotationTb == null || _focusedAnnotation == null || _vm == null) return;
        ApplyAnnotationFormatting(_focusedAnnotation, _focusedAnnotationTb);
    }

    // ── Annotation Toolbar (canvas-based) ────────────────────────────────────

    private Border BuildAnnotationToolbar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        // Drag grip
        var grip = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(5, 0, 5, 0),
            Cursor = Cursors.SizeAll,
            ToolTip = "Drag to move",
            Child = new TextBlock
            {
                Text = "⠿",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        grip.MouseLeftButtonDown += OnDragGripMouseDown;
        grip.MouseMove += OnDragGripMouseMove;
        grip.MouseLeftButtonUp += OnDragGripMouseUp;
        panel.Children.Add(grip);

        // Font size: small a (decrease 1pt)
        panel.Children.Add(MakeToolbarBtn("a", "Decrease font size by 1pt", () =>
        {
            if (_vm != null) _vm.CurrentFontSize = Math.Max(6, _vm.CurrentFontSize - 1);
        }, fontSize: 10));

        // Font size: big A (increase 1pt)
        panel.Children.Add(MakeToolbarBtn("A", "Increase font size by 1pt", () =>
        {
            if (_vm != null) _vm.CurrentFontSize = Math.Min(144, _vm.CurrentFontSize + 1);
        }, fontSize: 14));

        // Font size: − (decrease 2pt)
        panel.Children.Add(MakeToolbarBtn("−", "Decrease font size by 2pt", () =>
        {
            if (_vm != null) _vm.CurrentFontSize = Math.Max(6, _vm.CurrentFontSize - 2);
        }));

        // Font size: + (increase 2pt)
        panel.Children.Add(MakeToolbarBtn("+", "Increase font size by 2pt", () =>
        {
            if (_vm != null) _vm.CurrentFontSize = Math.Min(144, _vm.CurrentFontSize + 2);
        }));

        // Delete button
        var deleteBtn = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(160, 35, 25)),
            Padding = new Thickness(7, 0, 7, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Delete annotation (Del)",
            Margin = new Thickness(3, 0, 0, 0),
            Child = new TextBlock
            {
                Text = "✕",
                FontSize = 12,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        deleteBtn.MouseLeftButtonDown += (_, e) =>
        {
            DeleteFocusedAnnotation();
            e.Handled = true;
        };
        panel.Children.Add(deleteBtn);

        var toolbar = new Border
        {
            Child = panel,
            Background = new SolidColorBrush(Color.FromRgb(35, 35, 35)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4, 4, 0, 0),
            Height = ToolbarH,
            Visibility = Visibility.Collapsed,
        };
        Panel.SetZIndex(toolbar, 9999);
        return toolbar;
    }

    private static Border MakeToolbarBtn(string label, string tip, Action onClick, double fontSize = 12)
    {
        var border = new Border
        {
            Padding = new Thickness(6, 0, 6, 0),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Background = Brushes.Transparent,
            Child = new TextBlock
            {
                Text = label,
                FontSize = fontSize,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        border.MouseEnter += (_, _) =>
            border.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
        border.MouseLeave += (_, _) =>
            border.Background = Brushes.Transparent;
        border.MouseLeftButtonDown += (_, e) =>
        {
            onClick();
            e.Handled = true;
        };
        return border;
    }

    // Drag grip handlers
    private void OnDragGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_focusedAnnotationTb == null) return;
        _isDraggingAnnotation = true;
        _dragStartPos = e.GetPosition(AnnotationCanvas);
        _dragStartLeft = Canvas.GetLeft(_focusedAnnotationTb);
        _dragStartTop = Canvas.GetTop(_focusedAnnotationTb);
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnDragGripMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingAnnotation || _focusedAnnotationTb == null) return;
        var pos = e.GetPosition(AnnotationCanvas);
        double newLeft = _dragStartLeft + (pos.X - _dragStartPos.X);
        double newTop = _dragStartTop + (pos.Y - _dragStartPos.Y);
        // Clamp to canvas bounds
        newLeft = Math.Clamp(newLeft, 0, Math.Max(0, AnnotationCanvas.Width - _focusedAnnotationTb.Width));
        newTop = Math.Clamp(newTop, 0, Math.Max(0, AnnotationCanvas.Height - _focusedAnnotationTb.Height));
        Canvas.SetLeft(_focusedAnnotationTb, newLeft);
        Canvas.SetTop(_focusedAnnotationTb, newTop);
        PositionAnnotationToolbar(newLeft, newTop, _focusedAnnotationTb.Width);
        e.Handled = true;
    }

    private void OnDragGripMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingAnnotation) return;
        _isDraggingAnnotation = false;
        ((UIElement)sender).ReleaseMouseCapture();

        // Update PDF coordinates
        if (_focusedAnnotation != null && _focusedAnnotationTb != null && _vm?.Document != null)
        {
            int pageNum = _vm.CurrentPageIndex + 1;
            if (pageNum >= 1 && pageNum <= _vm.Document.PageSizes.Count)
            {
                double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
                double newLeft = Canvas.GetLeft(_focusedAnnotationTb);
                double newTop = Canvas.GetTop(_focusedAnnotationTb);
                _focusedAnnotation.Left = newLeft / Scale;
                _focusedAnnotation.Bottom = pageH - (newTop / Scale) - _focusedAnnotation.Height;
            }
        }
        e.Handled = true;
    }

    private void ShowAnnotationToolbar(TextBox tb)
    {
        if (_annotToolbar == null) return;

        // Ensure toolbar is in the annotation canvas and on top
        if (!AnnotationCanvas.Children.Contains(_annotToolbar))
            AnnotationCanvas.Children.Add(_annotToolbar);
        Panel.SetZIndex(_annotToolbar, 9999);

        double left = Canvas.GetLeft(tb);
        double top = Canvas.GetTop(tb);
        PositionAnnotationToolbar(left, top, tb.Width);
        _annotToolbar.Visibility = Visibility.Visible;
    }

    private void HideAnnotationToolbar()
    {
        if (_annotToolbar != null)
            _annotToolbar.Visibility = Visibility.Collapsed;
        _isDraggingAnnotation = false;
    }

    private void PositionAnnotationToolbar(double tbLeft, double tbTop, double tbWidth)
    {
        if (_annotToolbar == null) return;
        Canvas.SetLeft(_annotToolbar, tbLeft);
        Canvas.SetTop(_annotToolbar, Math.Max(0, tbTop - ToolbarH - 1));
    }

    private void DeleteFocusedAnnotation()
    {
        if (_focusedAnnotation == null) return;
        var ann = _focusedAnnotation;
        var tb = _focusedAnnotationTb;
        _focusedAnnotation = null;
        _focusedAnnotationTb = null;
        HideAnnotationToolbar();
        _vm?.RemoveFreeTextAnnotation(ann);
        if (tb != null) AnnotationCanvas.Children.Remove(tb);
        ToastService.Instance.Info("Annotation deleted.");
    }

    // ── Page rendering ───────────────────────────────────────────────────────

    public async void RefreshPage()
    {
        if (_vm?.Document == null) return;

        FinalizeAnnotationBox();
        HideAnnotationToolbar();
        _focusedAnnotation = null;
        _focusedAnnotationTb = null;
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

        // Right-click a field to delete it (like removing a field in Acrobat).
        if (ctrl is FrameworkElement fe)
        {
            var menu = new ContextMenu();
            var del = new MenuItem { Header = $"Delete field \"{field.Name}\"" };
            del.Click += (_, _) => _vm?.DeleteField(field);
            menu.Items.Add(del);
            fe.ContextMenu = menu;
        }

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
            // Acrobat-style fillable field: faint blue fill + subtle border so the
            // user can clearly see where the fields are and that they are editable.
            Background = FieldFillBrush,
            Foreground = Brushes.Black,
            CaretBrush = Brushes.Black,
            BorderBrush = FieldBorderBrush,
            BorderThickness = new Thickness(1),
            FontSize = Math.Max(8, (vertical ? w : h) * 0.6),
            VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn = field.IsMultiline,
            TextWrapping = field.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Padding = new Thickness(2, 0, 2, 0),
            ToolTip = string.IsNullOrEmpty(field.Tooltip) ? field.Name : field.Tooltip
        };
        System.Windows.Automation.AutomationProperties.SetName(tb, $"Form field: {field.Name}");

        if (vertical)
            tb.LayoutTransform = new RotateTransform(-90);

        tb.TextChanged += (_, _) => _vm!.UpdateFieldValue(field.Name, tb.Text);
        tb.GotFocus += (_, _) =>
        {
            _vm!.SelectedField = field;
            tb.Background = FieldFocusBrush;
            tb.BorderBrush = FieldFocusBorderBrush;
            HighlightActiveField(tb);
        };
        tb.LostFocus += (_, _) =>
        {
            tb.Background = FieldFillBrush;
            tb.BorderBrush = FieldBorderBrush;
        };
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
            ToolTip = field.Name
        };
        System.Windows.Automation.AutomationProperties.SetName(cb, $"Checkbox: {field.Name}");
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
            ToolTip = field.Name
        };
        System.Windows.Automation.AutomationProperties.SetName(rb, $"Radio button: {field.Name}");
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
            ToolTip = field.Name
        };
        System.Windows.Automation.AutomationProperties.SetName(cb, $"Dropdown: {field.Name}");
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
            var sig = OpenSignatureDialog();
            if (sig != null)
            {
                int pageNum = _vm.CurrentPageIndex + 1;
                if (pageNum >= 1 && pageNum <= _vm.Document!.PageSizes.Count)
                {
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
                    AnnotationCanvas.Children.Add(BuildSignatureImage(placed, _vm.Document.PageSizes[pageNum - 1].Height));
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

        // Ensure toolbar is always on top in the canvas
        if (_annotToolbar != null)
        {
            _annotToolbar.Visibility = Visibility.Collapsed;
            AnnotationCanvas.Children.Add(_annotToolbar);
        }
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
            Text = ann.ForceUpperCase ? ann.Text.ToUpperInvariant() : ann.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 200)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 70, 130, 180)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            ToolTip = "Annotation — drag the toolbar to move · right-click to delete"
        };
        System.Windows.Automation.AutomationProperties.SetName(tb, "Text annotation");

        ApplyAnnotationFormatting(ann, tb);

        if (ann.IsVertical)
            tb.LayoutTransform = new RotateTransform(-90);

        tb.TextChanged += (_, _) =>
        {
            string newText = ann.ForceUpperCase ? tb.Text.ToUpperInvariant() : tb.Text;
            if (ann.ForceUpperCase && tb.Text != newText)
            {
                int caretPos = tb.CaretIndex;
                tb.Text = newText;
                tb.CaretIndex = Math.Min(caretPos, newText.Length);
            }
            ann.Text = tb.Text;
        };

        tb.GotFocus += (_, _) =>
        {
            _focusedAnnotation = ann;
            _focusedAnnotationTb = tb;
            if (_vm != null) _vm.SelectedAnnotation = ann;
            // Adobe-style black selection outline
            tb.BorderBrush = new SolidColorBrush(Colors.Black);
            tb.BorderThickness = new Thickness(2);
            tb.Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 200));
            ShowAnnotationToolbar(tb);
        };

        tb.LostFocus += (_, _) =>
        {
            // Slight delay so clicking toolbar buttons doesn't lose focus and hide them
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                if (_focusedAnnotationTb == tb && !_isDraggingAnnotation)
                {
                    _focusedAnnotation = null;
                    _focusedAnnotationTb = null;
                    HideAnnotationToolbar();
                }
                tb.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 70, 130, 180));
                tb.BorderThickness = new Thickness(1);
                tb.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 200));
            });
        };

        // Right-click context menu
        var cm = new ContextMenu();
        var deleteItem = new MenuItem { Header = "Delete Annotation" };
        deleteItem.Click += (_, _) =>
        {
            _vm!.RemoveFreeTextAnnotation(ann);
            AnnotationCanvas.Children.Remove(tb);
            HideAnnotationToolbar();
        };
        cm.Items.Add(deleteItem);
        tb.ContextMenu = cm;

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
        tb.TextAlignment = ann.TextAlignment;
    }

    // ── Signature overlay ─────────────────────────────────────────────────────

    private void BuildSignatureOverlay(IEnumerable<PlacedSignature> sigs)
    {
        if (_vm?.Document == null) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

        foreach (var sig in sigs)
            AnnotationCanvas.Children.Add(BuildSignatureImage(sig, pageH));
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
        catch { }

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

    // ── Mouse: annotation placement, pan ─────────────────────────────────────

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
            if (!IsOnPage(posOnPage)) return;

            FinalizeAnnotationBox();

            if (tool == ActiveTool.DateStamp)
            {
                string dateText = DateTime.Now.ToString(AppSettings.Current.DateFormat);
                PlaceNewAnnotationBox(posOnPage, false, dateText);
            }
            else
            {
                PlaceNewAnnotationBox(posOnPage, tool == ActiveTool.VerticalText, null);
            }
            e.Handled = true;
            return;
        }

        if (tool is ActiveTool.Checkmark or ActiveTool.XMark)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;

            FinalizeAnnotationBox();
            PlaceStampAnnotation(posOnPage, tool == ActiveTool.Checkmark ? "✓" : "✕",
                tool == ActiveTool.Checkmark ? "#2E7D32" : "#C62828");
            e.Handled = true;
            return;
        }

        if (tool == ActiveTool.Signature)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;
            PlaceSignatureAtPoint(posOnPage);
            e.Handled = true;
        }
    }

    private bool IsOnPage(Point p)
        => p.X >= 0 && p.Y >= 0 && p.X <= AnnotationCanvas.Width && p.Y <= AnnotationCanvas.Height;

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
        if (e.Key == Key.Escape)
        {
            FinalizeAnnotationBox();
            HideAnnotationToolbar();
        }

        if (e.Key == Key.Delete && _focusedAnnotation != null)
        {
            DeleteFocusedAnnotation();
            e.Handled = true;
        }
    }

    // ── Stamp annotations (Checkmark / XMark) ────────────────────────────────

    private void PlaceStampAnnotation(Point posOnCanvas, string stampText, string colorHex)
    {
        if (_vm?.Document == null) return;

        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageHeightPts = _vm.Document.PageSizes[pageNum - 1].Height;

        double fontSize = _vm.CurrentFontSize * 2;
        double displayFontSize = fontSize * Scale / PdfRenderService.PointsToDips;
        double defaultW = 32 * Scale;
        double defaultH = 32 * Scale;

        double pdfX = posOnCanvas.X / Scale;
        double pdfY = pageHeightPts - (posOnCanvas.Y / Scale) - (defaultH / Scale);
        double pdfW = defaultW / Scale;
        double pdfH = defaultH / Scale;

        var ann = new FreeTextAnnotation
        {
            PageNumber = pageNum,
            Left = pdfX,
            Bottom = pdfY,
            Width = pdfW,
            Height = pdfH,
            Text = stampText,
            FontSize = fontSize,
            FontFamily = _vm.CurrentFontFamily,
            IsBold = true,
            FontColor = colorHex,
            TextAlignment = System.Windows.TextAlignment.Center,
        };
        _vm.FreeTextAnnotations.Add(ann);

        var tb = new TextBox
        {
            Width = defaultW,
            Height = defaultH,
            Text = stampText,
            FontSize = Math.Max(8, displayFontSize),
            FontWeight = FontWeights.Bold,
            Foreground = ParseBrush(colorHex),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsReadOnly = true,
            TextAlignment = System.Windows.TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var cm = new ContextMenu();
        var delItem = new MenuItem { Header = "Delete Stamp" };
        delItem.Click += (_, _) =>
        {
            _vm.RemoveFreeTextAnnotation(ann);
            AnnotationCanvas.Children.Remove(tb);
        };
        cm.Items.Add(delItem);
        tb.ContextMenu = cm;

        Canvas.SetLeft(tb, posOnCanvas.X);
        Canvas.SetTop(tb, posOnCanvas.Y);
        AnnotationCanvas.Children.Add(tb);

        _vm.StatusText = $"Stamp '{stampText}' placed.";
        ToastService.Instance.Success($"Stamp placed on page {pageNum}.");
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
            TextAlignment = _vm.CurrentTextAlignment,
            ToolTip = vertical ? "Vertical text — click away to commit"
                               : "Type here, then click away to commit",
        };

        if (vertical)
            tb.LayoutTransform = new RotateTransform(-90);

        if (_vm.ForceUpperCase)
        {
            tb.TextChanged += (_, _) =>
            {
                string upper = tb.Text.ToUpperInvariant();
                if (tb.Text != upper)
                {
                    int caret = tb.CaretIndex;
                    tb.Text = upper;
                    tb.CaretIndex = Math.Min(caret, upper.Length);
                }
            };
        }

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
            TextAlignment = _vm.CurrentTextAlignment,
            ForceUpperCase = _vm.ForceUpperCase,
        };

        tb.LostFocus += (_, _) => FinalizeAnnotationBox();
        tb.Focus();
        Keyboard.Focus(tb);

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
        _vm?.FreeTextAnnotations.Add(ann);

        if (_vm != null) _vm.StatusText = $"Annotation placed: \"{text}\"";
    }

    // ── Signature placement ───────────────────────────────────────────────────

    private void PlaceSignatureAtPoint(Point posOnCanvas)
    {
        if (_vm?.Document == null) return;

        // Use library signature if one was pre-selected
        byte[]? bytes = _vm.PendingLibrarySignature;
        if (bytes != null)
        {
            _vm.PendingLibrarySignature = null; // consume it
        }
        else
        {
            var sigData = OpenSignatureDialog();
            if (sigData?.ImageBytes == null || sigData.ImageBytes.Length == 0) return;
            bytes = sigData.ImageBytes;
        }

        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

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
            ImageBytes = bytes,
        };

        _vm.AddPlacedSignature(sig);
        AnnotationCanvas.Children.Add(BuildSignatureImage(sig, pageH));
        _vm.StatusText = "Signature placed. Right-click to delete.";
    }

    private SignatureData? OpenSignatureDialog()
    {
        var dlg = new SignatureDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            // Refresh the toolbox signatures in case the user saved to library
            if (Window.GetWindow(this) is Window win)
            {
                var toolbox = FindChild<ToolboxPanel>(win);
                toolbox?.RefreshSignatures();
            }
            return dlg.Result;
        }
        return null;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindChild<T>(child);
            if (result != null) return result;
        }
        return null;
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
