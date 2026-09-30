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
/// Acrobat "Prepare Form"-style field layout editing for the live view.
///
/// While the Edit Fields tool (or one of the Add Form Field tools) is active, form fields are drawn as
/// named placeholder boxes instead of live fill-in controls. Click a field to select it — a blue
/// frame with eight resize handles appears — drag the body to move it, drag a handle to resize it
/// (Shift on a corner keeps the aspect ratio), use the arrow keys to nudge (Shift = 10 pt,
/// Ctrl = resize) and Delete to remove it. Ctrl/Shift+click or drag a rubber band on empty page
/// space to select several fields; they then move, nudge, delete and align (right-click → Align,
/// or the ribbon's Arrange group) together. Changes go through <c>MainViewModel</c>, are undoable,
/// and are written to the PDF on save.
/// </summary>
public partial class PdfViewerControl
{
    private const double LayoutHandleSize = 8;
    private const double LayoutMinSize = 6;

    private static readonly Brush LayoutBoxFill        = Freeze(Color.FromArgb(55, 0, 110, 230));
    private static readonly Brush LayoutBoxBorder      = Freeze(Color.FromArgb(200, 0, 90, 200));
    private static readonly Brush LayoutBoxText        = Freeze(Color.FromRgb(0, 55, 140));
    private static readonly Brush LayoutSelectionBrush = Freeze(Color.FromRgb(0, 120, 215));

    private bool _builtInLayoutMode;

    // Selected fields, tracked by name + widget index so the selection survives an overlay rebuild
    // (undo, property edits, a newly-added field reloading the document). The last key is the
    // primary selection: it carries the resize handles and is the reference for alignment.
    private readonly List<(string Name, int WidgetIndex)> _layoutSelectedKeys = new();
    private readonly Dictionary<(string Name, int WidgetIndex), Border> _layoutBoxes = new();
    private (string Name, int WidgetIndex)? _layoutSelectedKey
    {
        get => _layoutSelectedKeys.Count > 0 ? _layoutSelectedKeys[^1] : null;
        set { _layoutSelectedKeys.Clear(); if (value is { } k) _layoutSelectedKeys.Add(k); }
    }
    private FormFieldInfo? _layoutSelectedField;
    private Border? _layoutSelectedBox;

    // Group move state: each selected box's position when the drag started
    private readonly Dictionary<Border, Point> _layoutGroupOrigins = new();

    // Rubber-band selection
    private Rectangle? _layoutBand;
    private Point _layoutBandStart;
    private bool _layoutBandAdditive;

    // Selection chrome (recreated whenever FieldOverlayCanvas is cleared)
    private Rectangle? _layoutFrame;
    private readonly Thumb?[] _layoutHandles = new Thumb?[8];

    // Move / resize gesture state
    private bool _layoutMoving;
    private bool _layoutMoved;
    private bool _layoutResizing;
    private Point _layoutMouseStart;
    private Rect _layoutOrigin;

    /// <summary>True when fields should be edited (moved/resized) rather than filled in.</summary>
    private bool IsFieldLayoutMode => _vm?.ActiveTool is ActiveTool.EditFields
        or ActiveTool.AddTextField or ActiveTool.AddCheckbox
        or ActiveTool.AddComboBox or ActiveTool.AddRadioButton
        or ActiveTool.AddListBox or ActiveTool.AddSignatureField or ActiveTool.AddDateField;

    private static bool IsAddFieldTool(ActiveTool t) => t is ActiveTool.AddTextField or ActiveTool.AddCheckbox
        or ActiveTool.AddComboBox or ActiveTool.AddRadioButton or ActiveTool.AddListBox
        or ActiveTool.AddSignatureField or ActiveTool.AddDateField;

    /// <summary>Switches to the Select tool and selects <paramref name="field"/> for moving/resizing.</summary>
    private void BeginFieldLayoutEdit(FormFieldInfo field)
    {
        if (_vm == null) return;
        _layoutSelectedKey = (field.Name, field.WidgetIndex);
        _vm.ActiveTool = ActiveTool.EditFields; // triggers an overlay rebuild that restores the selection
        if (!_builtInLayoutMode) RebuildFieldOverlay();
    }

