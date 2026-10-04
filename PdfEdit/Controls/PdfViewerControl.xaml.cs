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

    private double Scale => RendererFactory.PointsToDips * (_vm?.Zoom ?? 1.0);

    // Adobe Acrobat DC-accurate form-field appearance brushes (frozen for reuse).
    // The fills are OPAQUE on purpose: the rendered page bitmap already contains each field's
    // saved appearance (its old value / check mark). A see-through fill let that show through
    // under the live control, so typed text overlapped the old text and an unticked box still
    // looked ticked. Acrobat likewise paints over the appearance while a form is being filled.
    private static readonly Brush FieldFillBrush          = Freeze(Color.FromRgb(0xE1, 0xE9, 0xFF));
    private static readonly Brush FieldBorderBrush        = Freeze(Color.FromArgb(170,   0,  80, 200));
    private static readonly Brush FieldFocusBrush         = Freeze(Colors.White);
    private static readonly Brush FieldFocusBorderBrush   = Freeze(Color.FromArgb(230,  30, 120, 220));
    private static readonly Brush FieldRequiredBorderBrush= Freeze(Color.FromArgb(210, 200,  30,  30));
    private static readonly SolidColorBrush SigBlueBrush  = new(Color.FromRgb(0, 80, 200));

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

    // Adobe-style field selection chrome (floating mini toolbar shown above a focused text field)
    private Border?    _fieldChromeBorder;
    private Border?    _fieldChromeToday;
    private TextBox?   _activeTb;
    private FormFieldInfo? _activeFieldInfo;

    // Per-field (by field name) UI preferences, applied across re-renders of the same document.
    private readonly Dictionary<string, double> _fieldFontSizes = new();
    private readonly Dictionary<string, int> _fieldRotations = new();
    private readonly Dictionary<string, bool> _fieldAutoSize = new();

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
        // handledEventsToo: a click on the bare page must reach the tools even if something on the
        // way up (the ScrollViewer) marked it handled. Clicks that a field, placed text or a toolbar
        // handled itself are still left alone (see OnPageMouseLeftButtonDown).
        AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPageMouseLeftButtonDown), handledEventsToo: true);
        // Drawing / shape tools must work over form fields and placed text too (they eat the click).
        PreviewMouseLeftButtonDown += OnDrawToolPreviewMouseDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
        KeyDown += OnKeyDown;
        PreviewKeyDown += OnLayoutPreviewKeyDown;
        AllowDrop = true;
        Focusable = true;

        _annotToolbar = BuildAnnotationToolbar();
        InitFormBars();
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
            _vm.FieldValueChangedExternally -= OnFieldValueChangedExternally;
            _vm.AnnotationChanged -= OnAnnotationChanged;
            _vm.FieldSelectionRequested -= OnFieldSelectionRequested;
        }
        _vm = DataContext as MainViewModel;
        if (_vm != null)
        {
            _vm.PageChanged += RefreshPage;
            _vm.DocumentLoaded += OnDocumentLoaded;
            _vm.AnnotationFormattingChanged += OnAnnotationFormattingChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.FieldValueChangedExternally += OnFieldValueChangedExternally;
            _vm.AnnotationChanged += OnAnnotationChanged;
            _vm.FieldSelectionRequested += OnFieldSelectionRequested;

            // If a document is already loaded when DataContext arrives, show it
            if (_vm.Document != null)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(OnDocumentLoaded));
        }
    }

    /// <summary>A field value was edited elsewhere (e.g. the properties panel) — mirror it on the page.</summary>
    private void OnFieldValueChangedExternally(string name, string value)
    {
        if (_builtInLayoutMode) return;
        bool updated = false;
        foreach (var tb in FieldOverlayCanvas.Children.OfType<TextBox>())
        {
            if (tb.Tag is FormFieldInfo f && f.Name == name)
            {
                if (tb.Text != value) tb.Text = value;
                updated = true;
            }
        }
        // Checkboxes, radios and lists have no simple text to patch — rebuild the overlay instead.
        if (!updated) RebuildFieldOverlay();
    }

    private void OnDocumentLoaded()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            PdfScrollViewer.Visibility = Visibility.Visible;
            // Force a synchronous layout pass so ViewportWidth/Height are measured
            // before we read them — otherwise they're still 0 (the ScrollViewer was
            // just Collapsed) and the auto-fit zoom collapses to its 10% floor.
            PdfScrollViewer.UpdateLayout();
            // Seed viewport size then auto-fit before first render so the user
            // sees the whole page without manually hitting Fit Page.
            _vm?.UpdateViewerSize(PdfScrollViewer.ViewportWidth, PdfScrollViewer.ViewportHeight);
            _vm?.AutoFitOnLoad();
            RefreshPage();
            // Flat form: tell the user how to fill it (the boxes are only drawn on the page).
            if (_vm is { AllFields.Count: 0 })
                ToastService.Instance.Info("This PDF has no fillable fields — click inside any box to type in it, like Acrobat Fill & Sign.");
        }));
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Switching between a fill tool and the Select / Add-field tools toggles the live view
        // between filling fields in and moving/resizing them (Acrobat "Prepare Form").
        if (e.PropertyName == nameof(MainViewModel.ActiveTool)) { SyncFormBars(); CancelPoly(); UpdateLinkHitTesting(); }
        if (e.PropertyName == nameof(MainViewModel.ActiveTool) && IsFieldLayoutMode != _builtInLayoutMode)
        {
            if (IsFieldLayoutMode && _vm?.ActiveTool == ActiveTool.EditFields)
                _vm.StatusText = "Edit Fields: click a form field to move it, drag its handles to resize, Del to delete.";
            RebuildFieldOverlay();
        }

        if (_focusedAnnotationTb == null || _focusedAnnotation == null || _vm == null) return;

        // ✓ ✕ ● ○ — are drawn as the box's background with the glyph text hidden. Setting the
        // text colour / size here made the font's own glyph show up as a second mark on top
        // (e.g. when selecting or resizing a mark), so redraw marks from their model instead.
        if (IsDrawn(_focusedAnnotation))
        {
            if (e.PropertyName is nameof(MainViewModel.CurrentFontSize) or nameof(MainViewModel.CurrentFontFamily)
                or nameof(MainViewModel.CurrentFontBold) or nameof(MainViewModel.CurrentFontItalic)
                or nameof(MainViewModel.CurrentFontUnderline) or nameof(MainViewModel.CurrentFontColor)
                or nameof(MainViewModel.CurrentTextAlignment))
                ApplyAnnotationFormatting(_focusedAnnotation, _focusedAnnotationTb);
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CurrentFontSize):
                _focusedAnnotationTb.FontSize = _vm.CurrentFontSize * Scale;
                _focusedAnnotation.FontSize = _vm.CurrentFontSize;
                // Bigger text must not be cut off by its box (auto-size / wrap-and-grow).
                FitAnnotationBox(_focusedAnnotation, _focusedAnnotationTb);
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
        // Ribbon / Properties panel formatting applies to the selected text even after focus has
        // moved to the panel (the text box no longer has focus then).
        var ann = _focusedAnnotation ?? _vm?.SelectedAnnotation;
        if (ann == null) return;
        RefreshAnnotationVisual(ann);
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

        // Adobe Fill & Sign order: smaller · larger · delete · swap
        panel.Children.Add(MakeToolbarBtn("A", "Smaller", () =>
        {
            if (ResizeFocusedMark(1 / 1.15)) return;
            if (_vm != null) _vm.CurrentFontSize = Math.Max(4, _vm.CurrentFontSize - 1);
        }, fontSize: 10));

        panel.Children.Add(MakeToolbarBtn("A", "Larger", () =>
        {
            if (ResizeFocusedMark(1.15)) return;
            if (_vm != null) _vm.CurrentFontSize = Math.Min(144, _vm.CurrentFontSize + 1);
        }, fontSize: 16));

        panel.Children.Add(MakeToolbarBtn("", "Delete (Del)", DeleteFocusedAnnotation,
            fontSize: 14, fontFamily: "Segoe MDL2 Assets"));

        // Rotate 90° clockwise and character spacing — the rest of Acrobat's Fill & Sign toolbar
        panel.Children.Add(MakeToolbarBtn("\uE7AD", "Rotate 90°", RotateFocusedAnnotation,
            fontSize: 14, fontFamily: "Segoe MDL2 Assets"));
        panel.Children.Add(MakeToolbarBtn("VA", "Character spacing", ToggleSpacingPopup, fontSize: 11));

        // How the box follows the text: auto-size, wrap and grow, or fixed (text only, not marks)
        Border? fitBtn = null;
        fitBtn = MakeToolbarBtn("\uE740", "Fit box to text — auto-size, wrap or fixed", () => ShowFitMenu(fitBtn!),
            fontSize: 13, fontFamily: "Segoe MDL2 Assets");
        _fitTextBtn = fitBtn;
        panel.Children.Add(fitBtn);

        // Swap: cycles a placed mark through ✓ ✕ ○ — ● (only shown for marks)
        _swapMarkBtn = MakeToolbarBtn("", "Swap mark", SwapFocusedMark,
            fontSize: 14, fontFamily: "Segoe MDL2 Assets");
        panel.Children.Add(_swapMarkBtn);

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

    private static Border MakeToolbarBtn(string label, string tip, Action onClick, double fontSize = 12,
                                         string? fontFamily = null)
    {
        var border = new Border
        {
            Padding = new Thickness(8, 0, 8, 0),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Background = Brushes.Transparent,
            Child = new TextBlock
            {
                Text = label,
                FontSize = fontSize,
                FontFamily = fontFamily != null ? new FontFamily(fontFamily) : SystemFonts.MessageFontFamily,
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

    // ── Marks (✓ ✕ ○ — ●) ────────────────────────────────────────────────────

    private static readonly string[] MarkGlyphs = { "✓", "✕", "○", "—", "●" };
    private Border? _swapMarkBtn;
    private Border? _fitTextBtn;

    private static bool IsMarkGlyph(string? text) => text != null && MarkGlyphs.Contains(text);

    /// <summary>
    /// A mark fills its box, so its font size changes nothing on screen: the toolbar's smaller /
    /// larger buttons scale the box itself (about its centre, like Acrobat). False if not a mark.
    /// </summary>
    private bool ResizeFocusedMark(double factor)
    {
        var ann = _focusedAnnotation;
        var tb = _focusedAnnotationTb;
        if (ann == null || tb == null || _vm?.Document == null || !IsDrawn(ann) || ann.IsLocked) return false;

        double oldW = ann.Width, oldH = ann.Height;
        double newW = Math.Clamp(oldW * factor, 6, 400), newH = Math.Clamp(oldH * factor, 6, 400);
        if (Math.Abs(newW - oldW) < 0.01 && Math.Abs(newH - oldH) < 0.01) return true;

        void SetSize(double w, double h)
        {
            ann.Left -= (w - ann.Width) / 2;
            ann.Bottom -= (h - ann.Height) / 2;
            ann.Width = w;
            ann.Height = h;
            ann.AutoSize = false;
            if (_annotationBoxes.ContainsKey(ann)) RefreshAnnotationVisual(ann);
            if (ReferenceEquals(_focusedAnnotationTb, tb))
            {
                PositionResizeThumb(tb);
                PositionAnnotationToolbar(Canvas.GetLeft(tb), Canvas.GetTop(tb), tb.Width);
            }
            _vm?.NotifyAnnotationEdited(ann);
        }

        SetSize(newW, newH);
        _vm.PushUndo(undo: () => SetSize(oldW, oldH), redo: () => SetSize(newW, newH));
        return true;
    }

    private void SwapFocusedMark()
    {
        if (_focusedAnnotation == null || _focusedAnnotationTb == null) return;
        int i = Array.IndexOf(MarkGlyphs, _focusedAnnotation.Text);
        if (i < 0) return;
        string next = MarkGlyphs[(i + 1) % MarkGlyphs.Length];
        _focusedAnnotation.Text = next;
        _focusedAnnotationTb.Text = next;
        ApplyAnnotationFormatting(_focusedAnnotation, _focusedAnnotationTb); // redraw the new shape
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
                _vm.NotifyAnnotationEdited(_focusedAnnotation);
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
        if (_swapMarkBtn != null)
            _swapMarkBtn.Visibility = IsMarkGlyph(_focusedAnnotation?.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_fitTextBtn != null)
            _fitTextBtn.Visibility = IsDrawn(_focusedAnnotation) ? Visibility.Collapsed : Visibility.Visible;
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
            // Resizing by hand switches off Acrobat-style auto-size; the text now wraps in the box.
            _focusedAnnotation.AutoSize = false;
            if (_focusedAnnotationTb != null && !IsDrawn(_focusedAnnotation))
                _focusedAnnotationTb.TextWrapping = TextWrapping.Wrap;
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
            if (!IsQuarterTurn(ann.RotationAngle))
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
            // Made the box too small for its text: grow it back to fit (unless "Fixed box size").
            if (_focusedAnnotationTb != null) FitAnnotationBox(_focusedAnnotation, _focusedAnnotationTb);
            // A stamp's drawing is laid out for its box: redraw it at the new proportions.
            if (_focusedAnnotation.IsStamp && _focusedAnnotationTb != null) ApplyAnnotationFormatting(_focusedAnnotation, _focusedAnnotationTb);
            _vm?.NotifyAnnotationEdited(_focusedAnnotation);
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
            double dpiScale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var bmp = await _vm.RenderService.RenderPageAsync(_vm.CurrentPageIndex, _vm.Zoom, dpiScale);
            PageImage.Source = bmp;
            HideRenderDiagnostic();

            // Overlays are positioned in DIPs (see Scale), so size them to the bitmap's
            // DIP size — PixelWidth is larger than the displayed image on high-DPI screens.
            double w = bmp.Width;
            double h = bmp.Height;

            if (bmp.PixelWidth <= 1 || bmp.PixelHeight <= 1)
                ShowRenderDiagnostic($"Rendered page is degenerate ({w}×{h}px). Zoom={_vm.Zoom:F3}, PageIndex={_vm.CurrentPageIndex}.");
            else if (IsBitmapBlank(bmp))
                ShowRenderDiagnostic($"Bitmap decoded OK ({w}×{h}px, Zoom={_vm.Zoom:F3}) but every sampled pixel is blank/white — " +
                    $"the PDF rasterizer ({(_vm.RenderService.RendersAnnotations ? "Pdfium" : "Windows.Data.Pdf")}) returned an empty page, this is not a display/theme issue.");
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
            SyncFormBars();
            BuildHighlightAnnotationOverlay(_vm.GetHighlightAnnotationsForCurrentPage());
            BuildRedactAnnotationOverlay(_vm.GetRedactRegionsForCurrentPage());
            BuildAnnotationOverlay(_vm.GetAnnotationsForCurrentPage());
            BuildSignatureOverlay(_vm.GetSignaturesForCurrentPage());
            BuildStickyNoteOverlay(_vm.GetStickyNotesForCurrentPage());
            BuildShapeOverlay(_vm.GetShapeAnnotationsForCurrentPage());
            BuildTextEditOverlay();
            BuildLinkOverlay(w, h);
        }
        catch (Exception ex)
        {
            if (_vm != null) _vm.StatusText = $"Render error: {ex.Message}";
            ShowRenderDiagnostic($"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            ShowLoading(false);
        }
    }

    // ── Render diagnostics ──────────────────────────────────────────────────
    // Surfaces a render failure directly on the page instead of only in the
    // (easily-missed) status bar, so a blank page always explains itself.

    private TextBlock? _renderDiagnosticText;

    private void ShowRenderDiagnostic(string message)
    {
        if (_renderDiagnosticText == null)
        {
            _renderDiagnosticText = new TextBlock
            {
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(230, 180, 30, 30)),
                FontSize = 13,
                Padding = new Thickness(10),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 600,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
            };
            Panel.SetZIndex(_renderDiagnosticText, 99999);
            PageGrid.Children.Add(_renderDiagnosticText);
        }
        _renderDiagnosticText.Text = "PDF render diagnostic — " + message;
        _renderDiagnosticText.Visibility = Visibility.Visible;
    }

    private void HideRenderDiagnostic()
    {
        if (_renderDiagnosticText != null)
            _renderDiagnosticText.Visibility = Visibility.Collapsed;
    }

    /// <summary>Samples pixels across the bitmap to tell a genuinely blank render (rasterizer
    /// problem) apart from a display/theme problem where the bitmap itself has real content.</summary>
    private static bool IsBitmapBlank(BitmapSource bmp)
    {
        try
        {
            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            int step = Math.Max(1, pixels.Length / 4 / 2000); // sample ~2000 pixels max
            for (int i = 0; i < pixels.Length - 4; i += 4 * step)
            {
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
                bool nearWhite = r > 245 && g > 245 && b > 245;
                bool transparent = a < 10;
                if (!nearWhite && !transparent) return false;
            }
            return true;
        }
        catch
        {
            return false; // sampling failed — don't report a false blank
        }
    }

    // ── AcroForm field overlay ───────────────────────────────────────────────

    private void BuildFieldOverlay(IEnumerable<FormFieldInfo> fields, bool highlight)
    {
        _fieldChromeBorder = null;
        _fieldChromeToday = null;
        ResetFieldGrips();
        _activeTb = null;
        _activeFieldInfo = null;
        ResetLayoutChrome();

        FieldOverlayCanvas.Children.Clear();
        HighlightCanvas.Children.Clear();

        bool layoutMode = IsFieldLayoutMode;
        _builtInLayoutMode = layoutMode;

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

            if (layoutMode)
            {
                AddLayoutFieldBox(field, x, y, w, h);
                continue;
            }

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

            var layout = new MenuItem { Header = "Move / resize field" };
            layout.Click += (_, _) => BeginFieldLayoutEdit(field);
            menu.Items.Add(layout);

            var del = new MenuItem { Header = $"Delete field \"{field.Name}\"" };
            del.Click += (_, _) => _vm?.DeleteField(field);
            menu.Items.Add(del);
            fe.ContextMenu = menu;
        }

        Canvas.SetLeft(ctrl, x);
        Canvas.SetTop(ctrl, y);
        FieldOverlayCanvas.Children.Add(ctrl);

        // Fields can be moved / resized right here while filling (hover → ✥ grip), not only in Edit Fields.
        if (ctrl is FrameworkElement movable)
            AttachFieldGrips(field, movable, new Rect(x, y, w, h));

        // Wire up Adobe-style chrome for every focusable field control
        if (ctrl is TextBox tb)
        {
            if (field.IsDateField) AddDatePicker(field, tb, x, y, w, h);
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
                Background = FieldFill(field),
                Foreground = FieldText(field),
                CaretBrush = Brushes.Black,
                BorderBrush = FieldBorder(field),
                BorderThickness = new Thickness(1),
                FontSize = Math.Max(8, dh * 0.6),
                Padding = new Thickness(2, 0, 2, 0),
                ToolTip = string.IsNullOrEmpty(field.Tooltip) ? field.Name : field.Tooltip,
                Password = _vm!.FieldValues.TryGetValue(field.Name, out var pv) ? pv : field.Value
            };
            System.Windows.Automation.AutomationProperties.SetName(pb, $"Password field: {field.Name}");
            ApplyFieldFont(pb, field);
            pb.PasswordChanged += (_, _) => _vm!.UpdateFieldValue(field.Name, pb.Password);
            pb.GotFocus  += (_, _) => pb.Background = FieldFocusBrush;
            pb.LostFocus += (_, _) => pb.Background = FieldFill(field);
            return pb;
        }

        var tb = new TextBox
        {
            Width = dw, Height = dh,
            Text = _vm!.FieldValues.TryGetValue(field.Name, out var v) ? v : field.Value,
            // Acrobat-style fillable field: faint blue fill + subtle border so the
            // user can clearly see where the fields are and that they are editable.
            Background = FieldFill(field),
            Foreground = FieldText(field),
            CaretBrush = Brushes.Black,
            // Required fields get a red outline, matching Acrobat's convention.
            BorderBrush = field.IsRequired ? FieldRequiredBorderBrush : FieldBorder(field),
            BorderThickness = new Thickness(field.IsRequired ? 1.5 : 1),
            Cursor = Cursors.IBeam,
            FontSize = _fieldFontSizes.TryGetValue(field.Name, out var savedSize) ? savedSize
                     : field.FontSize > 0 ? field.FontSize * Scale
                     // Auto size like Acrobat: 12 pt for multi-line fields (60% of a 90 pt
                     // memo box was unreadably large), 60% of the height for one line.
                     : field.IsMultiline ? 12 * Scale
                     : Math.Max(8, (vertical ? w : h) * 0.6),
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = ToTextAlignment(field.Alignment),
            AcceptsReturn = field.IsMultiline,
            TextWrapping = field.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Padding = new Thickness(2, 0, 2, 0),
            ToolTip = string.IsNullOrEmpty(field.Tooltip) ? field.DisplayName : field.Tooltip
        };
        System.Windows.Automation.AutomationProperties.SetName(tb, $"Form field: {field.DisplayName}");
        ApplyFieldFont(tb, field);

        // Vertical-text fields already use LayoutTransform for their -90° orientation, so the
        // Adobe-style toolbar's Rotate button (which also targets LayoutTransform) is skipped there.
        if (vertical)
            tb.LayoutTransform = new RotateTransform(-90);
        else if (_fieldRotations.TryGetValue(field.Name, out var savedAngle) && savedAngle != 0)
            tb.LayoutTransform = new RotateTransform(savedAngle);

        tb.TextChanged += (_, _) =>
        {
            _vm!.UpdateFieldValue(field.Name, tb.Text);
            if (_fieldAutoSize.TryGetValue(field.Name, out var autoSize) && autoSize)
                ApplyFieldAutoSize(tb, field);
        };
        tb.GotFocus += (_, _) =>
        {
            tb.Background = FieldFocusBrush;
        };
        tb.LostFocus += (_, _) =>
        {
            tb.Background = FieldFill(field);
            tb.BorderBrush = field.IsRequired && string.IsNullOrWhiteSpace(tb.Text)
                ? FieldRequiredBorderBrush
                : FieldBorder(field);
            tb.BorderThickness = new Thickness(field.IsRequired && string.IsNullOrWhiteSpace(tb.Text) ? 1.5 : 1);
        };
        return tb;
    }

    private UIElement BuildCheckBox(FormFieldInfo field, double w, double h)
    {
        var currentVal = _vm!.FieldValues.TryGetValue(field.Name, out var cv) ? cv : field.Value;
        bool isChecked = currentVal == field.ExportValue
            || (currentVal is "Yes" or "true" or "On" or "1");

        var cb = new CheckBox
        {
            IsChecked = isChecked,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = true,
        };
        System.Windows.Automation.AutomationProperties.SetName(cb, $"Checkbox: {field.Name}");
        cb.Checked   += (_, _) => _vm!.UpdateFieldValue(field.Name, field.ExportValue);
        cb.Unchecked += (_, _) => _vm!.UpdateFieldValue(field.Name, "Off");
        cb.GotFocus  += (_, _) => _vm!.SelectedField = field;

        // Wrap in Acrobat-style highlighted field area so the interactive region is visible.
        var container = new Border
        {
            Width = w, Height = h,
            Background = FieldFill(field),
            BorderBrush = FieldBorder(field),
            BorderThickness = new Thickness(1),
            ToolTip = string.IsNullOrEmpty(field.Tooltip) ? field.Name : field.Tooltip,
            Cursor = Cursors.Hand,
            // Scale the glyph with the field (like Acrobat) instead of a fixed 13px box.
            Child = new Viewbox { Child = cb, Margin = new Thickness(Math.Min(2, h * 0.1)) },
        };
        container.GotFocus  += (_, _) => container.Background = FieldFocusBrush;
        container.LostFocus += (_, _) => container.Background = FieldFill(field);
        // Clicking anywhere in the field toggles it, not just on the small glyph.
        container.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled) return;
            cb.IsChecked = cb.IsChecked != true;
            cb.Focus();
            e.Handled = true;
        };
        return container;
    }

    private UIElement BuildRadioButton(FormFieldInfo field, double w, double h)
    {
        var groupVal = _vm!.FieldValues.TryGetValue(field.Name, out var gv) ? gv : field.Value;
        var rb = new RadioButton
        {
            GroupName = field.RadioGroup ?? field.Name,
            IsChecked = groupVal == field.ExportValue,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(rb, $"Radio: {field.Name} = {field.ExportValue}");
        rb.Checked  += (_, _) => _vm!.UpdateFieldValue(field.Name, field.ExportValue);
        rb.GotFocus += (_, _) => _vm!.SelectedField = field;

        var container = new Border
        {
            Width = w, Height = h,
            Background = FieldFill(field),
            BorderBrush = FieldBorder(field),
            BorderThickness = new Thickness(1),
            ToolTip = string.IsNullOrEmpty(field.Tooltip) ? field.Name : field.Tooltip,
            Cursor = Cursors.Hand,
            Child = new Viewbox { Child = rb, Margin = new Thickness(Math.Min(2, h * 0.1)) },
        };
        container.GotFocus  += (_, _) => container.Background = FieldFocusBrush;
        container.LostFocus += (_, _) => container.Background = FieldFill(field);
        container.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled) return;
            rb.IsChecked = true;
            rb.Focus();
            e.Handled = true;
        };
        return container;
    }

    private ComboBox BuildComboBox(FormFieldInfo field, double w, double h)
    {
        var cb = new ComboBox
        {
            Width = w, Height = h,
            FontSize = field.FontSize > 0 ? field.FontSize * Scale : Math.Max(8, h * 0.55),
            HorizontalContentAlignment = ToHorizontalAlignment(field.Alignment),
            ToolTip = field.Name
        };
        System.Windows.Automation.AutomationProperties.SetName(cb, $"Dropdown: {field.Name}");
        ApplyFieldFont(cb, field);
        foreach (var opt in field.Options) cb.Items.Add(opt);
        var cur = _vm!.FieldValues.TryGetValue(field.Name, out var cv) ? cv : field.Value;
        cb.SelectedItem = cur;
        if (cb.SelectedItem == null && field.Options.Count > 0) cb.SelectedIndex = 0;
        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is string val) _vm.UpdateFieldValue(field.Name, val); };
        cb.GotFocus += (_, _) => _vm.SelectedField = field;
        return cb;
    }

    // Acrobat: "Highlight Existing Fields" shades every field light blue; with it off, fields show
    // their own appearance (fill / border / text colours from Field Properties).
    private static Brush? HexBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        catch { return null; }
    }
    private static readonly HashSet<string> InstalledFonts =
        new(Fonts.SystemFontFamilies.Select(f => f.Source), StringComparer.OrdinalIgnoreCase);

    /// <summary>Shows a field's value in the PDF's own font (from /DA), like Acrobat does.</summary>
    private static void ApplyFieldFont(Control c, FormFieldInfo f)
    {
        var font = Services.PdfFontMap.Resolve(f.FontName);
        string family = InstalledFonts.Contains(font.Family) ? font.Family : "Arial";
        c.FontFamily = new FontFamily(family);
        c.FontWeight = font.Bold ? FontWeights.Bold : FontWeights.Normal;
        c.FontStyle = font.Italic ? FontStyles.Italic : FontStyles.Normal;
    }

    private Brush FieldFill(FormFieldInfo f) => _vm?.HighlightFields != false ? FieldFillBrush : HexBrush(f.FillColor) ?? Brushes.White;
    private Brush FieldBorder(FormFieldInfo f) => _vm?.HighlightFields != false ? FieldBorderBrush : HexBrush(f.BorderColor) ?? Brushes.Transparent;
    private static Brush FieldText(FormFieldInfo f) => HexBrush(f.TextColor) ?? Brushes.Black;

    private static TextAlignment ToTextAlignment(FieldAlignment a) => a switch
    {
        FieldAlignment.Center => TextAlignment.Center,
        FieldAlignment.Right  => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    private static HorizontalAlignment ToHorizontalAlignment(FieldAlignment a) => a switch
    {
        FieldAlignment.Center => HorizontalAlignment.Center,
        FieldAlignment.Right  => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left,
    };

    private ListBox BuildListBox(FormFieldInfo field, double w, double h)
    {
        var lb = new ListBox
        {
            Width = w, Height = h,
            FontSize = field.FontSize > 0 ? field.FontSize * Scale : Math.Max(8, h * 0.4),
            ToolTip = field.Name,
        };
        foreach (var opt in field.Options) lb.Items.Add(opt);
        var cur = _vm!.FieldValues.TryGetValue(field.Name, out var cv) ? cv : field.Value;
        lb.SelectedItem = cur;
        lb.SelectionChanged += (_, _) => { if (lb.SelectedItem is string val) _vm.UpdateFieldValue(field.Name, val); };
        lb.GotFocus += (_, _) => _vm.SelectedField = field;
        ApplyFieldFont(lb, field);
        return lb;
    }

    private Grid BuildSignatureBox(FormFieldInfo field, double w, double h)
    {
        // Adobe Acrobat signature field: light blue fill + dashed blue border + pen icon.
        var grid = new Grid
        {
            Width = w, Height = h,
            ToolTip = $"Signature field: {field.Name} — click to sign",
            Cursor = Cursors.Pen,
        };

        // Light-blue field fill
        grid.Children.Add(new Rectangle { Fill = FieldFill(field) });

        // Dashed border (WPF Border doesn't support dash; use Rectangle)
        grid.Children.Add(new Rectangle
        {
            Stroke = SigBlueBrush,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            Fill = Brushes.Transparent,
            Margin = new Thickness(1),
        });

        // Pen icon + "Click to Sign" label
        var label = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.Children.Add(new TextBlock
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Text = "",
            FontSize = Math.Max(8, h * 0.35),
            Foreground = SigBlueBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });
        label.Children.Add(new TextBlock
        {
            Text = "Click to Sign",
            Foreground = SigBlueBrush,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = Math.Max(8, h * 0.35),
            FontStyle = FontStyles.Italic,
        });
        grid.Children.Add(label);

        grid.MouseLeftButtonDown += (_, e) =>
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
        return grid;
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
        tb.Background = FieldFocusBrush;

        EnsureFieldChrome();

        // Show "Today" button for date-like fields
        if (_fieldChromeToday != null)
            _fieldChromeToday.Visibility = IsDateFieldName(field.Name)
                ? Visibility.Visible : Visibility.Collapsed;

        double barTop = Math.Max(0, y - ToolbarH - 1);
        Canvas.SetLeft(_fieldChromeBorder!, x);
        Canvas.SetTop(_fieldChromeBorder!, barTop);
        _fieldChromeBorder!.Visibility = Visibility.Visible;
    }

    private void HideFieldChrome(TextBox tb)
    {
        if (_fieldChromeBorder != null)
            _fieldChromeBorder.Visibility = Visibility.Collapsed;
        _activeTb = null;
        _activeFieldInfo = null;
    }

    /// <summary>
    /// Adobe-style mini toolbar shown above a focused text form field: decrease/increase font
    /// size, clear the value, rotate the displayed text, and toggle auto-size-to-fit — mirrors
    /// the same interaction the FreeText annotation toolbar already offers.
    /// </summary>
    private void EnsureFieldChrome()
    {
        if (_fieldChromeBorder != null) return;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        panel.Children.Add(MakeToolbarBtn("A", "Decrease font size", () => AdjustActiveFieldFontSize(-1), fontSize: 10));
        panel.Children.Add(MakeToolbarBtn("A", "Increase font size", () => AdjustActiveFieldFontSize(+1), fontSize: 14));
        panel.Children.Add(MakeToolbarBtn("🗑", "Clear field", ClearActiveField));
        panel.Children.Add(MakeToolbarBtn("↻", "Rotate text 90°", RotateActiveField));
        panel.Children.Add(MakeToolbarBtn("VA", "Auto-size text to fit the field", ToggleActiveFieldAutoSize, fontSize: 10));

        _fieldChromeToday = MakeToolbarBtn("Today", "Insert today's date", InsertTodayIntoActiveField, fontSize: 10);
        _fieldChromeToday.Visibility = Visibility.Collapsed;
        panel.Children.Add(_fieldChromeToday);

        _fieldChromeBorder = new Border
        {
            Child = panel,
            Background = new SolidColorBrush(Color.FromRgb(35, 35, 35)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Height = ToolbarH,
            IsHitTestVisible = true,
            Visibility = Visibility.Collapsed,
        };
        Panel.SetZIndex(_fieldChromeBorder, 9999);
        FieldOverlayCanvas.Children.Add(_fieldChromeBorder);
    }

    private void AdjustActiveFieldFontSize(double delta)
    {
        if (_activeTb == null || _activeFieldInfo == null) return;
        double newSize = Math.Clamp(_activeTb.FontSize + delta, 6, 72);
        _activeTb.FontSize = newSize;
        _fieldFontSizes[_activeFieldInfo.Name] = newSize;
        _fieldAutoSize[_activeFieldInfo.Name] = false; // manual size overrides auto-fit
    }

    private void ClearActiveField()
    {
        if (_activeTb == null || _activeFieldInfo == null) return;
        _activeTb.Text = string.Empty;
        _vm?.UpdateFieldValue(_activeFieldInfo.Name, string.Empty);
    }

    private void RotateActiveField()
    {
        if (_activeTb == null || _activeFieldInfo == null) return;
        int angle = (_fieldRotations.TryGetValue(_activeFieldInfo.Name, out var a) ? a : 0);
        angle = (angle + 90) % 360;
        _fieldRotations[_activeFieldInfo.Name] = angle;
        _activeTb.LayoutTransform = angle == 0 ? Transform.Identity : new RotateTransform(angle);
    }

    private void ToggleActiveFieldAutoSize()
    {
        if (_activeTb == null || _activeFieldInfo == null) return;
        bool next = !(_fieldAutoSize.TryGetValue(_activeFieldInfo.Name, out var cur) && cur);
        _fieldAutoSize[_activeFieldInfo.Name] = next;
        if (next) ApplyFieldAutoSize(_activeTb, _activeFieldInfo);
    }

    private void ApplyFieldAutoSize(TextBox tb, FormFieldInfo field)
    {
        double fit = Math.Clamp(tb.ActualHeight > 0 ? tb.ActualHeight * 0.6 : field.Height * Scale * 0.6, 6, 48);
        tb.FontSize = fit;
        _fieldFontSizes[field.Name] = fit;
    }

    private void InsertTodayIntoActiveField()
    {
        if (_activeTb == null || _activeFieldInfo == null) return;
        var dateStr = DateTime.Today.ToString("MM/dd/yyyy");
        _activeTb.Text = dateStr;
        _vm?.UpdateFieldValue(_activeFieldInfo.Name, dateStr);
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
        ResetAnnotationBoxes();
        // The canvas was cleared: drop any half-drawn polygon and the shape resize handle.
        _polyPreview = null; _polyLabel = null; _polyPts.Clear();
        _shapeResizeGrip = null;
        _replacePreview = null;
        _selectionOutline = null;

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
        // Freehand drawings: stored as __INK__:<color>|<width>:<pts> (see PdfViewerControl.Ink)
        if (ann.Text.StartsWith("__INK__:", StringComparison.Ordinal))
        {
            PlaceInkVisual(ann, pageHeightPts);
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

        bool isMark = IsDrawn(ann);
        // Adobe Fill & Sign style: text sits directly on the page (no fill, no box);
        // a thin outline only appears while the item is selected.
        var tb = new TextBox
        {
            Width = IsQuarterTurn(ann.RotationAngle) ? h : w,
            Height = IsQuarterTurn(ann.RotationAngle) ? w : h,
            Text = ann.ForceUpperCase ? ann.Text.ToUpperInvariant() : ann.Text,
            AcceptsReturn = !isMark,
            TextWrapping = isMark ? TextWrapping.NoWrap : TextWrapping.Wrap,
            Background = Brushes.Transparent,
            BorderBrush = ann.IsLocked
                ? new SolidColorBrush(Color.FromArgb(120, 200, 140, 0))   // amber = locked
                : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            IsReadOnly = ann.IsLocked || isMark,
            Cursor = isMark ? Cursors.Arrow : Cursors.IBeam,
            VerticalContentAlignment = isMark ? VerticalAlignment.Center : VerticalAlignment.Top,
            ToolTip = ann.IsLocked ? "Locked annotation — right-click to unlock" : null,
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
            // Adobe-style thin blue selection outline
            tb.BorderBrush = AdobeBlue;
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
                tb.BorderBrush = ann.IsLocked
                    ? new SolidColorBrush(Color.FromArgb(120, 200, 140, 0))
                    : Brushes.Transparent;
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
        RegisterAnnotationBox(ann, tb, isMark);
    }

    private void ApplyAnnotationFormatting(FreeTextAnnotation ann, TextBox tb)
    {
        // True page scale at every zoom, like the rendered page underneath
        double displayFontSize = ann.FontSize * Scale;
        tb.FontSize = Math.Max(1, displayFontSize);
        tb.FontFamily = new FontFamily(ann.FontFamily);
        tb.FontWeight = ann.IsBold ? FontWeights.Bold : FontWeights.Normal;
        tb.FontStyle = ann.IsItalic ? FontStyles.Italic : FontStyles.Normal;
        tb.TextDecorations = ann.IsUnderline ? TextDecorations.Underline : null;
        tb.Foreground = ParseBrush(ann.FontColor);
        tb.TextAlignment = ann.TextAlignment;

        // ✓ ✕ ● ○ — are drawn as shapes like Acrobat (not font glyphs): the box keeps the glyph
        // as its text (for select / drag / swap), hidden, and shows the drawing as its background.
        if (ann.IsStamp)
        {
            tb.Background = StampBrush(ann);
            tb.Foreground = Brushes.Transparent;
            tb.CaretBrush = Brushes.Transparent;
            tb.SelectionOpacity = 0;
        }
        else if (MarkShapes.FromGlyph(ann.Text) is { } kind)
        {
            tb.Background = MarkBrush(kind, ParseColor(ann.FontColor));
            tb.Foreground = Brushes.Transparent;
            tb.CaretBrush = Brushes.Transparent;
            tb.SelectionOpacity = 0;
        }
        else if (tb.Background is DrawingBrush)
        {
            tb.Background = Brushes.Transparent;
            tb.SelectionOpacity = 0.4;
        }
    }

    /// <summary>The vector drawing of a Fill &amp; Sign mark, scaled to whatever box it fills.</summary>
    private static Brush MarkBrush(MarkShapes.Kind kind, Color color)
    {
        var brush = new SolidColorBrush(color);
        var pen = new Pen(brush, MarkShapes.StrokeWidth(kind))
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
        };
        var group = new DrawingGroup();
        // Transparent frame so the unit square (not the ink) defines the drawing's bounds.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        foreach (var stroke in MarkShapes.Strokes(kind))
        {
            var fig = new PathFigure { StartPoint = stroke[0], IsClosed = false };
            fig.Segments.Add(new PolyLineSegment(stroke.Skip(1), isStroked: true));
            group.Children.Add(new GeometryDrawing(null, pen, new PathGeometry(new[] { fig })));
        }
        double r = MarkShapes.Radius(kind);
        if (r > 0)
        {
            var circle = new EllipseGeometry(new Point(0.5, 0.5), r, r);
            group.Children.Add(kind == MarkShapes.Kind.Dot
                ? new GeometryDrawing(brush, null, circle)
                : new GeometryDrawing(null, pen, circle));
        }
        group.Freeze();
        var db = new DrawingBrush(group)
        {
            Stretch = MarkShapes.FillsWidth(kind) ? Stretch.Fill : Stretch.Uniform,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };
        db.Freeze();
        return db;
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

    private void OnPageMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled)
        {
            // Only take it back when the click landed on the page itself (not on a field, a placed
            // annotation or a toolbar, which handle their own clicks).
            bool onBarePage = e.OriginalSource is DependencyObject src
                && (ReferenceEquals(src, PageImage) || ReferenceEquals(src, PageGrid) || ReferenceEquals(src, PageBorder)
                    || ReferenceEquals(src, AnnotationCanvas) || ReferenceEquals(src, FieldOverlayCanvas)
                    || ReferenceEquals(src, HlAnnotCanvas) || ReferenceEquals(src, RdAnnotCanvas));
            if (!onBarePage) return;
            e.Handled = false;
        }
        // Drawing tools already took the click in OnDrawToolPreviewMouseDown.
        if (_vm != null && IsDrawTool(_vm.ActiveTool) && !IsFieldLayoutMode) return;
        // Keyboard shortcuts (Delete, Esc, Enter for polygons, arrows) need the viewer focused.
        if (!IsKeyboardFocusWithin) Focus();
        OnMouseLeftButtonDown(sender, e);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null) return;

        var tool = _vm.ActiveTool;

        // Field boxes handle their own clicks in layout mode; a click that reaches here landed on
        // empty page space, so drop the current field selection (Add-field tools then start drawing).
        if (IsFieldLayoutMode)
        {
            Focus();
            if (tool == ActiveTool.EditFields)
            {
                // Drag on empty space = rubber-band selection (Ctrl/Shift adds to the selection).
                BeginLayoutRubberBand(e.GetPosition(FieldOverlayCanvas));
                e.Handled = true;
                return;
            }
            ClearLayoutSelection();
        }

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

        // A click on the bare page drops the shape / drawing selection.
        if (tool == ActiveTool.Select && _vm.SelectedGraphic != null)
        {
            _vm.SelectedGraphic = null;
            HideSelectionOutline();
        }

        // Select / Fill on a flat form (boxes drawn on the page, no fillable fields): clicking in
        // a box lets you type in it, like Acrobat. Clicks on real fields never reach here.
        if (tool is ActiveTool.Select or ActiveTool.TextFill or ActiveTool.CheckboxToggle)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (IsOnPage(posOnPage) && TryFillDrawnBox(posOnPage))
            {
                e.Handled = true;
                return;
            }
        }

        if (tool is ActiveTool.AddText or ActiveTool.VerticalText or ActiveTool.DateStamp)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;

            FinalizeAnnotationBox();
            // Inside a drawn box the text lines up with the box (Acrobat field detection).
            var snap = tool == ActiveTool.VerticalText ? null : DetectBoxAt(posOnPage);
            if (snap is { } filled && tool == ActiveTool.AddText && FocusExistingTextIn(filled))
            {
                e.Handled = true;
                return;
            }

            if (tool == ActiveTool.DateStamp)
            {
                string dateText = DateTime.Now.ToString(AppSettings.Current.DateFormat);
                PlaceNewAnnotationBox(posOnPage, false, dateText, snap);
            }
            else
            {
                PlaceNewAnnotationBox(posOnPage, tool == ActiveTool.VerticalText, null, snap);
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
            string glyph = tool switch
            {
                ActiveTool.Checkmark => "✓",
                ActiveTool.XMark => "✕",
                ActiveTool.Dot => "●",
                ActiveTool.Line => "—",
                _ => "○", // Circle
            };
            // Green tick, black cross (Acrobat defaults) unless a colour was picked in the toolbox.
            string colour = _vm.MarkColorFor(tool);
            var square = DetectBoxAt(posOnPage) is { } b && IsCheckBoxSized(b) ? b : (Rect?)null;
            PlaceStampAnnotation(posOnPage, glyph, colour, square);
            e.Handled = true;
            return;
        }

        if (tool == ActiveTool.Signature)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;
            PlaceSignatureAtPoint(posOnPage, DetectBoxAt(posOnPage));
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
                // Width is in points, like the saved drawing, so it looks the same at every zoom.
                StrokeThickness = Math.Max(0.5, (_vm?.CurrentStrokeWidth is > 0 ? _vm.CurrentStrokeWidth : 2.0) * Scale),
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

        // Acrobat text-edit comments
        if (tool == ActiveTool.InsertText)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;
            PlaceInsertTextMark(posOnPage);
            e.Handled = true;
            return;
        }
        if (tool == ActiveTool.ReplaceText)
        {
            var posOnPage = e.GetPosition(AnnotationCanvas);
            if (!IsOnPage(posOnPage)) return;
            BeginReplaceText(posOnPage);
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

        // Polygon / polyline / perimeter / area: click the points, double-click to finish.
        if (IsPolyTool(tool))
        {
            if (HandlePolyClick(e)) e.Handled = true;
            return;
        }

        if (tool is ActiveTool.DrawRectangle or ActiveTool.DrawEllipse or ActiveTool.DrawArrow or ActiveTool.DrawCallout
                 or ActiveTool.DrawLine or ActiveTool.DrawCloud or ActiveTool.MeasureDistance)
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
            if (tool is ActiveTool.DrawArrow or ActiveTool.DrawLine or ActiveTool.MeasureDistance)
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

        if (IsAddFieldTool(tool))
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
        if (EndLayoutRubberBand(e.GetPosition(FieldOverlayCanvas)))
        {
            e.Handled = true;
            return;
        }

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
                    int pageNum = _vm.CurrentPageIndex + 1;
                    if (pageNum >= 1 && pageNum <= _vm.Document.PageSizes.Count)
                    {
                        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;
                        _ = _vm.AddLinkInteractiveAsync(canvasX / Scale, pageH - (canvasY / Scale) - (rectH / Scale),
                            rectW / Scale, rectH / Scale);
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

            if (_freehandPolyline != null)
            {
                // Replace the live preview with the saved, smoothed, undoable drawing (a click = a dot).
                var pts = _freehandPolyline.Points.ToList();
                AnnotationCanvas.Children.Remove(_freehandPolyline);
                FinishFreehandStroke(pts);
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

                // Acrobat: a click places a default-size field, a drag sets the size.
                CreateFormField(_formFieldTool, new Rect(canvasX, canvasY, rectW, rectH));
            }
            e.Handled = true;
        }

        if (EndReplaceText(e.GetPosition(AnnotationCanvas)))
        {
            e.Handled = true;
            return;
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
                                Kind        = _shapeTool switch
                                {
                                    ActiveTool.DrawLine        => Models.ShapeKind.Line,
                                    ActiveTool.MeasureDistance => Models.ShapeKind.Distance,
                                    _                          => Models.ShapeKind.Arrow,
                                },
                                MeasureUnit = _vm.MeasureUnit,
                                X1          = _arrowRubberBand.X1 / Scale,
                                Y1          = pageH - _arrowRubberBand.Y1 / Scale,
                                X2          = _arrowRubberBand.X2 / Scale,
                                Y2          = pageH - _arrowRubberBand.Y2 / Scale,
                                StrokeColor = colorHex,
                                LineWidth   = strokeW,
                            };
                            _vm.AddShapeAnnotation(shape);
                            PlaceShapeVisual(shape, pageH);
                            _vm.StatusText = shape.Kind == Models.ShapeKind.Distance
                                ? $"Distance: {MeasureLabel(shape)}"
                                : $"{shape.Kind} added — drag with Select to move, right-click for properties.";
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
                                    Kind        = isEllipse ? Models.ShapeKind.Ellipse
                                                : _shapeTool == ActiveTool.DrawCloud ? Models.ShapeKind.Cloud
                                                : Models.ShapeKind.Rectangle,
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
        if (e.LeftButton == MouseButtonState.Pressed && UpdateLayoutRubberBand(e.GetPosition(FieldOverlayCanvas)))
            return;

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
            // Skip sub-pixel jitter so the smoothed stroke stays clean.
            var last = _freehandPolyline.Points[^1];
            if ((pos - last).Length >= 1.5) _freehandPolyline.Points.Add(pos);
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

        if (_polyPreview != null)
            UpdatePolyPreview(e.GetPosition(AnnotationCanvas));
        if (e.LeftButton == MouseButtonState.Pressed && UpdateReplaceText(e.GetPosition(AnnotationCanvas)))
            return;

        if (_isDrawingShape && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(AnnotationCanvas);
            if (_arrowRubberBand != null)
            {
                _arrowRubberBand.X2 = pos.X;
                _arrowRubberBand.Y2 = pos.Y;
                if (_shapeTool == ActiveTool.MeasureDistance && _vm != null)
                    _vm.StatusText = "Distance: " + FormatLength(
                        new Vector(_arrowRubberBand.X2 - _arrowRubberBand.X1, _arrowRubberBand.Y2 - _arrowRubberBand.Y1).Length / Scale,
                        _vm.MeasureUnit);
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

    /// <summary>
    /// True when keyboard focus is on something that takes typed characters (text/password boxes,
    /// drop-downs and list boxes that select by typing). Single-letter shortcuts must not fire then.
    /// </summary>
    public static bool IsTextInputFocused() => Keyboard.FocusedElement is TextBoxBase or PasswordBox
        or ComboBox or ComboBoxItem or ListBox or ListBoxItem;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Enter / Esc / Backspace while placing polygon or measurement points
        if (HandlePolyKey(e)) { e.Handled = true; return; }
        // Don't steal shortcuts when a TextBox / field has focus
        bool textboxFocused = IsTextInputFocused();

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
                // The scroll viewer no longer takes focus, so scroll it from here.
                else if (e.Key == Key.Down) { PdfScrollViewer.LineDown(); e.Handled = true; }
                else { PdfScrollViewer.LineRight(); e.Handled = true; }
                break;
            case Key.Left:
            case Key.Up:
                if (_vm.ActiveTool == ActiveTool.Hand) { _vm.PreviousPageCommand.Execute(null); e.Handled = true; }
                else if (e.Key == Key.Up) { PdfScrollViewer.LineUp(); e.Handled = true; }
                else { PdfScrollViewer.LineLeft(); e.Handled = true; }
                break;
            case Key.PageDown: PdfScrollViewer.PageDown(); e.Handled = true; break;
            case Key.PageUp:   PdfScrollViewer.PageUp();   e.Handled = true; break;
            case Key.Home:     PdfScrollViewer.ScrollToTop();    e.Handled = true; break;
            case Key.End:      PdfScrollViewer.ScrollToBottom(); e.Handled = true; break;
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

    private void PlaceStampAnnotation(Point posOnCanvas, string stampText, string colorHex, Rect? snapBox = null)
    {
        if (_vm?.Document == null) return;

        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageHeightPts = _vm.Document.PageSizes[pageNum - 1].Height;

        double fontSize = _vm.CurrentFontSize * 2;
        double defaultW = 32 * Scale;
        double defaultH = 32 * Scale;

        // The mark is centred on the click (it used to hang down-right of the pointer).
        double pdfW = defaultW / Scale;
        double pdfH = defaultH / Scale;
        double pdfX = posOnCanvas.X / Scale - pdfW / 2;
        double pdfY = pageHeightPts - (posOnCanvas.Y / Scale) - pdfH / 2;

        // Clicked in a drawn checkbox (flat form): the mark fills that square, centred.
        if (snapBox is { } box)
        {
            pdfX = box.X / Scale;
            pdfW = box.Width / Scale;
            pdfH = box.Height / Scale;
            pdfY = pageHeightPts - box.Y / Scale - pdfH;
            fontSize = Math.Max(6, pdfH * 0.8);
        }

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

        // Same visual path as saved annotations, so the mark gets the selection
        // outline, floating toolbar (size / delete / swap) and drag handling at once.
        PlaceAnnotationVisual(ann, pageHeightPts);
        AnnotationCanvas.Children.OfType<TextBox>().LastOrDefault()?.Focus();

        var capturedAnn = ann;
        _vm.PushUndo(
            undo: () => { _vm.FreeTextAnnotations.Remove(capturedAnn); RefreshPage(); },
            redo: () => { _vm.FreeTextAnnotations.Add(capturedAnn);    RefreshPage(); });

        _vm.StatusText = $"Mark '{stampText}' placed — use the toolbar to resize, swap or delete.";
    }

    // ── Free-text TextBox placement ───────────────────────────────────────────

    /// <summary>
    /// Add Text (Acrobat Fill &amp; Sign): creates the annotation straight away and focuses it, so the
    /// mini toolbar is available while typing and the text can be re-selected, dragged and resized
    /// immediately. An annotation left empty is removed again when it loses focus.
    /// </summary>
    private void PlaceNewAnnotationBox(Point posOnCanvas, bool vertical, string? prefilledText, Rect? snapBox = null)
    {
        if (_vm?.Document == null) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageHeightPts = _vm.Document.PageSizes[pageNum - 1].Height;

        // Acrobat places the text baseline roughly at the click; start with a one-line box.
        double fontSize = _vm.CurrentFontSize;
        double lineH = Math.Max(10, fontSize * 1.4);
        double wPt = vertical ? lineH : 60;
        double hPt = vertical ? 100 : lineH;
        double leftPt = posOnCanvas.X / Scale;
        double topPt = posOnCanvas.Y / Scale - (vertical ? 0 : lineH / 2);

        // Clicked inside a drawn box (flat form, like Acrobat's detected fields): line the text
        // up at the box's left edge, centred vertically, shrinking the font to fit a short box.
        if (snapBox is { } box && !vertical)
        {
            double boxTop = box.Y / Scale, boxH = box.Height / Scale;
            fontSize = Math.Max(6, Math.Min(fontSize, boxH * 0.7));
            lineH = Math.Min(boxH, fontSize * 1.4);
            hPt = lineH;
            leftPt = box.X / Scale + 2;
            topPt = boxTop + (boxH - lineH) / 2;
        }

        var ann = new FreeTextAnnotation
        {
            PageNumber = pageNum,
            Left = leftPt,
            Bottom = pageHeightPts - topPt - hPt,
            Width = wPt,
            Height = hPt,
            Text = prefilledText ?? string.Empty,
            RotationAngle = vertical ? -90.0 : 0.0,
            FontSize = fontSize,
            FontFamily = _vm.CurrentFontFamily,
            IsBold = _vm.CurrentFontBold,
            IsItalic = _vm.CurrentFontItalic,
            IsUnderline = _vm.CurrentFontUnderline,
            FontColor = _vm.CurrentFontColor,
            TextAlignment = _vm.CurrentTextAlignment,
            ForceUpperCase = _vm.ForceUpperCase,
            AutoSize = !vertical,
        };

        _vm.FreeTextAnnotations.Add(ann);
        PlaceAnnotationVisual(ann, pageHeightPts);
        if (!_annotationBoxes.TryGetValue(ann, out var tb)) return;

        var vm = _vm;
        vm.PushUndo(
            undo: () => { vm.FreeTextAnnotations.Remove(ann); RefreshPage(); },
            redo: () => { vm.FreeTextAnnotations.Add(ann);    RefreshPage(); });

        // Empty text is discarded when the box loses focus (clicking elsewhere), like Acrobat.
        void RemoveIfEmpty(object? sender, RoutedEventArgs e)
        {
            tb.LostFocus -= RemoveIfEmpty;
            if (!string.IsNullOrWhiteSpace(ann.Text)) return;
            vm.FreeTextAnnotations.Remove(ann);
            AnnotationCanvas.Children.Remove(tb);
            _annotationBoxes.Remove(ann);
            if (ReferenceEquals(vm.SelectedAnnotation, ann)) vm.SelectedAnnotation = null;
            HideAnnotationToolbar();
        }
        tb.LostFocus += RemoveIfEmpty;

        tb.Focus();
        Keyboard.Focus(tb);
        if (!string.IsNullOrEmpty(prefilledText)) tb.SelectAll();

        vm.StatusText = vertical
            ? "Vertical text: type, then click away. Use the toolbar above the text to resize, rotate or delete."
            : prefilledText != null ? $"Date stamp: '{prefilledText}' — edit it or click away."
            : "Add text: type, then click away. Use the toolbar above the text to change size, rotate, space or delete.";
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

    private void PlaceSignatureAtPoint(Point posOnCanvas, Rect? snapBox = null)
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

        // Clicked in a drawn signature box: fit the signature inside it, keeping its shape.
        if (snapBox is { } box && !IsCheckBoxSized(box))
        {
            double aspect = 200.0 / 60.0;
            try
            {
                using var ms = new System.IO.MemoryStream(bytes);
                var frame = System.Windows.Media.Imaging.BitmapFrame.Create(ms,
                    System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                    System.Windows.Media.Imaging.BitmapCacheOption.None);
                if (frame.PixelHeight > 0) aspect = (double)frame.PixelWidth / frame.PixelHeight;
            }
            catch { }
            double bw = box.Width / Scale, bh = box.Height / Scale;
            pdfW = bw; pdfH = bw / aspect;
            if (pdfH > bh) { pdfH = bh; pdfW = bh * aspect; }
            pdfX = box.X / Scale + (bw - pdfW) / 2;
            pdfY = pageH - (box.Y / Scale + (bh + pdfH) / 2);
        }

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
        if (TryPlaceExtendedShape(shape, pageH)) return;
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
                StrokeThickness = Math.Max(0.5, shape.LineWidth * Scale),
                StrokeEndLineCap = PenLineCap.Triangle,
            };
            line.Tag = shape;
            line.ToolTip = "Arrow annotation (right-click to delete)";
            line.ToolTip = "Arrow — drag with Select to move, right-click for properties";
            line.Opacity = shape.Opacity;
            line.ContextMenu = BuildShapeMenu(shape, line);
            MakeDraggable(line, (dx, dy) => MoveShape(shape, dx, dy), shape);
            visual = line;
        }
        else
        {
            double left   = shape.X1 * Scale;
            double top    = (pageH - shape.Y2) * Scale;
            double width  = (shape.X2 - shape.X1) * Scale;
            double height = (shape.Y2 - shape.Y1) * Scale;
            System.Windows.Shapes.Shape sh = shape.Kind == Models.ShapeKind.Ellipse
                ? new System.Windows.Shapes.Ellipse { Width = width, Height = height, Fill = fillBrush, Stroke = strokeBrush, StrokeThickness = Math.Max(0.5, shape.LineWidth * Scale) }
                : new Rectangle { Width = width, Height = height, Fill = fillBrush, Stroke = strokeBrush, StrokeThickness = Math.Max(0.5, shape.LineWidth * Scale) };
            Canvas.SetLeft(sh, left);
            Canvas.SetTop(sh,  top);
            sh.Tag = shape;
            sh.ToolTip = $"{shape.Kind} annotation (right-click to delete)";
            sh.ToolTip = $"{shape.Kind} — drag with Select to move, corner to resize, right-click for properties";
            sh.Opacity = shape.Opacity;
            sh.ContextMenu = BuildShapeMenu(shape, sh);
            AttachShapeResize(shape, sh, pageH);
            MakeDraggable(sh, (dx, dy) => MoveShape(shape, dx, dy), shape);
            visual = sh;
        }

        AnnotationCanvas.Children.Add(visual);
        if (ReferenceEquals(_vm?.SelectedGraphic, shape) && visual is FrameworkElement selFe)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => ShowSelectionOutline(selFe));
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
            StrokeThickness = Math.Max(0.5, shape.LineWidth * Scale),
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
            StrokeThickness = Math.Max(0.5, shape.LineWidth * Scale),
        };
        cv.Children.Add(pointer);

        var ctx = BuildShapeMenu(shape, cv);
        cv.ContextMenu = ctx;
        cv.Opacity = shape.Opacity;
        cv.MouseRightButtonDown += (s2, e2) => { ctx.IsOpen = true; e2.Handled = true; };
        AttachShapeResize(shape, cv, pageH);
        cv.Background = Brushes.Transparent;   // grab anywhere in the callout's box
        MakeDraggable(cv, (dx, dy) => MoveShape(shape, dx, dy), shape);

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
                    else if (sh.Tag is FreeTextAnnotation ink)
                    {
                        toRemoveUi.Add(sh);
                        _vm.FreeTextAnnotations.Remove(ink);   // drawings used to come back after a refresh
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
