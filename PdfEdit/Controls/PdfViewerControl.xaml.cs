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
    private static readonly Brush FieldRequiredBorderBrush = Freeze(Color.FromArgb(200, 210, 60, 60));

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

    // SE-corner resize thumb shown when a text annotation is focused
    private System.Windows.Controls.Primitives.Thumb? _resizeThumb;
    private double _resizeStartAnnW, _resizeStartAnnH, _resizeStartAnnBottom;

    // Annotation drag state
    private bool _isDraggingAnnotation;
    private Point _dragStartPos;
    private double _dragStartLeft, _dragStartTop;

    // Pan state
    private bool _isPanning;
    private Point _panStart;
    private double _scrollHStart, _scrollVStart;

    // Highlight drag state
    private bool _isDrawingHighlight;
    private bool _isDraggingHighlight;
    private Point _highlightDragStart;
    private Rectangle? _highlightRubberBand;
    private Rectangle? _highlightPreview;

    // Redaction drag state
    private bool _isDrawingRedact;
    private Point _redactDragStart;
    private Rectangle? _redactRubberBand;

    // Link drag state
    private bool _isDrawingLink;
    private Point _linkDragStart;
    private Rectangle? _linkRubberBand;

    // Freehand draw state
    private bool _isDrawingFreehand;
    private Polyline? _freehandPolyline;
    private List<Point> _freehandPoints = new();

    // Form field placement drag state
    private bool _isDrawingFormField;
    private Point _formFieldDragStart;
    private Rectangle? _formFieldRubberBand;
    private ActiveTool _formFieldTool;

    // Shape drawing drag state (Rectangle / Ellipse / Arrow / Callout)
    private bool _isDrawingShape;
    private Point _shapeDragStart;
    private ActiveTool _shapeTool;
    private System.Windows.Shapes.Shape? _shapeRubberBand;
    private System.Windows.Shapes.Line?  _arrowRubberBand;

    // Common stamp preset texts
    private static readonly string[] StampPresets =
        { "APPROVED", "DRAFT", "CONFIDENTIAL", "RECEIVED", "REVIEWED", "REJECTED", "FOR REVIEW" };

    // Adobe-style field selection chrome
    private Border?    _fieldChromeBorder;
    private TextBlock? _fieldChromeLabel;
    private Button?    _fieldChromeClear;
    private Button?    _fieldChromeToday;
    private TextBox?   _activeTb;
    private FormFieldInfo? _activeFieldInfo;

    private static bool IsDateFieldName(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("date") || lower.Contains("today") || lower.Contains("signed")
            || lower.Contains("dated") || lower.Contains("day") || lower.Contains("datetime");
    }

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
        _resizeThumb = BuildResizeThumb();

        // Report viewport size to VM whenever the scroll viewer is resized
        Loaded += (_, _) =>
        {
            PdfScrollViewer.SizeChanged += (_, _) =>
                _vm?.UpdateViewerSize(PdfScrollViewer.ViewportWidth, PdfScrollViewer.ViewportHeight);
            // Seed initial size immediately
            _vm?.UpdateViewerSize(PdfScrollViewer.ViewportWidth, PdfScrollViewer.ViewportHeight);
        };
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

            // If a document is already loaded when DataContext arrives, show it
            if (_vm.Document != null)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(OnDocumentLoaded));
        }
    }

    private void OnDocumentLoaded()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            PdfScrollViewer.Visibility = Visibility.Visible;
            // Seed viewport size then auto-fit before first render so the user
            // sees the whole page without manually hitting Fit Page.
            _vm?.UpdateViewerSize(PdfScrollViewer.ViewportWidth, PdfScrollViewer.ViewportHeight);
            _vm?.AutoFitOnLoad();
            RefreshPage();
        }));
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

        // Rotate CCW (↺ -90°)
        panel.Children.Add(MakeToolbarBtn("↺", "Rotate 90° counter-clockwise", () =>
        {
            if (_focusedAnnotation == null || _focusedAnnotationTb == null) return;
            _focusedAnnotation.RotationAngle = (_focusedAnnotation.RotationAngle - 90) % 360;
            _focusedAnnotationTb.LayoutTransform = _focusedAnnotation.RotationAngle == 0
                ? Transform.Identity
                : new RotateTransform(_focusedAnnotation.RotationAngle);
        }));

        // Rotate CW (↻ +90°)
        panel.Children.Add(MakeToolbarBtn("↻", "Rotate 90° clockwise", () =>
        {
            if (_focusedAnnotation == null || _focusedAnnotationTb == null) return;
            _focusedAnnotation.RotationAngle = (_focusedAnnotation.RotationAngle + 90) % 360;
            _focusedAnnotationTb.LayoutTransform = _focusedAnnotation.RotationAngle == 0
                ? Transform.Identity
                : new RotateTransform(_focusedAnnotation.RotationAngle);
        }));

        // Quick color swatches (black, dark blue, dark red, dark green, gray)
        var swatchColors = new[]
        {
            ("#000000", "Black"),
            ("#1A237E", "Dark Blue"),
            ("#B71C1C", "Dark Red"),
            ("#1B5E20", "Dark Green"),
            ("#757575", "Gray"),
        };
        var sep1 = new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            Margin = new Thickness(2, 3, 2, 3),
        };
        panel.Children.Add(sep1);
        foreach (var (hex, colorName) in swatchColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Border
            {
                Width = 12, Height = 12,
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                ToolTip = $"Set color: {colorName}",
                VerticalAlignment = VerticalAlignment.Center,
            };
            var capturedHex = hex;
            swatch.MouseLeftButtonDown += (_, e) =>
            {
                if (_vm != null) _vm.CurrentFontColor = capturedHex;
                e.Handled = true;
            };
            panel.Children.Add(swatch);
        }

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
        if (_focusedAnnotationTb == null || _focusedAnnotation?.IsLocked == true) return;
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
        PositionResizeThumb(_focusedAnnotationTb);
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

        ShowResizeThumb(tb);
    }

    private void HideAnnotationToolbar()
    {
        if (_annotToolbar != null)
            _annotToolbar.Visibility = Visibility.Collapsed;
        _isDraggingAnnotation = false;
        HideResizeThumb();
    }

    // ── Resize thumb ─────────────────────────────────────────────────────────

    private const double ThumbSize = 10;

    private System.Windows.Controls.Primitives.Thumb BuildResizeThumb()
    {
        var thumb = new System.Windows.Controls.Primitives.Thumb
        {
            Width = ThumbSize,
            Height = ThumbSize,
            Cursor = Cursors.SizeNWSE,
            Visibility = Visibility.Collapsed,
            ToolTip = "Drag to resize annotation",
            Template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb))
        };

        // Minimal blue square template
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 120, 215)));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        factory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Colors.White));
        factory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        thumb.Template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb))
        {
            VisualTree = factory
        };

        thumb.DragStarted += (_, _) =>
        {
            if (_focusedAnnotation == null) return;
            _resizeStartAnnW = _focusedAnnotation.Width;
            _resizeStartAnnH = _focusedAnnotation.Height;
            _resizeStartAnnBottom = _focusedAnnotation.Bottom;
        };

        thumb.DragDelta += (_, args) =>
        {
            var ann = _focusedAnnotation;
            var tb = _focusedAnnotationTb;
            if (ann == null || tb == null) return;

            double dw = args.HorizontalChange / Scale;
            double dh = args.VerticalChange / Scale;

            double newW = Math.Max(20 / Scale, ann.Width + dw);
            double newH = Math.Max(10 / Scale, ann.Height + dh);

            // Keep the top edge fixed; lower the bottom in PDF-space
            ann.Bottom -= (newH - ann.Height);
            ann.Width = newW;
            ann.Height = newH;

            // Resize the TextBox directly (avoid full RefreshPage during drag)
            if (!ann.IsVertical)
            {
                tb.Width = ann.Width * Scale;
                tb.Height = ann.Height * Scale;
            }
            else
            {
                tb.Width = ann.Height * Scale;
                tb.Height = ann.Width * Scale;
            }

            // Reposition the thumb to the new SE corner
            Canvas.SetLeft(_resizeThumb!, Canvas.GetLeft(tb) + tb.Width - ThumbSize / 2);
            Canvas.SetTop(_resizeThumb!, Canvas.GetTop(tb) + tb.Height - ThumbSize / 2);
        };

        thumb.DragCompleted += (_, _) =>
        {
            if (_focusedAnnotation == null) return;
            // Reposition toolbar (width may have changed)
            if (_focusedAnnotationTb != null)
                PositionAnnotationToolbar(Canvas.GetLeft(_focusedAnnotationTb),
                    Canvas.GetTop(_focusedAnnotationTb), _focusedAnnotationTb.Width);
        };

        return thumb;
    }

    private void ShowResizeThumb(TextBox tb)
    {
        if (_resizeThumb == null) return;
        if (!AnnotationCanvas.Children.Contains(_resizeThumb))
            AnnotationCanvas.Children.Add(_resizeThumb);
        Panel.SetZIndex(_resizeThumb, 10000);
        PositionResizeThumb(tb);
        _resizeThumb.Visibility = Visibility.Visible;
    }

    private void HideResizeThumb()
    {
        if (_resizeThumb != null)
            _resizeThumb.Visibility = Visibility.Collapsed;
    }

    private void PositionResizeThumb(TextBox tb)
    {
        if (_resizeThumb == null) return;
        Canvas.SetLeft(_resizeThumb, Canvas.GetLeft(tb) + tb.Width - ThumbSize / 2);
        Canvas.SetTop(_resizeThumb, Canvas.GetTop(tb) + tb.Height - ThumbSize / 2);
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
            HlAnnotCanvas.Width = w;
            HlAnnotCanvas.Height = h;
            RdAnnotCanvas.Width = w;
            RdAnnotCanvas.Height = h;
            AnnotationCanvas.Width = w;
            AnnotationCanvas.Height = h;

            int rotation = _vm.GetPageRotation(_vm.CurrentPageIndex);
            PageBorder.LayoutTransform = rotation == 0
                ? Transform.Identity
                : new RotateTransform(rotation);

            _vm.RefreshCurrentPageFields();
            BuildFieldOverlay(_vm.CurrentPageFields, _vm.HighlightFields);
            BuildHighlightAnnotationOverlay(_vm.GetHighlightAnnotationsForCurrentPage());
            BuildRedactAnnotationOverlay(_vm.GetRedactRegionsForCurrentPage());
            BuildAnnotationOverlay(_vm.GetAnnotationsForCurrentPage());
            BuildSignatureOverlay(_vm.GetSignaturesForCurrentPage());
            BuildStickyNoteOverlay(_vm.GetStickyNotesForCurrentPage());
            BuildShapeOverlay(_vm.GetShapeAnnotationsForCurrentPage());
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
        _fieldChromeBorder = null;
        _fieldChromeLabel = null;
        _fieldChromeClear = null;
        _activeTb = null;
        _activeFieldInfo = null;

        FieldOverlayCanvas.Children.Clear();
        HighlightCanvas.Children.Clear();

        if (_vm?.Document == null) return;

        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageHeightPts = _vm.Document.PageSizes[pageNum - 1].Height;

        bool verticalMode = _vm.ActiveTool == ActiveTool.VerticalText;

        // Tab through fields in reading order (top-to-bottom, then left-to-right),
        // matching Adobe Acrobat's Tab / Shift+Tab navigation between fields.
        var ordered = fields
            .OrderBy(f => Math.Round((pageHeightPts - f.Bottom - f.Height) / 8))
            .ThenBy(f => f.Left)
            .ToList();

        int tabIndex = 0;
        foreach (var field in ordered)
        {
            double x = field.Left * Scale;
            double y = (pageHeightPts - field.Bottom - field.Height) * Scale;
            double w = field.Width * Scale;
            double h = field.Height * Scale;

            if (highlight)
                AddHighlight(x, y, w, h, field.IsRequired);

            if (!field.IsReadOnly)
                AddFieldControl(field, x, y, w, h, verticalMode, tabIndex++);
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

    private void AddFieldControl(FormFieldInfo field, double x, double y, double w, double h, bool verticalText, int tabIndex = 0)
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

        if (ctrl is Control fieldCtrl)
            fieldCtrl.TabIndex = tabIndex;

        // Right-click context menu — Acrobat-style for text fields, simpler for others.
        if (ctrl is FrameworkElement fe)
        {
            var menu = new ContextMenu();

            if (ctrl is TextBox tb2)
            {
                var cutItem = new MenuItem { Header = "Cut", InputGestureText = "Ctrl+X" };
                cutItem.Click += (_, _) => tb2.Cut();

                var copyItem = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C" };
                copyItem.Click += (_, _) => tb2.Copy();

                var pasteItem = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V" };
                pasteItem.Click += (_, _) => tb2.Paste();

                var selectAllItem = new MenuItem { Header = "Select All", InputGestureText = "Ctrl+A" };
                selectAllItem.Click += (_, _) => tb2.SelectAll();

                var clearItem = new MenuItem { Header = "Clear Field" };
                clearItem.Click += (_, _) =>
                {
                    tb2.Clear();
                    _vm?.UpdateFieldValue(field.Name, string.Empty);
                };

                var applyAllItem = new MenuItem { Header = "Apply value to all pages" };
                applyAllItem.ToolTip = "Copy this field's current value to all fields with the same name on every page";
                applyAllItem.Click += (_, _) =>
                {
                    string val = tb2.Text;
                    _vm?.ApplyValueToAllMatchingFields(field.Name, val);
                };

                menu.Items.Add(cutItem);
                menu.Items.Add(copyItem);
                menu.Items.Add(pasteItem);
                menu.Items.Add(selectAllItem);
                menu.Items.Add(new Separator());
                menu.Items.Add(clearItem);
                menu.Items.Add(applyAllItem);
                menu.Items.Add(new Separator());
            }

            var del = new MenuItem { Header = $"Delete field \"{field.Name}\"" };
            del.Click += (_, _) => _vm?.DeleteField(field);
            menu.Items.Add(del);
            fe.ContextMenu = menu;
        }

        Canvas.SetLeft(ctrl, x);
        Canvas.SetTop(ctrl, y);
        FieldOverlayCanvas.Children.Add(ctrl);

        // Wire up Adobe-style chrome for every focusable field control
        if (ctrl is TextBox tb)
        {
            tb.Tag = field;  // used by Tab navigation
            tb.GotFocus  += (_, _) => ShowFieldChrome(field, x, y, w, h, tb);
            tb.LostFocus += (_, _) => HideFieldChrome(tb);
        }
        else if (ctrl is CheckBox cb)
            cb.GotFocus += (_, _) => { _vm!.SelectedField = field; };
        else if (ctrl is RadioButton rb)
            rb.GotFocus += (_, _) => { _vm!.SelectedField = field; };
        else if (ctrl is ComboBox cbb)
            cbb.GotFocus += (_, _) => { _vm!.SelectedField = field; };
        else if (ctrl is ListBox lb)
            lb.GotFocus += (_, _) => { _vm!.SelectedField = field; };
    }

    private Control BuildTextBox(FormFieldInfo field, double w, double h, bool vertical)
    {
        double dw = vertical ? h : w;
        double dh = vertical ? w : h;

        if (field.IsPassword)
        {
            var pb = new PasswordBox
            {
                Width = dw, Height = dh,
                Background = FieldFillBrush,
                Foreground = Brushes.Black,
                CaretBrush = Brushes.Black,
                BorderBrush = FieldBorderBrush,
                BorderThickness = new Thickness(1),
                FontSize = Math.Max(8, dh * 0.6),
                Padding = new Thickness(2, 0, 2, 0),
                ToolTip = string.IsNullOrEmpty(field.Tooltip) ? field.Name : field.Tooltip,
                Password = _vm!.FieldValues.TryGetValue(field.Name, out var pv) ? pv : field.Value
            };
            System.Windows.Automation.AutomationProperties.SetName(pb, $"Password field: {field.Name}");
            pb.PasswordChanged += (_, _) => _vm!.UpdateFieldValue(field.Name, pb.Password);
            pb.GotFocus  += (_, _) => pb.Background = FieldFocusBrush;
            pb.LostFocus += (_, _) => pb.Background = FieldFillBrush;
            return pb;
        }

        var tb = new TextBox
        {
            Width = dw, Height = dh,
            Text = _vm!.FieldValues.TryGetValue(field.Name, out var v) ? v : field.Value,
            // Acrobat-style fillable field: faint blue fill + subtle border so the
            // user can clearly see where the fields are and that they are editable.
            Background = FieldFillBrush,
            Foreground = Brushes.Black,
            CaretBrush = Brushes.Black,
            // Required fields get a red outline, matching Acrobat's convention.
            BorderBrush = field.IsRequired ? FieldRequiredBorderBrush : FieldBorderBrush,
            BorderThickness = new Thickness(field.IsRequired ? 1.5 : 1),
            Cursor = Cursors.IBeam,
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
            tb.Background = FieldFocusBrush;
        };
        tb.LostFocus += (_, _) =>
        {
            tb.Background = FieldFillBrush;
            tb.BorderBrush = field.IsRequired && string.IsNullOrWhiteSpace(tb.Text)
                ? FieldRequiredBorderBrush
                : FieldBorderBrush;
            tb.BorderThickness = new Thickness(field.IsRequired && string.IsNullOrWhiteSpace(tb.Text) ? 1.5 : 1);
        };
        return tb;
    }

    private CheckBox BuildCheckBox(FormFieldInfo field, double w, double h)
    {
        var currentVal = _vm!.FieldValues.TryGetValue(field.Name, out var cv) ? cv : field.Value;
        bool isChecked = currentVal == field.ExportValue
            || (currentVal is "Yes" or "true" or "On" or "1");

        var cb = new CheckBox
        {
            Width = w, Height = h,
            IsChecked = isChecked,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = string.IsNullOrEmpty(field.Tooltip) ? field.Name : field.Tooltip
        };
        System.Windows.Automation.AutomationProperties.SetName(cb, $"Checkbox: {field.Name}");
        cb.Checked   += (_, _) => _vm!.UpdateFieldValue(field.Name, field.ExportValue);
        cb.Unchecked += (_, _) => _vm!.UpdateFieldValue(field.Name, "Off");
        cb.GotFocus  += (_, _) => _vm!.SelectedField = field;
        return cb;
    }

    private RadioButton BuildRadioButton(FormFieldInfo field, double w, double h)
    {
        var groupVal = _vm!.FieldValues.TryGetValue(field.Name, out var gv) ? gv : field.Value;
        var rb = new RadioButton
        {
            Width = w, Height = h,
            GroupName = field.RadioGroup ?? field.Name,
            IsChecked = groupVal == field.ExportValue,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = string.IsNullOrEmpty(field.Tooltip) ? field.Name : field.Tooltip
        };
        System.Windows.Automation.AutomationProperties.SetName(rb, $"Radio: {field.Name} = {field.ExportValue}");
        // Only handle Checked — GroupName handles mutual exclusion automatically
        rb.Checked  += (_, _) => _vm!.UpdateFieldValue(field.Name, field.ExportValue);
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

    private static readonly SolidColorBrush AdobeBlue = new(Color.FromRgb(0, 120, 215));

    private void ShowFieldChrome(FormFieldInfo field, double x, double y, double w, double h, TextBox tb)
    {
        _vm!.SelectedField = field;
        _activeTb = tb;
        _activeFieldInfo = field;

        // Apply 2px blue border + light blue tint to the TextBox
        tb.BorderBrush = AdobeBlue;
        tb.BorderThickness = new Thickness(2);
        tb.Background = new SolidColorBrush(Color.FromArgb(20, 0, 120, 215));

        EnsureFieldChrome();

        // Label: field name above the selected field
        _fieldChromeLabel!.Text = field.Name;

        // Show "Today" button for date-like fields
        if (_fieldChromeToday != null)
            _fieldChromeToday.Visibility = IsDateFieldName(field.Name)
                ? Visibility.Visible : Visibility.Collapsed;

        double labelY = Math.Max(0, y - 18);
        Canvas.SetLeft(_fieldChromeBorder!, x);
        Canvas.SetTop(_fieldChromeBorder!, labelY);
        _fieldChromeBorder!.Width = Math.Max(80, w);
        _fieldChromeBorder.Visibility = Visibility.Visible;

        // Wire clear button
        _fieldChromeClear!.Tag = (tb, field);
    }

    private void HideFieldChrome(TextBox tb)
    {
        if (_fieldChromeBorder != null)
            _fieldChromeBorder.Visibility = Visibility.Collapsed;
        _activeTb = null;
        _activeFieldInfo = null;
    }

    private void EnsureFieldChrome()
    {
        if (_fieldChromeBorder != null) return;

        _fieldChromeLabel = new TextBlock
        {
            FontSize = 10,
            Foreground = AdobeBlue,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(2, 0, 4, 0),
        };

        _fieldChromeClear = new Button
        {
            Content = "✕",
            FontSize = 9,
            Padding = new Thickness(3, 0, 3, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = AdobeBlue,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Clear field",
        };
        _fieldChromeClear.Click += (_, _) =>
        {
            if (_activeTb != null)
            {
                _activeTb.Text = string.Empty;
                if (_activeFieldInfo != null)
                    _vm?.UpdateFieldValue(_activeFieldInfo.Name, string.Empty);
            }
        };

        _fieldChromeToday = new Button
        {
            Content = "Today",
            FontSize = 9,
            Padding = new Thickness(3, 0, 3, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = AdobeBlue,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Insert today's date",
            Visibility = Visibility.Collapsed,
        };
        _fieldChromeToday.Click += (_, _) =>
        {
            if (_activeTb != null && _activeFieldInfo != null)
            {
                var dateStr = DateTime.Today.ToString("MM/dd/yyyy");
                _activeTb.Text = dateStr;
                _vm?.UpdateFieldValue(_activeFieldInfo.Name, dateStr);
            }
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(_fieldChromeLabel);
        row.Children.Add(_fieldChromeToday);
        row.Children.Add(_fieldChromeClear);

        _fieldChromeBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30)),
            BorderBrush = AdobeBlue,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Height = 16,
            Padding = new Thickness(2, 0, 2, 0),
            Child = row,
            IsHitTestVisible = true,
            Visibility = Visibility.Collapsed,
        };

        Panel.SetZIndex(_fieldChromeBorder, 999);
        FieldOverlayCanvas.Children.Add(_fieldChromeBorder);
    }

    // ── Highlight annotation overlay ──────────────────────────────────────────

    private void BuildHighlightAnnotationOverlay(IEnumerable<Models.HighlightAnnotation> highlights)
    {
        HlAnnotCanvas.Children.Clear();
        if (_vm?.Document == null) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
        foreach (var hl in highlights)
            PlaceHighlightAnnotationVisual(hl, pageH);
    }

    private void PlaceHighlightAnnotationVisual(Models.HighlightAnnotation hl, double pageH)
    {
        double x = hl.Left * Scale;
        double y = (pageH - hl.Bottom - hl.Height) * Scale;
        double w = hl.Width * Scale;
        double h = hl.Height * Scale;

        Color c = ParseColor(hl.Color);
        byte alpha = (byte)(hl.Opacity * 200);

        FrameworkElement elem;
        if (hl.Kind == Models.HighlightKind.Highlight)
        {
            var rect = new Rectangle
            {
                Width = w, Height = h,
                Fill = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)),
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            elem = rect;
        }
        else if (hl.Kind == Models.HighlightKind.Underline)
        {
            double lineH = Math.Max(2, h * 0.1);
            var rect = new Rectangle
            {
                Width = w, Height = lineH,
                Fill = new SolidColorBrush(Color.FromArgb(220, c.R, c.G, c.B)),
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y + h - lineH);
            elem = rect;
        }
        else // Strikethrough
        {
            double lineH = Math.Max(2, h * 0.1);
            var rect = new Rectangle
            {
                Width = w, Height = lineH,
                Fill = new SolidColorBrush(Color.FromArgb(220, c.R, c.G, c.B)),
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y + (h - lineH) / 2);
            elem = rect;
        }

        elem.ToolTip = "Right-click to delete highlight";
        var cm = new ContextMenu();
        var delItem = new MenuItem { Header = "Delete Highlight" };
        delItem.Click += (_, _) =>
        {
            _vm!.RemoveHighlightAnnotation(hl);
            HlAnnotCanvas.Children.Remove(elem);
        };
        cm.Items.Add(delItem);
        elem.ContextMenu = cm;

        HlAnnotCanvas.Children.Add(elem);
    }

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Yellow; }
    }

    // ── Redaction annotation overlay ──────────────────────────────────────────

    private void BuildRedactAnnotationOverlay(IEnumerable<Models.RedactRegion> regions)
    {
        RdAnnotCanvas.Children.Clear();
        if (_vm?.Document == null) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
        foreach (var r in regions)
            PlaceRedactVisual(r, pageH);
    }

    private void PlaceRedactVisual(Models.RedactRegion r, double pageH)
    {
        double x = r.Left * Scale;
        double y = (pageH - r.Bottom - r.Height) * Scale;
        double w = r.Width * Scale;
        double h = r.Height * Scale;

        var rect = new Rectangle
        {
            Width = w, Height = h,
            Fill = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)),
            Stroke = new SolidColorBrush(Colors.Red),
            StrokeThickness = 1.5,
            ToolTip = "Redaction — right-click to remove",
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);

        var cm = new ContextMenu();
        var delItem = new MenuItem { Header = "Remove Redaction Box" };
        delItem.Click += (_, _) =>
        {
            _vm!.RemoveRedactRegion(r);
            RdAnnotCanvas.Children.Remove(rect);
        };
        cm.Items.Add(delItem);
        rect.ContextMenu = cm;

        RdAnnotCanvas.Children.Add(rect);
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
        // Handle ink stroke annotations stored as __INK__:<color>:<pts>
        if (ann.Text.StartsWith("__INK__:", StringComparison.Ordinal))
        {
            var parts = ann.Text.Split(':', 3);
            if (parts.Length == 3)
            {
                var poly = new Polyline
                {
                    Stroke = new SolidColorBrush(ParseColor(parts[1])),
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = true,
                    ToolTip = "Ink stroke — right-click to delete"
                };
                foreach (var ptStr in parts[2].Split(';'))
                {
                    var xy = ptStr.Split(',');
                    if (xy.Length == 2 &&
                        double.TryParse(xy[0], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double ptX) &&
                        double.TryParse(xy[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double ptY))
                    {
                        poly.Points.Add(new Point(ptX * Scale, (pageHeightPts - ptY) * Scale));
                    }
                }
                poly.MouseRightButtonDown += (_, re) =>
                {
                    _vm?.FreeTextAnnotations.Remove(ann);
                    AnnotationCanvas.Children.Remove(poly);
                    re.Handled = true;
                };
                AnnotationCanvas.Children.Add(poly);
            }
            return;
        }

        double x = ann.Left * Scale;
        double y = (pageHeightPts - ann.Bottom - ann.Height) * Scale;
        double w = ann.Width * Scale;
        double h = ann.Height * Scale;

        // Highlight annotations render as a colored rectangle, not a TextBox
        if (ann.IsHighlight)
        {
            Brush fillBrush;
            try { fillBrush = ParseBrush(ann.HighlightColor); }
            catch { fillBrush = new SolidColorBrush(Color.FromArgb(128, 255, 255, 0)); }

            var rect = new Rectangle
            {
                Width = w, Height = h,
                Fill = fillBrush,
                Stroke = new SolidColorBrush(Color.FromArgb(100, 200, 180, 0)),
                StrokeThickness = 1,
                IsHitTestVisible = true,
                Cursor = Cursors.Arrow,
                ToolTip = "Highlight — right-click to delete",
            };
            var hlCtxMenu = new ContextMenu();
            var del = new MenuItem { Header = "Delete Highlight" };
            del.Click += (_, _) => { _vm?.FreeTextAnnotations.Remove(ann); RefreshPage(); };
            hlCtxMenu.Items.Add(del);
            rect.ContextMenu = hlCtxMenu;
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            AnnotationCanvas.Children.Add(rect);
            return;
        }

        var tb = new TextBox
        {
            Width = ann.IsVertical ? h : w,
            Height = ann.IsVertical ? w : h,
            Text = ann.ForceUpperCase ? ann.Text.ToUpperInvariant() : ann.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 200)),
            BorderBrush = ann.IsLocked
                ? new SolidColorBrush(Color.FromArgb(120, 200, 140, 0))   // amber = locked
                : new SolidColorBrush(Color.FromArgb(120, 70, 130, 180)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            IsReadOnly = ann.IsLocked,
            ToolTip = ann.IsLocked
                ? "Locked annotation — right-click to unlock"
                : "Annotation — drag the toolbar to move · right-click to delete"
        };
        System.Windows.Automation.AutomationProperties.SetName(tb, "Text annotation");

        ApplyAnnotationFormatting(ann, tb);

        if (ann.RotationAngle != 0)
            tb.LayoutTransform = new RotateTransform(ann.RotationAngle);

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
            if (_vm != null) _vm.SelectedAnnotation = ann;
            if (ann.IsLocked)
            {
                // Locked — just highlight with amber, no toolbar/resize
                tb.BorderBrush = new SolidColorBrush(Color.FromArgb(200, 200, 140, 0));
                tb.BorderThickness = new Thickness(2);
                return;
            }
            _focusedAnnotation = ann;
            _focusedAnnotationTb = tb;
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

        var cutAnn = new MenuItem { Header = "Cut", InputGestureText = "Ctrl+X" };
        cutAnn.Click += (_, _) => tb.Cut();
        var copyAnn = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C" };
        copyAnn.Click += (_, _) => tb.Copy();
        var pasteAnn = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V" };
        pasteAnn.Click += (_, _) => tb.Paste();
        var selAllAnn = new MenuItem { Header = "Select All", InputGestureText = "Ctrl+A" };
        selAllAnn.Click += (_, _) => tb.SelectAll();
        cm.Items.Add(cutAnn);
        cm.Items.Add(copyAnn);
        cm.Items.Add(pasteAnn);
        cm.Items.Add(selAllAnn);
        cm.Items.Add(new Separator());

        var rotateCwAnn = new MenuItem { Header = "Rotate 90° Clockwise" };
        rotateCwAnn.Click += (_, _) =>
        {
            ann.RotationAngle = (ann.RotationAngle + 90) % 360;
            tb.LayoutTransform = ann.RotationAngle == 0 ? Transform.Identity : new RotateTransform(ann.RotationAngle);
        };
        var rotateCcwAnn = new MenuItem { Header = "Rotate 90° Counter-clockwise" };
        rotateCcwAnn.Click += (_, _) =>
        {
            ann.RotationAngle = (ann.RotationAngle - 90 + 360) % 360;
            tb.LayoutTransform = ann.RotationAngle == 0 ? Transform.Identity : new RotateTransform(ann.RotationAngle);
        };
        cm.Items.Add(rotateCwAnn);
        cm.Items.Add(rotateCcwAnn);
        cm.Items.Add(new Separator());

        var lockItem = new MenuItem();
        lockItem.Header = ann.IsLocked ? "🔓 Unlock Annotation" : "🔒 Lock Annotation";
        lockItem.Click += (_, _) =>
        {
            ann.IsLocked = !ann.IsLocked;
            // Refresh so the visual reflects the new locked state
            RefreshPage();
        };
        cm.Items.Add(lockItem);
        cm.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "Delete Annotation", IsEnabled = !ann.IsLocked };
        deleteItem.Click += (_, _) =>
        {
            if (ann.IsLocked) return;
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
        };

        // SE resize thumb for signatures
        var resizeFactory = new FrameworkElementFactory(typeof(Border));
        resizeFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 120, 215)));
        resizeFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        resizeFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Colors.White));
        resizeFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var sigResizeThumb = new System.Windows.Controls.Primitives.Thumb
        {
            Width = ThumbSize,
            Height = ThumbSize,
            Cursor = Cursors.SizeNWSE,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = Visibility.Collapsed,
            ToolTip = "Drag to resize signature",
            Template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb))
            {
                VisualTree = resizeFactory,
            },
        };

        double sigResizeStartW = 0, sigResizeStartH = 0, sigResizeStartBottom = 0;
        Grid container = null!; // forward reference; assigned below before any lambda fires

        sigResizeThumb.DragStarted += (_, _) =>
        {
            sigResizeStartW = sig.Width;
            sigResizeStartH = sig.Height;
            sigResizeStartBottom = sig.Bottom;
        };

        sigResizeThumb.DragDelta += (_, args) =>
        {
            double dw = args.HorizontalChange / Scale;
            double dh = args.VerticalChange / Scale;

            double newW = Math.Max(20 / Scale, sig.Width + dw);
            double newH = Math.Max(10 / Scale, sig.Height + dh);
            sig.Bottom -= (newH - sig.Height);
            sig.Width = newW;
            sig.Height = newH;
            img.Width = sig.Width * Scale;
            img.Height = sig.Height * Scale;
            container.Width = img.Width;
            container.Height = img.Height;
        };

        // Container grid: image fills it, resize thumb anchored SE
        container = new Grid
        {
            Width = w,
            Height = h,
            ToolTip = "Signature — drag to move · right-click to delete · drag corner to resize",
        };
        container.Children.Add(img);
        container.Children.Add(sigResizeThumb);

        // Context menu
        var cm = new ContextMenu();
        var delItem = new MenuItem { Header = "Delete Signature" };
        delItem.Click += (_, _) =>
        {
            _vm!.RemovePlacedSignature(sig);
            AnnotationCanvas.Children.Remove(container);
        };
        cm.Items.Add(delItem);
        container.ContextMenu = cm;

        // Show/hide resize thumb on hover
        container.MouseEnter += (_, _) => sigResizeThumb.Visibility = Visibility.Visible;
        container.MouseLeave += (_, _) =>
        {
            if (!sigResizeThumb.IsDragging)
                sigResizeThumb.Visibility = Visibility.Collapsed;
        };
        sigResizeThumb.DragCompleted += (_, _) => sigResizeThumb.Visibility = Visibility.Collapsed;

        // Drag-to-move the entire signature
        bool isDragging = false;
        Point dragStart = default;
        double dragStartX = 0, dragStartY = 0;

        container.MouseLeftButtonDown += (_, e2) =>
        {
            if (sigResizeThumb.IsDragging) return;
            isDragging = true;
            dragStart = e2.GetPosition(AnnotationCanvas);
            dragStartX = Canvas.GetLeft(container);
            dragStartY = Canvas.GetTop(container);
            container.CaptureMouse();
            e2.Handled = true;
        };
        container.MouseMove += (_, e2) =>
        {
            if (!isDragging) return;
            var pos = e2.GetPosition(AnnotationCanvas);
            double newX = dragStartX + (pos.X - dragStart.X);
            double newY = dragStartY + (pos.Y - dragStart.Y);
            Canvas.SetLeft(container, newX);
            Canvas.SetTop(container, newY);
            e2.Handled = true;
        };
        container.MouseLeftButtonUp += (_, e2) =>
        {
            if (!isDragging) return;
            isDragging = false;
            container.ReleaseMouseCapture();
            if (_vm?.Document != null)
            {
                int pageNum = _vm.CurrentPageIndex + 1;
                if (pageNum >= 1 && pageNum <= _vm.Document.PageSizes.Count)
                {
                    double pgH = _vm.Document.PageSizes[pageNum - 1].Height;
                    sig.Left = Canvas.GetLeft(container) / Scale;
                    sig.Bottom = pgH - (Canvas.GetTop(container) / Scale) - sig.Height;
                }
            }
            e2.Handled = true;
        };

        Canvas.SetLeft(container, x);
        Canvas.SetTop(container, y);
        return container;
    }

    // ── Paste image from clipboard as a placed annotation ─────────────────────

    private void PasteImageAnnotation(System.Windows.Media.Imaging.BitmapSource img)
    {
        if (_vm?.Document == null) return;

        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

        // Encode image to PNG bytes
        byte[] bytes;
        try
        {
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
            using var ms = new MemoryStream();
            enc.Save(ms);
            bytes = ms.ToArray();
        }
        catch { return; }

        // Place at center of the viewport, 200×100 pts default size
        double defaultWPts = 200;
        double defaultHPts = Math.Round(defaultWPts * img.PixelHeight / Math.Max(1, img.PixelWidth));
        double centerLeftPts = (_vm.Document.PageSizes[pageNum - 1].Width - defaultWPts) / 2;
        double centerBottomPts = (pageH - defaultHPts) / 2;

        var placed = new Models.PlacedSignature
        {
            PageNumber = pageNum,
            Left = centerLeftPts,
            Bottom = centerBottomPts,
            Width = defaultWPts,
            Height = defaultHPts,
            ImageBytes = bytes,
        };

        _vm.AddPlacedSignature(placed);
        AnnotationCanvas.Children.Add(BuildSignatureImage(placed, pageH));

        var capturedSig = placed;
        _vm.PushUndo(
            undo: () => { _vm.RemovePlacedSignature(capturedSig); RefreshPage(); },
            redo: () => { _vm.AddPlacedSignature(capturedSig);    RefreshPage(); });

        _vm.StatusText = "Image pasted from clipboard.";
        ToastService.Instance.Success("Image pasted — drag to reposition, corner to resize.");
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

        if (tool is ActiveTool.Checkmark or ActiveTool.XMark
                 or ActiveTool.Dot or ActiveTool.Line or ActiveTool.Circle)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;

            FinalizeAnnotationBox();
            (string glyph, string colour) = tool switch
            {
                ActiveTool.Checkmark => ("✓", "#2E7D32"),
                ActiveTool.XMark => ("✕", "#C62828"),
                ActiveTool.Dot => ("●", "#1A1A1A"),
                ActiveTool.Line => ("—", "#1A1A1A"),
                _ => ("○", "#1A1A1A"), // Circle
            };
            PlaceStampAnnotation(posOnPage, glyph, colour);
            e.Handled = true;
            return;
        }

        if (tool == ActiveTool.Signature)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;
            PlaceSignatureAtPoint(posOnPage);
            e.Handled = true;
            return;
        }

        if (tool is ActiveTool.Highlight or ActiveTool.Underline or ActiveTool.Strikethrough)
        {
            var posOnPage = e.GetPosition(HlAnnotCanvas);
            if (!IsOnPage(posOnPage)) return;
            _isDrawingHighlight = true;
            _highlightDragStart = posOnPage;
            _highlightRubberBand = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(100, 255, 255, 0)),
                Stroke = new SolidColorBrush(Color.FromArgb(180, 200, 150, 0)),
                StrokeThickness = 1,
                Width = 0,
                Height = 0,
            };
            Canvas.SetLeft(_highlightRubberBand, posOnPage.X);
            Canvas.SetTop(_highlightRubberBand, posOnPage.Y);
            HlAnnotCanvas.Children.Add(_highlightRubberBand);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (tool == ActiveTool.Stamp)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;
            FinalizeAnnotationBox();
            PlaceRubberStampAnnotation(posOnPage);
            e.Handled = true;
            return;
        }

        if (tool == ActiveTool.Redact)
        {
            var posOnPage = e.GetPosition(RdAnnotCanvas);
            if (!IsOnPage(posOnPage)) return;
            _isDrawingRedact = true;
            _redactDragStart = posOnPage;
            _redactRubberBand = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(160, 20, 20, 20)),
                Stroke = new SolidColorBrush(Colors.Red),
                StrokeThickness = 1.5,
                Width = 0,
                Height = 0,
            };
            Canvas.SetLeft(_redactRubberBand, posOnPage.X);
            Canvas.SetTop(_redactRubberBand, posOnPage.Y);
            RdAnnotCanvas.Children.Add(_redactRubberBand);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (tool == ActiveTool.Link)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;
            _isDrawingLink = true;
            _linkDragStart = posOnPage;
            _linkRubberBand = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(40, 0, 100, 255)),
                Stroke = new SolidColorBrush(Color.FromArgb(200, 0, 80, 220)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Width = 0,
                Height = 0,
            };
            Canvas.SetLeft(_linkRubberBand, posOnPage.X);
            Canvas.SetTop(_linkRubberBand, posOnPage.Y);
            AnnotationCanvas.Children.Add(_linkRubberBand);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (tool == ActiveTool.DrawFreehand)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;
            _isDrawingFreehand = true;
            _freehandPoints.Clear();
            _freehandPoints.Add(posOnPage);
            _freehandPolyline = new Polyline
            {
                Stroke = new SolidColorBrush(ParseColor(_vm?.CurrentDrawingColor ?? "#1A1A1A")),
                StrokeThickness = _vm?.CurrentStrokeWidth ?? 2.0,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            _freehandPolyline.Points.Add(posOnPage);
            AnnotationCanvas.Children.Add(_freehandPolyline);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (tool == ActiveTool.StickyNote)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;
            PlaceStickyNote(posOnPage);
            e.Handled = true;
            return;
        }

        if (tool is ActiveTool.DrawRectangle or ActiveTool.DrawEllipse or ActiveTool.DrawArrow or ActiveTool.DrawCallout)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;
            _isDrawingShape = true;
            _shapeDragStart = posOnPage;
            _shapeTool = tool;
            string colorHex = _vm?.CurrentDrawingColor ?? "#C62828";
            var strokeBrush = new SolidColorBrush(ParseColor(colorHex));
            string fillHex = _vm?.CurrentFillColor ?? "";
            Brush shapeFill = string.IsNullOrEmpty(fillHex)
                ? new SolidColorBrush(Color.FromArgb(30, strokeBrush.Color.R, strokeBrush.Color.G, strokeBrush.Color.B))
                : new SolidColorBrush(ParseColor(fillHex));

            double sw = _vm?.CurrentStrokeWidth ?? 2.0;
            if (tool == ActiveTool.DrawArrow)
            {
                _arrowRubberBand = new System.Windows.Shapes.Line
                {
                    Stroke = strokeBrush,
                    StrokeThickness = sw,
                    X1 = posOnPage.X, Y1 = posOnPage.Y,
                    X2 = posOnPage.X, Y2 = posOnPage.Y,
                };
                AnnotationCanvas.Children.Add(_arrowRubberBand);
            }
            else
            {
                System.Windows.Shapes.Shape rb = tool == ActiveTool.DrawEllipse
                    ? new System.Windows.Shapes.Ellipse()
                    : new Rectangle();
                rb.Fill = shapeFill;
                rb.Stroke = strokeBrush;
                rb.StrokeThickness = sw;
                rb.Width  = 0;
                rb.Height = 0;
                Canvas.SetLeft(rb, posOnPage.X);
                Canvas.SetTop(rb,  posOnPage.Y);
                _shapeRubberBand = rb;
                AnnotationCanvas.Children.Add(rb);
            }
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (tool == ActiveTool.Eraser)
        {
            var pos = e.GetPosition(AnnotationCanvas);
            EraseAnnotationsNear(pos, eraserRadius: 16);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (tool is ActiveTool.AddTextField or ActiveTool.AddCheckbox or ActiveTool.AddComboBox or ActiveTool.AddRadioButton)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;
            _isDrawingFormField = true;
            _formFieldTool = tool;
            _formFieldDragStart = posOnPage;
            var strokeColor = tool == ActiveTool.AddCheckbox ? Color.FromArgb(200, 30, 160, 30)
                            : tool == ActiveTool.AddRadioButton ? Color.FromArgb(200, 160, 80, 0)
                            : Color.FromArgb(200, 30, 90, 220);
            _formFieldRubberBand = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(30, strokeColor.R, strokeColor.G, strokeColor.B)),
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Width = 0,
                Height = 0,
            };
            Canvas.SetLeft(_formFieldRubberBand, posOnPage.X);
            Canvas.SetTop(_formFieldRubberBand, posOnPage.Y);
            AnnotationCanvas.Children.Add(_formFieldRubberBand);
            CaptureMouse();
            e.Handled = true;
        }
    }

    private bool IsOnPage(Point p)
    {
        double w = AnnotationCanvas.Width > 0 ? AnnotationCanvas.Width : AnnotationCanvas.ActualWidth;
        double h = AnnotationCanvas.Height > 0 ? AnnotationCanvas.Height : AnnotationCanvas.ActualHeight;
        return p.X >= 0 && p.Y >= 0 && p.X <= w && p.Y <= h;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.ActiveTool == ActiveTool.Eraser && IsMouseCaptured)
        {
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (_isPanning)
        {
            _isPanning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            return;
        }

        if (_isDrawingHighlight)
        {
            _isDrawingHighlight = false;
            ReleaseMouseCapture();

            if (_highlightRubberBand != null && _vm?.Document != null)
            {
                double rectW = _highlightRubberBand.Width;
                double rectH = _highlightRubberBand.Height;
                double canvasX = Canvas.GetLeft(_highlightRubberBand);
                double canvasY = Canvas.GetTop(_highlightRubberBand);
                HlAnnotCanvas.Children.Remove(_highlightRubberBand);
                _highlightRubberBand = null;

                if (rectW > 4 && rectH > 4)
                {
                    int pageNum = _vm.CurrentPageIndex + 1;
                    if (pageNum >= 1 && pageNum <= _vm.Document.PageSizes.Count)
                    {
                        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
                        var kind = _vm.ActiveTool switch
                        {
                            ActiveTool.Underline      => Models.HighlightKind.Underline,
                            ActiveTool.Strikethrough  => Models.HighlightKind.Strikethrough,
                            _                         => Models.HighlightKind.Highlight,
                        };
                        var hl = new Models.HighlightAnnotation
                        {
                            Left    = canvasX / Scale,
                            Bottom  = pageH - (canvasY / Scale) - (rectH / Scale),
                            Width   = rectW / Scale,
                            Height  = rectH / Scale,
                            Color   = _vm.CurrentHighlightColor,
                            Opacity = _vm.CurrentHighlightOpacity,
                            Kind    = kind,
                        };
                        _vm.AddHighlightAnnotation(hl);
                        PlaceHighlightAnnotationVisual(hl, pageH);
                        _vm.StatusText = $"{kind} added. Right-click to delete.";
                    }
                }
            }
            e.Handled = true;
            return;
        }

        if (_isDrawingRedact)
        {
            _isDrawingRedact = false;
            ReleaseMouseCapture();

            if (_redactRubberBand != null && _vm?.Document != null)
            {
                double rectW = _redactRubberBand.Width;
                double rectH = _redactRubberBand.Height;
                double canvasX = Canvas.GetLeft(_redactRubberBand);
                double canvasY = Canvas.GetTop(_redactRubberBand);
                RdAnnotCanvas.Children.Remove(_redactRubberBand);
                _redactRubberBand = null;

                if (rectW > 4 && rectH > 4)
                {
                    int pageNum = _vm.CurrentPageIndex + 1;
                    if (pageNum >= 1 && pageNum <= _vm.Document.PageSizes.Count)
                    {
                        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
                        var r = new Models.RedactRegion
                        {
                            Left   = canvasX / Scale,
                            Bottom = pageH - (canvasY / Scale) - (rectH / Scale),
                            Width  = rectW / Scale,
                            Height = rectH / Scale,
                        };
                        _vm.AddRedactRegion(r);
                        PlaceRedactVisual(r, pageH);
                        _vm.StatusText = "Redaction box added. Click 'Apply Redactions' to burn in.";
                    }
                }
            }
            e.Handled = true;
            return;
        }

        if (_isDrawingLink)
        {
            _isDrawingLink = false;
            ReleaseMouseCapture();

            if (_linkRubberBand != null && _vm?.Document != null)
            {
                double rectW = _linkRubberBand.Width;
                double rectH = _linkRubberBand.Height;
                double canvasX = Canvas.GetLeft(_linkRubberBand);
                double canvasY = Canvas.GetTop(_linkRubberBand);
                AnnotationCanvas.Children.Remove(_linkRubberBand);
                _linkRubberBand = null;

                if (rectW > 6 && rectH > 6)
                {
                    var uriDlg = new Dialogs.LinkUriDialog { Owner = Window.GetWindow(this) };
                    if (uriDlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(uriDlg.Uri))
                    {
                        int pageNum = _vm.CurrentPageIndex + 1;
                        if (pageNum >= 1 && pageNum <= _vm.Document.PageSizes.Count)
                        {
                            double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
                            float left   = (float)(canvasX / Scale);
                            float bottom = (float)(pageH - (canvasY / Scale) - (rectH / Scale));
                            float width  = (float)(rectW / Scale);
                            float height = (float)(rectH / Scale);
                            string uri   = uriDlg.Uri;
                            string srcPath = _vm.CurrentFilePath!;
                            string tmpPath = srcPath + ".tmp";
                            try
                            {
                                var svc = new PdfEdit.Services.PdfFormService();
                                svc.AddLinkAnnotation(srcPath, tmpPath, pageNum, left, bottom, width, height, uri);
                                System.IO.File.Copy(tmpPath, srcPath, overwrite: true);
                                _vm.StatusText = $"Link added to page {pageNum}.";
                                PdfEdit.Services.ToastService.Instance.Success("Hyperlink annotation added.");
                                _ = _vm.ReloadCurrentFileAsync();
                            }
                            catch (Exception ex)
                            {
                                Dialogs.AppDialog.ShowError("Add link failed.", ex);
                            }
                            finally
                            {
                                if (System.IO.File.Exists(tmpPath)) System.IO.File.Delete(tmpPath);
                            }
                        }
                    }
                }
            }
            e.Handled = true;
            return;
        }

        if (_isDrawingFreehand)
        {
            _isDrawingFreehand = false;
            ReleaseMouseCapture();

            if (_freehandPolyline != null && _freehandPolyline.Points.Count >= 2 && _vm?.Document != null)
            {
                // Store the freehand polyline as a FreeTextAnnotation so it saves with the PDF.
                // The visual representation is kept on AnnotationCanvas; the VM stores it in FreeTextAnnotations
                // as a special "Ink" annotation type that gets serialized on save.
                int pageNum = _vm.CurrentPageIndex + 1;
                if (pageNum >= 1 && pageNum <= _vm.Document.PageSizes.Count)
                {
                    double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
                    // Compute bounding box of points
                    var xs = _freehandPolyline.Points.Select(p => p.X);
                    var ys = _freehandPolyline.Points.Select(p => p.Y);
                    double minX = xs.Min(), maxX = xs.Max();
                    double minY = ys.Min(), maxY = ys.Max();

                    // Encode path as a compact string stored as annotation content
                    string encodedPts = string.Join(";", _freehandPolyline.Points.Select(p =>
                        $"{p.X / Scale:F2},{(pageH - p.Y / Scale):F2}"));
                    string colorHex = _vm.CurrentDrawingColor ?? "#000000";

                    var annot = new Models.FreeTextAnnotation
                    {
                        PageNumber = pageNum,
                        Left       = minX / Scale,
                        Bottom     = pageH - (maxY / Scale),
                        Width      = Math.Max((maxX - minX) / Scale, 2),
                        Height     = Math.Max((maxY - minY) / Scale, 2),
                        Text       = $"__INK__:{colorHex}:{encodedPts}",
                        FontSize   = 0,
                        FontFamily = "Ink",
                        FontColor  = colorHex,
                        IsBold     = false, IsItalic = false, IsUnderline = false,
                    };
                    _vm.FreeTextAnnotations.Add(annot);
                    _vm.StatusText = "Ink stroke added.";
                }
            }
            else if (_freehandPolyline != null)
            {
                AnnotationCanvas.Children.Remove(_freehandPolyline);
            }
            _freehandPolyline = null;
            _freehandPoints.Clear();
            e.Handled = true;
            return;
        }

        if (_isDrawingFormField)
        {
            _isDrawingFormField = false;
            ReleaseMouseCapture();

            if (_formFieldRubberBand != null && _vm?.Document != null)
            {
                double rectW = _formFieldRubberBand.Width;
                double rectH = _formFieldRubberBand.Height;
                double canvasX = Canvas.GetLeft(_formFieldRubberBand);
                double canvasY = Canvas.GetTop(_formFieldRubberBand);
                AnnotationCanvas.Children.Remove(_formFieldRubberBand);
                _formFieldRubberBand = null;

                if (rectW > 8 && rectH > 8)
                {
                    var nameDlg = new Dialogs.FieldNameDialog
                    {
                        Owner = Window.GetWindow(this),
                        FieldType = _formFieldTool == ActiveTool.AddCheckbox ? "Checkbox"
                                  : _formFieldTool == ActiveTool.AddComboBox ? "Combo Box"
                                  : _formFieldTool == ActiveTool.AddRadioButton ? "Radio Button"
                                  : "Text Field",
                    };
                    if (nameDlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(nameDlg.FieldName))
                    {
                        int pageNum = _vm.CurrentPageIndex + 1;
                        if (pageNum >= 1 && pageNum <= _vm.Document.PageSizes.Count)
                        {
                            double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
                            float left   = (float)(canvasX / Scale);
                            float bottom = (float)(pageH - (canvasY / Scale) - (rectH / Scale));
                            float width  = (float)(rectW / Scale);
                            float height = (float)(rectH / Scale);
                            string fname = nameDlg.FieldName;
                            ActiveTool fTool = _formFieldTool;
                            string srcPath = _vm.CurrentFilePath!;
                            string tmpPath = srcPath + ".tmp";
                            try
                            {
                                var svc = new PdfEdit.Services.PdfFormService();
                                if (fTool == ActiveTool.AddCheckbox)
                                    svc.AddCheckboxField(srcPath, tmpPath, pageNum, left, bottom, Math.Min(width, height), fname);
                                else if (fTool == ActiveTool.AddComboBox)
                                    svc.AddComboBoxField(srcPath, tmpPath, pageNum, left, bottom, width, height, fname, nameDlg.ComboChoices ?? Array.Empty<string>());
                                else if (fTool == ActiveTool.AddRadioButton)
                                    svc.AddRadioButtonField(srcPath, tmpPath, pageNum, left, bottom, Math.Min(width, height), fname, fname);
                                else
                                    svc.AddTextFormField(srcPath, tmpPath, pageNum, left, bottom, width, height, fname);
                                System.IO.File.Copy(tmpPath, srcPath, overwrite: true);
                                _vm.StatusText = $"Field '{fname}' added to page {pageNum}.";
                                PdfEdit.Services.ToastService.Instance.Success($"Form field '{fname}' added.");
                                _ = _vm.ReloadCurrentFileAsync();
                            }
                            catch (Exception ex)
                            {
                                Dialogs.AppDialog.ShowError("Add form field failed.", ex);
                            }
                            finally
                            {
                                if (System.IO.File.Exists(tmpPath)) System.IO.File.Delete(tmpPath);
                            }
                        }
                    }
                }
            }
            e.Handled = true;
        }

        if (_isDrawingShape)
        {
            _isDrawingShape = false;
            ReleaseMouseCapture();

            if (_vm?.Document != null)
            {
                int pageNum = _vm.CurrentPageIndex + 1;
                if (pageNum >= 1 && pageNum <= _vm.Document.PageSizes.Count)
                {
                    double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
                    string colorHex = _vm.CurrentDrawingColor ?? "#C62828";
                    string fillHex  = _vm.CurrentFillColor ?? "";
                    double strokeW = _vm.CurrentStrokeWidth;

                    if (_arrowRubberBand != null)
                    {
                        double dx = Math.Abs(_arrowRubberBand.X2 - _arrowRubberBand.X1);
                        double dy = Math.Abs(_arrowRubberBand.Y2 - _arrowRubberBand.Y1);
                        if (dx > 4 || dy > 4)
                        {
                            var shape = new Models.ShapeAnnotation
                            {
                                Kind        = Models.ShapeKind.Arrow,
                                X1          = _arrowRubberBand.X1 / Scale,
                                Y1          = pageH - _arrowRubberBand.Y1 / Scale,
                                X2          = _arrowRubberBand.X2 / Scale,
                                Y2          = pageH - _arrowRubberBand.Y2 / Scale,
                                StrokeColor = colorHex,
                                LineWidth   = strokeW,
                            };
                            _vm.AddShapeAnnotation(shape);
                            PlaceShapeVisual(shape, pageH);
                            _vm.StatusText = "Arrow annotation added. Right-click to delete.";
                        }
                        AnnotationCanvas.Children.Remove(_arrowRubberBand);
                        _arrowRubberBand = null;
                    }
                    else if (_shapeRubberBand != null)
                    {
                        double rectW = _shapeRubberBand.Width;
                        double rectH2 = _shapeRubberBand.Height;
                        double canvasX = Canvas.GetLeft(_shapeRubberBand);
                        double canvasY = Canvas.GetTop(_shapeRubberBand);
                        bool isEllipse = _shapeRubberBand is System.Windows.Shapes.Ellipse;
                        bool isCallout = _shapeTool == ActiveTool.DrawCallout;
                        AnnotationCanvas.Children.Remove(_shapeRubberBand);
                        _shapeRubberBand = null;

                        if (rectW > 4 && rectH2 > 4)
                        {
                            double left   = canvasX / Scale;
                            double bottom = pageH - (canvasY + rectH2) / Scale;

                            if (isCallout)
                            {
                                var dlg = new Dialogs.InputDialog("Callout Text", "Enter the text for the callout:", "");
                                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
                                {
                                    string defaultFill = string.IsNullOrEmpty(fillHex) ? "#FFFDE7" : fillHex;
                                    var shape = new Models.ShapeAnnotation
                                    {
                                        Kind        = Models.ShapeKind.Callout,
                                        X1          = left,
                                        Y1          = bottom,
                                        X2          = left + rectW / Scale,
                                        Y2          = bottom + rectH2 / Scale,
                                        StrokeColor = colorHex,
                                        FillColor   = defaultFill,
                                        LineWidth   = strokeW,
                                        CalloutText = dlg.InputText.Trim(),
                                    };
                                    _vm.AddShapeAnnotation(shape);
                                    PlaceCalloutVisual(shape, pageH);
                                    _vm.StatusText = "Callout annotation added. Right-click to delete.";
                                }
                            }
                            else
                            {
                                var shape = new Models.ShapeAnnotation
                                {
                                    Kind        = isEllipse ? Models.ShapeKind.Ellipse : Models.ShapeKind.Rectangle,
                                    X1          = left,
                                    Y1          = bottom,
                                    X2          = left + rectW / Scale,
                                    Y2          = bottom + rectH2 / Scale,
                                    StrokeColor = colorHex,
                                    FillColor   = fillHex,
                                    LineWidth   = strokeW,
                                };
                                _vm.AddShapeAnnotation(shape);
                                PlaceShapeVisual(shape, pageH);
                                _vm.StatusText = $"{(isEllipse ? "Ellipse" : "Rectangle")} annotation added. Right-click to delete.";
                            }
                        }
                    }
                }
            }
            else
            {
                if (_arrowRubberBand  != null) { AnnotationCanvas.Children.Remove(_arrowRubberBand);  _arrowRubberBand  = null; }
                if (_shapeRubberBand  != null) { AnnotationCanvas.Children.Remove(_shapeRubberBand);  _shapeRubberBand  = null; }
            }
            e.Handled = true;
        }

        if (_isDraggingHighlight)
        {
            _isDraggingHighlight = false;
            ReleaseMouseCapture();

            var endPos = e.GetPosition(AnnotationCanvas);
            if (_highlightPreview != null)
                AnnotationCanvas.Children.Remove(_highlightPreview);
            _highlightPreview = null;

            double x = Math.Min(_highlightDragStart.X, endPos.X);
            double y = Math.Min(_highlightDragStart.Y, endPos.Y);
            double w = Math.Abs(endPos.X - _highlightDragStart.X);
            double h = Math.Abs(endPos.Y - _highlightDragStart.Y);

            if (w < 4 || h < 4 || _vm?.Document == null) return;

            int pageNum = _vm.CurrentPageIndex + 1;
            if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
            double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

            var ann = new FreeTextAnnotation
            {
                PageNumber = pageNum,
                Left   = x / Scale,
                Bottom = pageH - (y / Scale) - (h / Scale),
                Width  = w / Scale,
                Height = h / Scale,
                IsHighlight = true,
                HighlightColor = _vm.ActiveHighlightColor,
                Text = string.Empty,
            };
            _vm.FreeTextAnnotations.Add(ann);
            _vm.PushUndo(
                undo: () => { _vm.FreeTextAnnotations.Remove(ann); RefreshPage(); },
                redo: () => { _vm.FreeTextAnnotations.Add(ann);    RefreshPage(); });
            RefreshPage();
            _vm.StatusText = "Highlight placed.";
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(this);
            PdfScrollViewer.ScrollToHorizontalOffset(_scrollHStart + (_panStart.X - pos.X));
            PdfScrollViewer.ScrollToVerticalOffset(_scrollVStart + (_panStart.Y - pos.Y));
            return;
        }

        if (_isDrawingHighlight && _highlightRubberBand != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(HlAnnotCanvas);
            double x = Math.Min(pos.X, _highlightDragStart.X);
            double y = Math.Min(pos.Y, _highlightDragStart.Y);
            double w = Math.Abs(pos.X - _highlightDragStart.X);
            double h = Math.Abs(pos.Y - _highlightDragStart.Y);
            Canvas.SetLeft(_highlightRubberBand, x);
            Canvas.SetTop(_highlightRubberBand, y);
            _highlightRubberBand.Width  = w;
            _highlightRubberBand.Height = h;
            return;
        }

        if (_isDrawingRedact && _redactRubberBand != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(RdAnnotCanvas);
            double x = Math.Min(pos.X, _redactDragStart.X);
            double y = Math.Min(pos.Y, _redactDragStart.Y);
            double w = Math.Abs(pos.X - _redactDragStart.X);
            double h = Math.Abs(pos.Y - _redactDragStart.Y);
            Canvas.SetLeft(_redactRubberBand, x);
            Canvas.SetTop(_redactRubberBand, y);
            _redactRubberBand.Width  = w;
            _redactRubberBand.Height = h;
            return;
        }

        if (_isDrawingLink && _linkRubberBand != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(AnnotationCanvas);
            double x = Math.Min(pos.X, _linkDragStart.X);
            double y = Math.Min(pos.Y, _linkDragStart.Y);
            double w = Math.Abs(pos.X - _linkDragStart.X);
            double h = Math.Abs(pos.Y - _linkDragStart.Y);
            Canvas.SetLeft(_linkRubberBand, x);
            Canvas.SetTop(_linkRubberBand, y);
            _linkRubberBand.Width  = w;
            _linkRubberBand.Height = h;
            return;
        }

        if (_isDrawingFreehand && _freehandPolyline != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(AnnotationCanvas);
            _freehandPolyline.Points.Add(pos);
            return;
        }

        if (_vm?.ActiveTool == ActiveTool.Eraser && e.LeftButton == MouseButtonState.Pressed && IsMouseCaptured)
        {
            var pos = e.GetPosition(AnnotationCanvas);
            EraseAnnotationsNear(pos, eraserRadius: 16);
            return;
        }

        if (_isDrawingFormField && _formFieldRubberBand != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(AnnotationCanvas);
            double x = Math.Min(pos.X, _formFieldDragStart.X);
            double y = Math.Min(pos.Y, _formFieldDragStart.Y);
            double w = Math.Abs(pos.X - _formFieldDragStart.X);
            double h = Math.Abs(pos.Y - _formFieldDragStart.Y);
            Canvas.SetLeft(_formFieldRubberBand, x);
            Canvas.SetTop(_formFieldRubberBand, y);
            _formFieldRubberBand.Width  = w;
            _formFieldRubberBand.Height = h;
        }

        if (_isDrawingShape && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(AnnotationCanvas);
            if (_arrowRubberBand != null)
            {
                _arrowRubberBand.X2 = pos.X;
                _arrowRubberBand.Y2 = pos.Y;
            }
            else if (_shapeRubberBand != null)
            {
                double x = Math.Min(pos.X, _shapeDragStart.X);
                double y = Math.Min(pos.Y, _shapeDragStart.Y);
                double w = Math.Abs(pos.X - _shapeDragStart.X);
                double h = Math.Abs(pos.Y - _shapeDragStart.Y);
                Canvas.SetLeft(_shapeRubberBand, x);
                Canvas.SetTop(_shapeRubberBand,  y);
                _shapeRubberBand.Width  = w;
                _shapeRubberBand.Height = h;
            }
        }

        if (_isDraggingHighlight && e.LeftButton == MouseButtonState.Pressed && _highlightPreview != null)
        {
            var pos = e.GetPosition(AnnotationCanvas);
            double x = Math.Min(_highlightDragStart.X, pos.X);
            double y = Math.Min(_highlightDragStart.Y, pos.Y);
            double w = Math.Abs(pos.X - _highlightDragStart.X);
            double h = Math.Abs(pos.Y - _highlightDragStart.Y);
            Canvas.SetLeft(_highlightPreview, x);
            Canvas.SetTop(_highlightPreview, y);
            _highlightPreview.Width  = w;
            _highlightPreview.Height = h;
        }
    }

    private void ShowStampMenu(Point posOnCanvas)
    {
        var menu = new ContextMenu { IsOpen = false };

        foreach (var preset in StampPresets)
        {
            var captured = preset;
            var item = new MenuItem { Header = preset };
            item.Click += (_, _) => PlaceStampAnnotation(posOnCanvas, captured, "#B71C1C");
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var customItem = new MenuItem { Header = "Custom stamp…" };
        customItem.Click += (_, _) =>
        {
            var dlg = new Dialogs.SimpleInputDialog("Enter Stamp Text", "Stamp text:", "");
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputValue))
                PlaceStampAnnotation(posOnCanvas, dlg.InputValue.ToUpperInvariant(), "#1A237E");
        };
        menu.Items.Add(customItem);

        menu.PlacementTarget = AnnotationCanvas;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl+wheel = zoom (standard across apps)
        // Plain wheel over the page image = zoom too (like most PDF viewers)
        if (Keyboard.Modifiers == ModifierKeys.Control ||
            (Keyboard.Modifiers == ModifierKeys.None && IsMouseOverPage(e)))
        {
            if (_vm != null) _vm.Zoom += e.Delta > 0 ? 0.1 : -0.1;
            e.Handled = true;
        }
        // Plain wheel without Ctrl scrolls the document (default ScrollViewer behavior)
    }

    private bool IsMouseOverPage(MouseWheelEventArgs e)
    {
        // Only capture plain-wheel zoom when the cursor is over the rendered page image
        var pos = e.GetPosition(PageImage);
        return pos.X >= 0 && pos.Y >= 0
            && pos.X <= PageImage.ActualWidth && pos.Y <= PageImage.ActualHeight;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Don't steal shortcuts when a TextBox / field has focus
        bool textboxFocused = Keyboard.FocusedElement is TextBox or PasswordBox;

        if (e.Key == Key.Escape)
        {
            FinalizeAnnotationBox();
            HideAnnotationToolbar();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && _focusedAnnotation != null && !_focusedAnnotation.IsLocked)
        {
            DeleteFocusedAnnotation();
            e.Handled = true;
            return;
        }

        // Ctrl+D — duplicate focused annotation
        if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control
            && _focusedAnnotation != null && _vm != null)
        {
            var src = _focusedAnnotation;
            var copy = new FreeTextAnnotation
            {
                PageNumber = src.PageNumber,
                Left = src.Left + 10,    // offset slightly so user can see both
                Bottom = src.Bottom - 10,
                Width = src.Width,
                Height = src.Height,
                Text = src.Text,
                FontSize = src.FontSize,
                FontFamily = src.FontFamily,
                IsBold = src.IsBold,
                IsItalic = src.IsItalic,
                IsUnderline = src.IsUnderline,
                FontColor = src.FontColor,
                TextAlignment = src.TextAlignment,
                RotationAngle = src.RotationAngle,
                ForceUpperCase = src.ForceUpperCase,
            };
            _vm.FreeTextAnnotations.Add(copy);
            _vm.PushUndo(
                undo: () => { _vm.FreeTextAnnotations.Remove(copy); RefreshPage(); },
                redo: () => { _vm.FreeTextAnnotations.Add(copy);    RefreshPage(); });
            RefreshPage();
            _vm.StatusText = "Annotation duplicated.";
            e.Handled = true;
            return;
        }

        // Tab/Shift+Tab — cycle focus between form fields on the current page
        if (e.Key == Key.Tab && _vm?.CurrentPageFields.Count > 0)
        {
            bool backward = Keyboard.Modifiers == ModifierKeys.Shift;
            // Sort top-to-bottom, then left-to-right (Bottom = PDF Y from bottom, so descending = top-first)
            var fields = _vm.CurrentPageFields
                .OrderByDescending(f => f.Bottom + f.Height)
                .ThenBy(f => f.Left)
                .ToList();
            if (fields.Count > 0)
            {
                int idx = _activeFieldInfo != null ? fields.IndexOf(_activeFieldInfo) : -1;
                idx = backward
                    ? (idx <= 0 ? fields.Count - 1 : idx - 1)
                    : (idx >= fields.Count - 1 ? 0 : idx + 1);
                var target = fields[idx];
                _vm.SelectedField = target;
                // Focus the TextBox for that field in the overlay
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                {
                    foreach (var child in FieldOverlayCanvas.Children.OfType<TextBox>())
                    {
                        if (child.Tag is FormFieldInfo fi && fi.Name == target.Name)
                        { child.Focus(); child.SelectAll(); break; }
                    }
                });
                e.Handled = true;
                return;
            }
        }

        // Ctrl+Z / Ctrl+Y undo-redo (always active, even in text boxes)
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && _vm != null)
        {
            _vm.Undo();
            // Rebuild canvas because undo may have added/removed annotations
            RefreshPage();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control && _vm != null)
        {
            _vm.Redo();
            RefreshPage();
            e.Handled = true;
            return;
        }

        // Ctrl+V when no TextBox is focused: paste clipboard image as annotation
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control
            && !textboxFocused && _vm?.Document != null)
        {
            if (Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img != null)
                {
                    PasteImageAnnotation(img);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (textboxFocused || _vm == null) return;

        // Tool shortcuts
        switch (e.Key)
        {
            case Key.H: _vm.ActiveTool = ActiveTool.Hand;     e.Handled = true; break;
            case Key.Z: _vm.ActiveTool = ActiveTool.Zoom;     e.Handled = true; break;
            case Key.A: _vm.ActiveTool = ActiveTool.AddText;  e.Handled = true; break;
            case Key.D: _vm.ActiveTool = ActiveTool.DateStamp; e.Handled = true; break;
            case Key.C: _vm.ActiveTool = ActiveTool.Checkmark; e.Handled = true; break;
            case Key.X: _vm.ActiveTool = ActiveTool.XMark;    e.Handled = true; break;
            case Key.S: _vm.ActiveTool = ActiveTool.Signature; e.Handled = true; break;
            case Key.F: _vm.ActiveTool = ActiveTool.TextFill;  e.Handled = true; break;
            // Page navigation via arrow keys when hand tool active
            case Key.Right:
            case Key.Down:
                if (_vm.ActiveTool == ActiveTool.Hand) { _vm.NextPageCommand.Execute(null); e.Handled = true; }
                break;
            case Key.Left:
            case Key.Up:
                if (_vm.ActiveTool == ActiveTool.Hand) { _vm.PreviousPageCommand.Execute(null); e.Handled = true; }
                break;
            // Zoom shortcuts
            case Key.OemPlus: case Key.Add:
                _vm.ZoomInCommand.Execute(null); e.Handled = true; break;
            case Key.OemMinus: case Key.Subtract:
                _vm.ZoomOutCommand.Execute(null); e.Handled = true; break;
            case Key.D0: case Key.NumPad0:
                _vm.ZoomFitCommand.Execute(null); e.Handled = true; break;
            case Key.D1: case Key.NumPad1:
                _vm.ZoomActualCommand.Execute(null); e.Handled = true; break;
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

        var capturedAnn = ann;
        _vm.PushUndo(
            undo: () => { _vm.FreeTextAnnotations.Remove(capturedAnn); RefreshPage(); },
            redo: () => { _vm.FreeTextAnnotations.Add(capturedAnn);    RefreshPage(); });

        _vm.StatusText = $"Stamp '{stampText}' placed.";
        ToastService.Instance.Success($"Stamp placed on page {pageNum}.");
    }

    // ── Rubber stamp (APPROVED / CONFIDENTIAL / etc.) ─────────────────────────

    private void PlaceRubberStampAnnotation(Point posOnCanvas)
    {
        if (_vm?.Document == null) return;

        string label = _vm.SelectedStamp;
        int pageNum  = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageHeightPts = _vm.Document.PageSizes[pageNum - 1].Height;

        string colorHex = label switch
        {
            "APPROVED"     => "#1B5E20",
            "CONFIDENTIAL" => "#B71C1C",
            "DRAFT"        => "#1565C0",
            "FINAL"        => "#1B5E20",
            "VOID"         => "#B71C1C",
            "REJECTED"     => "#B71C1C",
            "NOT APPROVED" => "#B71C1C",
            _              => "#7B1FA2",
        };
        Color c = ParseColor(colorHex);

        double dispW = 130 * Scale;
        double dispH = 34 * Scale;

        double pdfX = posOnCanvas.X / Scale;
        double pdfY = pageHeightPts - (posOnCanvas.Y / Scale) - (dispH / Scale);

        var ann = new FreeTextAnnotation
        {
            PageNumber  = pageNum,
            Left        = pdfX,
            Bottom      = pdfY,
            Width       = dispW / Scale,
            Height      = dispH / Scale,
            Text        = label,
            FontSize    = 18,
            FontFamily  = "Arial",
            IsBold      = true,
            FontColor   = colorHex,
            TextAlignment = System.Windows.TextAlignment.Center,
            RotationAngle = -15,
        };
        _vm.FreeTextAnnotations.Add(ann);

        // Visual rubber stamp border
        var tb = new TextBox
        {
            Width = dispW, Height = dispH,
            Text = label,
            FontSize = Math.Max(8, 18 * Scale / PdfRenderService.PointsToDips),
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Arial"),
            Foreground = new SolidColorBrush(c),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, c.R, c.G, c.B)),
            BorderThickness = new Thickness(2),
            IsReadOnly = true,
            TextAlignment = System.Windows.TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = $"Rubber stamp: {label} — right-click to delete",
        };
        tb.LayoutTransform = new RotateTransform(-15);

        var cm = new ContextMenu();
        var delItem = new MenuItem { Header = $"Delete Stamp \"{label}\"" };
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

        _vm.StatusText = $"'{label}' stamp placed. Right-click to delete.";
        ToastService.Instance.Success($"'{label}' stamp placed.");
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

        double initialRotation = vertical ? -90.0 : 0.0;
        if (initialRotation != 0)
            tb.LayoutTransform = new RotateTransform(initialRotation);

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
            RotationAngle = initialRotation,
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

        if (_vm != null)
        {
            var capturedAnn = ann;
            _vm.PushUndo(
                undo: () => { _vm.FreeTextAnnotations.Remove(capturedAnn); RefreshPage(); },
                redo: () => { _vm.FreeTextAnnotations.Add(capturedAnn);    RefreshPage(); });
            _vm.StatusText = $"Annotation placed: \"{text}\"";
        }
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

        var capturedSig = sig;
        _vm.PushUndo(
            undo: () => { _vm.RemovePlacedSignature(capturedSig); RefreshPage(); },
            redo: () => { _vm.AddPlacedSignature(capturedSig);    RefreshPage(); });

        _vm.StatusText = "Signature placed. Right-click to delete.";
    }

    private SignatureData? OpenSignatureDialog()
    {
        var dlg = new SignatureDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            return dlg.Result;
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

    // ── Sticky note overlay ───────────────────────────────────────────────────

    private void BuildStickyNoteOverlay(IEnumerable<Models.StickyNoteAnnotation> notes)
    {
        if (_vm?.Document == null) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
        foreach (var note in notes)
            PlaceStickyNoteVisual(note, pageH);
    }

    private void PlaceStickyNoteVisual(Models.StickyNoteAnnotation note, double pageH)
    {
        double x = note.Left * Scale;
        double y = (pageH - note.Bottom) * Scale;

        var border = new Border
        {
            Width = 28, Height = 28,
            Background = new SolidColorBrush(ParseColor(note.Color)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 100, 80, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3, 3, 0, 3),
            Cursor = Cursors.Hand,
            ToolTip = $"📌 {note.Author}: {note.Text}",
        };

        var icon = new TextBlock
        {
            Text = "📌",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        border.Child = icon;

        var cm = new ContextMenu();
        var viewItem = new MenuItem { Header = "View Note" };
        viewItem.Click += (_, _) =>
            MessageBox.Show(note.Text, $"Sticky Note — {note.Author}",
                MessageBoxButton.OK, MessageBoxImage.Information);
        var delItem = new MenuItem { Header = "Delete Note" };
        delItem.Click += (_, _) =>
        {
            _vm?.RemoveStickyNote(note);
            AnnotationCanvas.Children.Remove(border);
        };
        cm.Items.Add(viewItem);
        cm.Items.Add(delItem);
        border.ContextMenu = cm;

        Canvas.SetLeft(border, x - 14);
        Canvas.SetTop(border, y - 28);
        AnnotationCanvas.Children.Add(border);
    }

    private void PlaceStickyNote(Point posOnCanvas)
    {
        if (_vm?.Document == null) return;
        var dlg = new Dialogs.StickyNoteDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

        var note = new Models.StickyNoteAnnotation
        {
            Left   = posOnCanvas.X / Scale,
            Bottom = pageH - (posOnCanvas.Y / Scale),
            Text   = dlg.NoteText,
            Author = dlg.Author,
            Color  = dlg.NoteColor,
        };
        _vm.AddStickyNote(note);
        PlaceStickyNoteVisual(note, pageH);
        _vm.StatusText = "Sticky note added. Right-click to delete or view.";
        ToastService.Instance.Success("Sticky note added.");
    }

    // ── Shape Annotation Overlay ──────────────────────────────────────────────

    private void BuildShapeOverlay(IEnumerable<Models.ShapeAnnotation> shapes)
    {
        if (_vm?.Document == null) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
        foreach (var shape in shapes)
            PlaceShapeVisual(shape, pageH);
    }

    private void PlaceShapeVisual(Models.ShapeAnnotation shape, double pageH)
    {
        if (shape.Kind == Models.ShapeKind.Callout)
        {
            PlaceCalloutVisual(shape, pageH);
            return;
        }
        var strokeBrush = new SolidColorBrush(ParseColor(shape.StrokeColor));
        Brush fillBrush;
        if (shape.FillColor == "")
            fillBrush = System.Windows.Media.Brushes.Transparent;
        else if (!string.IsNullOrEmpty(shape.FillColor))
            fillBrush = new SolidColorBrush(ParseColor(shape.FillColor));
        else
            fillBrush = new SolidColorBrush(Color.FromArgb(30, strokeBrush.Color.R, strokeBrush.Color.G, strokeBrush.Color.B));

        System.Windows.UIElement visual;

        if (shape.Kind == Models.ShapeKind.Arrow)
        {
            double x1c = shape.X1 * Scale;
            double y1c = (pageH - shape.Y1) * Scale;
            double x2c = shape.X2 * Scale;
            double y2c = (pageH - shape.Y2) * Scale;
            var line = new System.Windows.Shapes.Line
            {
                X1 = x1c, Y1 = y1c, X2 = x2c, Y2 = y2c,
                Stroke = strokeBrush,
                StrokeThickness = shape.LineWidth,
                StrokeEndLineCap = PenLineCap.Triangle,
            };
            line.Tag = shape;
            line.ToolTip = "Arrow annotation (right-click to delete)";
            var ctxLine = new ContextMenu();
            var delLine = new MenuItem { Header = "Delete Arrow" };
            delLine.Click += (_, _) => { AnnotationCanvas.Children.Remove(line); _vm?.RemoveShapeAnnotation(shape); };
            ctxLine.Items.Add(delLine);
            line.ContextMenu = ctxLine;
            visual = line;
        }
        else
        {
            double left   = shape.X1 * Scale;
            double top    = (pageH - shape.Y2) * Scale;
            double width  = (shape.X2 - shape.X1) * Scale;
            double height = (shape.Y2 - shape.Y1) * Scale;
            System.Windows.Shapes.Shape sh = shape.Kind == Models.ShapeKind.Ellipse
                ? new System.Windows.Shapes.Ellipse { Width = width, Height = height, Fill = fillBrush, Stroke = strokeBrush, StrokeThickness = shape.LineWidth }
                : new Rectangle { Width = width, Height = height, Fill = fillBrush, Stroke = strokeBrush, StrokeThickness = shape.LineWidth };
            Canvas.SetLeft(sh, left);
            Canvas.SetTop(sh,  top);
            sh.Tag = shape;
            sh.ToolTip = $"{shape.Kind} annotation (right-click to delete)";
            var ctx = new ContextMenu();
            var del = new MenuItem { Header = $"Delete {shape.Kind}" };
            del.Click += (_, _) => { AnnotationCanvas.Children.Remove(sh); _vm?.RemoveShapeAnnotation(shape); };
            ctx.Items.Add(del);
            sh.ContextMenu = ctx;
            visual = sh;
        }

        AnnotationCanvas.Children.Add(visual);
    }

    private void PlaceCalloutVisual(Models.ShapeAnnotation shape, double pageH)
    {
        double left   = shape.X1 * Scale;
        double top    = (pageH - shape.Y2) * Scale;
        double width  = (shape.X2 - shape.X1) * Scale;
        double height = (shape.Y2 - shape.Y1) * Scale;

        var strokeBrush = new SolidColorBrush(ParseColor(shape.StrokeColor));
        var fillBrush   = string.IsNullOrEmpty(shape.FillColor)
            ? new SolidColorBrush(Color.FromArgb(240, 255, 253, 231))
            : new SolidColorBrush(ParseColor(shape.FillColor));

        const double tipLen = 22.0;

        var cv = new Canvas
        {
            Width   = width,
            Height  = height + tipLen,
            Tag     = shape,
            ToolTip = $"Callout: {shape.CalloutText}\n(right-click to delete)",
        };
        Canvas.SetLeft(cv, left);
        Canvas.SetTop(cv,  top);

        var box = new Rectangle
        {
            Width           = width,
            Height          = height,
            Fill            = fillBrush,
            Stroke          = strokeBrush,
            StrokeThickness = shape.LineWidth,
            RadiusX         = 3,
            RadiusY         = 3,
        };
        Canvas.SetLeft(box, 0);
        Canvas.SetTop(box,  0);
        cv.Children.Add(box);

        var tb = new TextBlock
        {
            Text         = shape.CalloutText,
            TextWrapping = TextWrapping.Wrap,
            Foreground   = strokeBrush,
            FontSize     = 11,
            Width        = Math.Max(width - 10, 10),
            MaxHeight    = Math.Max(height - 10, 10),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Canvas.SetLeft(tb, 5);
        Canvas.SetTop(tb,  5);
        cv.Children.Add(tb);

        double mid = width / 2.0;
        var pointer = new Polygon
        {
            Points = new PointCollection
            {
                new Point(mid - 8, height),
                new Point(mid + 8, height),
                new Point(mid,     height + tipLen),
            },
            Fill            = fillBrush,
            Stroke          = strokeBrush,
            StrokeThickness = shape.LineWidth,
        };
        cv.Children.Add(pointer);

        var ctx = new ContextMenu();
        var del = new MenuItem { Header = "Delete Callout" };
        del.Click += (_, _) => { AnnotationCanvas.Children.Remove(cv); _vm?.RemoveShapeAnnotation(shape); };
        ctx.Items.Add(del);
        cv.ContextMenu = ctx;
        cv.MouseRightButtonDown += (s2, e2) => { ctx.IsOpen = true; e2.Handled = true; };

        AnnotationCanvas.Children.Add(cv);
    }

    private void EraseAnnotationsNear(Point canvasPos, double eraserRadius)
    {
        if (_vm == null) return;

        // Erase shape annotations (Rectangle, Ellipse, Arrow, Freehand polylines, Callout canvases)
        var toRemoveUi    = new List<UIElement>();
        var toRemoveModel = new List<Models.ShapeAnnotation>();

        foreach (UIElement child in AnnotationCanvas.Children)
        {
            if (child is System.Windows.Shapes.Shape sh)
            {
                var bounds = sh.RenderedGeometry?.Bounds ?? Rect.Empty;
                var offset = sh.TranslatePoint(new Point(0, 0), AnnotationCanvas);
                bounds.Offset(offset.X, offset.Y);
                bounds.Inflate(eraserRadius, eraserRadius);
                if (bounds.Contains(canvasPos))
                {
                    if (sh.Tag is Models.ShapeAnnotation ann)
                    {
                        toRemoveUi.Add(sh);
                        toRemoveModel.Add(ann);
                    }
                    else if (sh is Polyline)
                    {
                        toRemoveUi.Add(sh);
                    }
                }
            }
            else if (child is Canvas cv && cv.Tag is Models.ShapeAnnotation calloutAnn)
            {
                double cvLeft = Canvas.GetLeft(cv);
                double cvTop  = Canvas.GetTop(cv);
                var bounds = new Rect(cvLeft - eraserRadius, cvTop - eraserRadius,
                    cv.Width + eraserRadius * 2, cv.Height + eraserRadius * 2);
                if (bounds.Contains(canvasPos))
                {
                    toRemoveUi.Add(cv);
                    toRemoveModel.Add(calloutAnn);
                }
            }
        }

        foreach (var ui in toRemoveUi) AnnotationCanvas.Children.Remove(ui);
        foreach (var ann in toRemoveModel) _vm.RemoveShapeAnnotation(ann);
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