    /// <summary>Re-applies the field overlay (e.g. when switching between fill and layout mode).</summary>
    private void RebuildFieldOverlay()
    {
        if (_vm?.Document == null || PageImage.Source == null) return;
        BuildFieldOverlay(_vm.CurrentPageFields, _vm.HighlightFields);
    }

    // ── Placeholder boxes ────────────────────────────────────────────────────

    private void AddLayoutFieldBox(FormFieldInfo field, double x, double y, double w, double h)
    {
        string glyph = field.FieldType switch
        {
            FieldType.Checkbox    => "☑ ",
            FieldType.RadioButton => "◉ ",
            FieldType.ComboBox    => "▾ ",
            FieldType.ListBox     => "☰ ",
            FieldType.Signature   => "✍ ",
            _                     => field.IsDateField ? "📅 " : string.Empty,
        };

        var label = new TextBlock
        {
            Text = glyph + field.DisplayName,
            Foreground = LayoutBoxText,
            FontSize = Math.Clamp(h * 0.5, 7, 12),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 3, 0),
            IsHitTestVisible = false,
        };

        var box = new Border
        {
            Width = Math.Max(1, w),
            Height = Math.Max(1, h),
            Background = LayoutBoxFill,
            BorderBrush = LayoutBoxBorder,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.SizeAll,
            Tag = field,
            Child = label,
            ClipToBounds = true,
            ToolTip = $"{field.DisplayName} ({field.FieldType}) — drag to move, drag the handles to resize, Ctrl+click to multi-select",
        };
        System.Windows.Automation.AutomationProperties.SetName(box, $"Form field layout: {field.Name}");

        box.MouseLeftButtonDown += OnLayoutBoxMouseDown;
        box.MouseMove += OnLayoutBoxMouseMove;
        box.MouseLeftButtonUp += OnLayoutBoxMouseUp;
        box.MouseRightButtonDown += (_, e) =>
        {
            // Right-clicking inside the current multi-selection keeps it (so Align acts on the group).
            if (!_layoutSelectedKeys.Contains(KeyOf(field))) SelectOnly(box);
            e.Handled = false;
        };
        box.ContextMenu = BuildLayoutContextMenu();

        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);
        FieldOverlayCanvas.Children.Add(box);
        _layoutBoxes[KeyOf(field)] = box;

        // Restore the selection after a rebuild.
        var key = KeyOf(field);
        if (_layoutSelectedKeys.Contains(key))
        {
            StyleSelected(box, true);
            if (_layoutSelectedKeys[^1] == key) MakePrimary(box);
            SyncSelectionToVm();
        }
        MaybeShowNamePopup(box, field);
    }

    private static (string Name, int WidgetIndex) KeyOf(FormFieldInfo f) => (f.Name, f.WidgetIndex);

    private ContextMenu BuildLayoutContextMenu()
    {
        var menu = new ContextMenu();

        MenuItem Op(string header, ArrangeOperation op, string? gesture = null)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty };
            mi.Click += (_, _) => _vm?.ArrangeFields(op);
            return mi;
        }

        var align = new MenuItem { Header = "Align" };
        align.Items.Add(Op("Lefts", ArrangeOperation.AlignLefts));
        align.Items.Add(Op("Centres", ArrangeOperation.AlignCenters));
        align.Items.Add(Op("Rights", ArrangeOperation.AlignRights));
        align.Items.Add(new Separator());
        align.Items.Add(Op("Tops", ArrangeOperation.AlignTops));
        align.Items.Add(Op("Middles", ArrangeOperation.AlignMiddles));
        align.Items.Add(Op("Bottoms", ArrangeOperation.AlignBottoms));

        var distribute = new MenuItem { Header = "Distribute" };
        distribute.Items.Add(Op("Horizontally", ArrangeOperation.DistributeHorizontally));
        distribute.Items.Add(Op("Vertically", ArrangeOperation.DistributeVertically));

        var size = new MenuItem { Header = "Make Same Size" };
        size.Items.Add(Op("Width", ArrangeOperation.MakeSameWidth));
        size.Items.Add(Op("Height", ArrangeOperation.MakeSameHeight));
        size.Items.Add(Op("Both", ArrangeOperation.MakeSameSize));

        var center = new MenuItem { Header = "Center on Page" };
        center.Items.Add(Op("Horizontally", ArrangeOperation.CenterOnPageHorizontally));
        center.Items.Add(Op("Vertically", ArrangeOperation.CenterOnPageVertically));

        var props = new MenuItem { Header = "Properties…", FontWeight = FontWeights.SemiBold };
        props.Click += (_, _) => _vm?.OpenFieldProperties(_layoutSelectedField);
        var del = new MenuItem { Header = "Delete", InputGestureText = "Del" };
        del.Click += (_, _) => DeleteLayoutSelection();
        var selectAll = new MenuItem { Header = "Select All Fields on Page", InputGestureText = "Ctrl+A" };
        selectAll.Click += (_, _) => SelectAllLayoutBoxes();
        var fill = new MenuItem { Header = "Switch to fill mode (Hand tool)" };
        fill.Click += (_, _) => { if (_vm != null) _vm.ActiveTool = ActiveTool.Hand; };

        menu.Items.Add(props);
        menu.Items.Add(new Separator());
        menu.Items.Add(align);
        menu.Items.Add(distribute);
        menu.Items.Add(size);
        menu.Items.Add(center);
        menu.Items.Add(new Separator());
        menu.Items.Add(selectAll);
        menu.Items.Add(del);
        menu.Items.Add(new Separator());
        menu.Items.Add(fill);
        return menu;
    }

    // ── Selection ────────────────────────────────────────────────────────────

    /// <summary>Selects just this box (plain click).</summary>
    private void SelectOnly(Border box)
    {
        if (box.Tag is not FormFieldInfo field) return;
        foreach (var k in _layoutSelectedKeys)
            if (_layoutBoxes.TryGetValue(k, out var b)) StyleSelected(b, false);
        _layoutSelectedKeys.Clear();
        _layoutSelectedKeys.Add(KeyOf(field));
        StyleSelected(box, true);
        MakePrimary(box);
        SyncSelectionToVm();
    }

    // Kept for callers that select a single field.
    private void SelectLayoutBox(Border box) => SelectOnly(box);

    /// <summary>Ctrl/Shift+click: add to or remove from the selection.</summary>
    private void ToggleSelected(Border box)
    {
        if (box.Tag is not FormFieldInfo field) return;
        var key = KeyOf(field);
        if (_layoutSelectedKeys.Remove(key))
        {
            StyleSelected(box, false);
            if (_layoutSelectedBox == box)
            {
                HideLayoutChrome();
                _layoutSelectedBox = null;
                _layoutSelectedField = null;
                if (_layoutSelectedKeys.Count > 0 && _layoutBoxes.TryGetValue(_layoutSelectedKeys[^1], out var next))
                    MakePrimary(next);
            }
        }
        else
        {
            _layoutSelectedKeys.Add(key);
            StyleSelected(box, true);
            MakePrimary(box);
        }
        SyncSelectionToVm();
    }

    private void SelectAllLayoutBoxes()
    {
        _layoutSelectedKeys.Clear();
        foreach (var (key, box) in _layoutBoxes)
        {
            _layoutSelectedKeys.Add(key);
            StyleSelected(box, true);
        }
        if (_layoutSelectedKeys.Count > 0) MakePrimary(_layoutBoxes[_layoutSelectedKeys[^1]]);
        SyncSelectionToVm();
    }

    private void MakePrimary(Border box)
    {
        _layoutSelectedBox = box;
        _layoutSelectedField = box.Tag as FormFieldInfo;
        Panel.SetZIndex(box, 5000);
        EnsureLayoutChrome();
        PositionLayoutChrome();
    }

    private static void StyleSelected(Border box, bool selected)
    {
        box.BorderBrush = selected ? LayoutSelectionBrush : LayoutBoxBorder;
        box.BorderThickness = new Thickness(selected ? 2 : 1);
        if (!selected) Panel.SetZIndex(box, 0);
    }

    /// <summary>Publishes the selection so the Properties panel and the Arrange commands see it.</summary>
    private void SyncSelectionToVm()
    {
        if (_vm == null) return;
        _vm.SelectedLayoutFields.Clear();
        foreach (var k in _layoutSelectedKeys)
            if (_layoutBoxes.TryGetValue(k, out var b) && b.Tag is FormFieldInfo f)
                _vm.SelectedLayoutFields.Add(f);
        if (_layoutSelectedField != null) _vm.SelectedField = _layoutSelectedField;
        if (_layoutSelectedKeys.Count > 1)
            _vm.StatusText = $"{_layoutSelectedKeys.Count} fields selected — right-click → Align, or use Forms → Arrange.";
    }

    private IEnumerable<Border> SelectedBoxes() =>
        _layoutSelectedKeys.Where(_layoutBoxes.ContainsKey).Select(k => _layoutBoxes[k]);

    private void HideLayoutChrome()
    {
        if (_layoutFrame != null) _layoutFrame.Visibility = Visibility.Collapsed;
        foreach (var t in _layoutHandles)
            if (t != null) t.Visibility = Visibility.Collapsed;
    }

    private void ClearLayoutSelection()
    {
        foreach (var b in SelectedBoxes().ToList()) StyleSelected(b, false);
        _layoutSelectedKeys.Clear();
        _layoutSelectedBox = null;
        _layoutSelectedField = null;
        _layoutMoving = _layoutResizing = false;
        HideLayoutChrome();
        _vm?.SelectedLayoutFields.Clear();
    }

    /// <summary>Called at the start of BuildFieldOverlay — the canvas is about to be cleared.</summary>
    private void ResetLayoutChrome()
    {
        _layoutSelectedBox = null;
        _layoutSelectedField = null;
        _layoutFrame = null;
        _layoutBand = null;
        _layoutMoving = _layoutResizing = false;
        _layoutBoxes.Clear();
        _layoutGroupOrigins.Clear();
        for (int i = 0; i < _layoutHandles.Length; i++) _layoutHandles[i] = null;
    }

    private void DeleteLayoutSelection()
    {
        if (_vm == null) return;
        var fields = SelectedBoxes().Select(b => b.Tag).OfType<FormFieldInfo>().ToList();
        if (fields.Count == 0) return;
        ClearLayoutSelection();
        foreach (var f in fields) _vm.DeleteField(f);
    }

    private void EnsureLayoutChrome()
    {
        if (_layoutFrame == null || !FieldOverlayCanvas.Children.Contains(_layoutFrame))
        {
            _layoutFrame = new Rectangle
            {
                Stroke = LayoutSelectionBrush,
                StrokeThickness = 1.5,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            Panel.SetZIndex(_layoutFrame, 9000);
            FieldOverlayCanvas.Children.Add(_layoutFrame);
        }

        for (int i = 0; i < 8; i++)
        {
            if (_layoutHandles[i] != null && FieldOverlayCanvas.Children.Contains(_layoutHandles[i])) continue;

            var factory = new FrameworkElementFactory(typeof(Rectangle));
            factory.SetValue(Shape.FillProperty, Brushes.White);
            factory.SetValue(Shape.StrokeProperty, LayoutSelectionBrush);
            factory.SetValue(Shape.StrokeThicknessProperty, 1.25);

            var thumb = new Thumb
            {
                Width = LayoutHandleSize,
                Height = LayoutHandleSize,
                Tag = i,
                Cursor = i switch
                {
                    0 or 7 => Cursors.SizeNWSE,
                    2 or 5 => Cursors.SizeNESW,
                    1 or 6 => Cursors.SizeNS,
                    _      => Cursors.SizeWE,
                },
                Template = new ControlTemplate(typeof(Thumb)) { VisualTree = factory },
                ToolTip = "Drag to resize (Shift keeps proportions)",
            };
            thumb.DragStarted += OnLayoutHandleDragStarted;
            thumb.DragDelta += OnLayoutHandleDragDelta;
            thumb.DragCompleted += OnLayoutHandleDragCompleted;
            Panel.SetZIndex(thumb, 9001);
            FieldOverlayCanvas.Children.Add(thumb);
            _layoutHandles[i] = thumb;
        }
    }

    private void PositionLayoutChrome()
    {
        if (_layoutSelectedBox == null || _layoutFrame == null) return;

        double l = Canvas.GetLeft(_layoutSelectedBox);
        double t = Canvas.GetTop(_layoutSelectedBox);
        double w = _layoutSelectedBox.Width;
        double h = _layoutSelectedBox.Height;

        _layoutFrame.Width = w + 2;
        _layoutFrame.Height = h + 2;
        Canvas.SetLeft(_layoutFrame, l - 1);
        Canvas.SetTop(_layoutFrame, t - 1);
        _layoutFrame.Visibility = Visibility.Visible;

        double hh = LayoutHandleSize / 2;
        // 0=NW 1=N 2=NE 3=W 4=E 5=SW 6=S 7=SE
        Point[] pos =
        {
            new(l - hh,         t - hh),
            new(l + w / 2 - hh, t - hh),
            new(l + w - hh,     t - hh),
            new(l - hh,         t + h / 2 - hh),
            new(l + w - hh,     t + h / 2 - hh),
            new(l - hh,         t + h - hh),
            new(l + w / 2 - hh, t + h - hh),
            new(l + w - hh,     t + h - hh),
        };
        for (int i = 0; i < 8; i++)
        {
            var thumb = _layoutHandles[i];
            if (thumb == null) continue;
            Canvas.SetLeft(thumb, pos[i].X);
            Canvas.SetTop(thumb, pos[i].Y);
            thumb.Visibility = Visibility.Visible;
        }
    }

    // ── Move (drag the box body) ─────────────────────────────────────────────

    private void OnLayoutBoxMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border box || box.Tag is not FormFieldInfo field) return;
        Focus();

        // Double-click: Acrobat's Field Properties dialog.
        if (e.ClickCount == 2)
        {
            SelectOnly(box);
            _vm?.OpenFieldProperties(field);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ToggleSelected(box);
            e.Handled = true;
            return;
        }

        // Clicking a field that is already part of the selection keeps the group (to drag it).
        if (_layoutSelectedKeys.Contains(KeyOf(field))) MakePrimary(box);
        else SelectOnly(box);
        SyncSelectionToVm();

        _layoutMoving = true;
        _layoutMoved = false;
        _layoutMouseStart = e.GetPosition(FieldOverlayCanvas);
        _layoutGroupOrigins.Clear();
        foreach (var b in SelectedBoxes())
            _layoutGroupOrigins[b] = new Point(Canvas.GetLeft(b), Canvas.GetTop(b));
        box.CaptureMouse();
        e.Handled = true;
    }

    private void OnLayoutBoxMouseMove(object sender, MouseEventArgs e)
    {
        if (!_layoutMoving || sender is not Border box || box != _layoutSelectedBox) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndLayoutMove(box); return; }

        var pos = e.GetPosition(FieldOverlayCanvas);
        double dx = pos.X - _layoutMouseStart.X;
        double dy = pos.Y - _layoutMouseStart.Y;

        // Small dead-zone so a plain click to select never nudges the field.
        if (!_layoutMoved && Math.Abs(dx) < 3 && Math.Abs(dy) < 3) return;
        _layoutMoved = true;

        // Clamp the delta so the whole group stays on the page.
        var (maxW, maxH) = LayoutCanvasSize();
        foreach (var (b, o) in _layoutGroupOrigins)
        {
            dx = Math.Clamp(dx, -o.X, Math.Max(-o.X, maxW - b.Width - o.X));
            dy = Math.Clamp(dy, -o.Y, Math.Max(-o.Y, maxH - b.Height - o.Y));
        }
        foreach (var (b, o) in _layoutGroupOrigins)
        {
            Canvas.SetLeft(b, o.X + dx);
            Canvas.SetTop(b, o.Y + dy);
        }
        PositionLayoutChrome();
        e.Handled = true;
    }

    private void OnLayoutBoxMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_layoutMoving || sender is not Border box) return;
        EndLayoutMove(box);
        e.Handled = true;
    }

    private void EndLayoutMove(Border box)
    {
        _layoutMoving = false;
        box.ReleaseMouseCapture();
        if (_layoutMoved) CommitLayoutBoxes(_layoutGroupOrigins.Keys.ToList(), "Moved");
        _layoutMoved = false;
        _layoutGroupOrigins.Clear();
    }

    // ── Rubber-band selection (drag on empty page space) ─────────────────────

    private void BeginLayoutRubberBand(Point start)
    {
        _layoutBandStart = start;
        _layoutBandAdditive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!_layoutBandAdditive) ClearLayoutSelection();
        _layoutBand = new Rectangle
        {
            Stroke = LayoutSelectionBrush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_layoutBand, start.X);
        Canvas.SetTop(_layoutBand, start.Y);
        Panel.SetZIndex(_layoutBand, 9500);
        FieldOverlayCanvas.Children.Add(_layoutBand);
        CaptureMouse();
    }

    private bool UpdateLayoutRubberBand(Point pos)
    {
        if (_layoutBand == null) return false;
        Canvas.SetLeft(_layoutBand, Math.Min(pos.X, _layoutBandStart.X));
        Canvas.SetTop(_layoutBand, Math.Min(pos.Y, _layoutBandStart.Y));
        _layoutBand.Width = Math.Abs(pos.X - _layoutBandStart.X);
        _layoutBand.Height = Math.Abs(pos.Y - _layoutBandStart.Y);
        return true;
    }

    private bool EndLayoutRubberBand(Point pos)
    {
        if (_layoutBand == null) return false;
        var band = new Rect(_layoutBandStart, pos);
        FieldOverlayCanvas.Children.Remove(_layoutBand);
        _layoutBand = null;
        ReleaseMouseCapture();
        if (band.Width < 3 && band.Height < 3) return true; // plain click on empty space

        foreach (var (key, box) in _layoutBoxes)
        {
            var r = new Rect(Canvas.GetLeft(box), Canvas.GetTop(box), box.Width, box.Height);
            if (band.IntersectsWith(r) && !_layoutSelectedKeys.Contains(key))
            {
                _layoutSelectedKeys.Add(key);
                StyleSelected(box, true);
            }
        }
        if (_layoutSelectedKeys.Count > 0 && _layoutBoxes.TryGetValue(_layoutSelectedKeys[^1], out var primary))
            MakePrimary(primary);
        SyncSelectionToVm();
        return true;
    }

    // ── Resize (drag a handle) ───────────────────────────────────────────────

    private void OnLayoutHandleDragStarted(object sender, DragStartedEventArgs e)
    {
        if (_layoutSelectedBox == null) return;
        _layoutResizing = true;
        _layoutOrigin = new Rect(Canvas.GetLeft(_layoutSelectedBox), Canvas.GetTop(_layoutSelectedBox),
                                 _layoutSelectedBox.Width, _layoutSelectedBox.Height);
        _layoutMouseStart = Mouse.GetPosition(FieldOverlayCanvas);
    }

    private void OnLayoutHandleDragDelta(object sender, DragDeltaEventArgs e)
    {
        var box = _layoutSelectedBox;
        if (!_layoutResizing || box == null || sender is not Thumb { Tag: int idx }) return;

        // Work from the absolute mouse position (not accumulated Thumb deltas) so clamping never
        // lets the handle drift away from the cursor.
        var mouse = Mouse.GetPosition(FieldOverlayCanvas);
        double dx = mouse.X - _layoutMouseStart.X;
        double dy = mouse.Y - _layoutMouseStart.Y;
        var (maxW, maxH) = LayoutCanvasSize();

        double left = _layoutOrigin.Left, top = _layoutOrigin.Top;
        double right = _layoutOrigin.Right, bottom = _layoutOrigin.Bottom;

        bool moveLeft   = idx is 0 or 3 or 5;
        bool moveRight  = idx is 2 or 4 or 7;
        bool moveTop    = idx is 0 or 1 or 2;
        bool moveBottom = idx is 5 or 6 or 7;

        if (moveLeft)   left   = Math.Clamp(left + dx, 0, right - LayoutMinSize);
        if (moveRight)  right  = Math.Clamp(right + dx, left + LayoutMinSize, maxW);
        if (moveTop)    top    = Math.Clamp(top + dy, 0, bottom - LayoutMinSize);
        if (moveBottom) bottom = Math.Clamp(bottom + dy, top + LayoutMinSize, maxH);

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && idx is 0 or 2 or 5 or 7
            && _layoutOrigin.Width > 0 && _layoutOrigin.Height > 0)
        {
            double ratio = _layoutOrigin.Width / _layoutOrigin.Height;
            double w = right - left, h = bottom - top;
            if (w / ratio > h) h = w / ratio; else w = h * ratio;
            if (moveLeft) left = right - w; else right = left + w;
            if (moveTop)  top = bottom - h; else bottom = top + h;
        }

        Canvas.SetLeft(box, left);
        Canvas.SetTop(box, top);
        box.Width = right - left;
        box.Height = bottom - top;
        if (box.Child is TextBlock label) label.FontSize = Math.Clamp(box.Height * 0.5, 7, 12);
        PositionLayoutChrome();
        e.Handled = true;
    }

    private void OnLayoutHandleDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_layoutResizing) return;
        _layoutResizing = false;
        if (_layoutSelectedBox != null) CommitLayoutBoxes(new[] { _layoutSelectedBox }, "Resized");
    }

    // ── Keyboard (nudge / resize / delete) ───────────────────────────────────

    /// <summary>Handled in the tunnelling phase so the ScrollViewer doesn't swallow the arrow keys.</summary>
    private void OnLayoutPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsFieldLayoutMode) return;
        if (Keyboard.FocusedElement is TextBox or PasswordBox) return;

        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SelectAllLayoutBoxes();
            e.Handled = true;
            return;
        }
        if (_layoutSelectedBox == null) return;

        switch (e.Key)
        {
            case Key.Delete:
                DeleteLayoutSelection();
                e.Handled = true;
                return;
            case Key.Escape:
                ClearLayoutSelection();
                e.Handled = true;
                return;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                break;
            default:
                return;
        }

        // Canvas units are PDF points × Scale, so this nudges by 1 pt (10 pt with Shift).
        double step = (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1) * Scale;
        var (maxW, maxH) = LayoutCanvasSize();
        var boxes = SelectedBoxes().ToList();

        foreach (var box in boxes)
        {
            double l = Canvas.GetLeft(box), t = Canvas.GetTop(box);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Key == Key.Right) box.Width  = Math.Min(box.Width + step, maxW - l);
                if (e.Key == Key.Left)  box.Width  = Math.Max(LayoutMinSize, box.Width - step);
                if (e.Key == Key.Down)  box.Height = Math.Min(box.Height + step, maxH - t);
                if (e.Key == Key.Up)    box.Height = Math.Max(LayoutMinSize, box.Height - step);
            }
            else
            {
                if (e.Key == Key.Left)  l -= step;
                if (e.Key == Key.Right) l += step;
                if (e.Key == Key.Up)    t -= step;
                if (e.Key == Key.Down)  t += step;
                Canvas.SetLeft(box, Math.Clamp(l, 0, Math.Max(0, maxW - box.Width)));
                Canvas.SetTop(box, Math.Clamp(t, 0, Math.Max(0, maxH - box.Height)));
            }
        }

        PositionLayoutChrome();
        CommitLayoutBoxes(boxes, Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? "Resized" : "Nudged");
        e.Handled = true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private (double Width, double Height) LayoutCanvasSize()
    {
        double w = FieldOverlayCanvas.Width > 0 ? FieldOverlayCanvas.Width : FieldOverlayCanvas.ActualWidth;
        double h = FieldOverlayCanvas.Height > 0 ? FieldOverlayCanvas.Height : FieldOverlayCanvas.ActualHeight;
        return (w, h);
    }

    /// <summary>Converts the boxes' canvas rectangles back to PDF points and stores them (one undo step).</summary>
    private void CommitLayoutBoxes(IReadOnlyList<Border> boxes, string description)
    {
        if (_vm?.Document == null || boxes.Count == 0) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

        var changes = new List<(FormFieldInfo, FieldBounds)>();
        foreach (var box in boxes)
        {
            if (box.Tag is not FormFieldInfo field) continue;
            double x = Canvas.GetLeft(box), y = Canvas.GetTop(box);
            changes.Add((field, new FieldBounds(
                Left:   Math.Round(x / Scale, 2),
                Bottom: Math.Round(pageH - (y + box.Height) / Scale, 2),
                Width:  Math.Round(box.Width / Scale, 2),
                Height: Math.Round(box.Height / Scale, 2))));
        }
        _vm.SetFieldBoundsBatch(changes, description, refresh: false);
    }
}
