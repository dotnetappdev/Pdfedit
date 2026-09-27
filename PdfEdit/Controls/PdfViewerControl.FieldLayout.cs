using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PdfEdit.Models;

namespace PdfEdit.Controls;

/// <summary>
/// Acrobat "Prepare Form"-style field layout editing for the live view.
///
/// While the Select tool (or one of the Add Form Field tools) is active, form fields are drawn as
/// named placeholder boxes instead of live fill-in controls. Click a field to select it — a blue
/// frame with eight resize handles appears — drag the body to move it, drag a handle to resize it
/// (Shift on a corner keeps the aspect ratio), use the arrow keys to nudge (Shift = 10 pt,
/// Ctrl = resize) and Delete to remove it. Changes go through <c>MainViewModel.SetFieldBounds</c>,
/// are undoable, and are written to the PDF on save.
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

    // Selected field (tracked by name + widget index so the selection survives an overlay rebuild,
    // e.g. after undo or after a newly-added field reloads the document).
    private (string Name, int WidgetIndex)? _layoutSelectedKey;
    private FormFieldInfo? _layoutSelectedField;
    private Border? _layoutSelectedBox;

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
    private bool IsFieldLayoutMode => _vm?.ActiveTool is ActiveTool.Select
        or ActiveTool.AddTextField or ActiveTool.AddCheckbox
        or ActiveTool.AddComboBox or ActiveTool.AddRadioButton;

    /// <summary>Switches to the Select tool and selects <paramref name="field"/> for moving/resizing.</summary>
    private void BeginFieldLayoutEdit(FormFieldInfo field)
    {
        if (_vm == null) return;
        _layoutSelectedKey = (field.Name, field.WidgetIndex);
        _vm.ActiveTool = ActiveTool.Select; // triggers an overlay rebuild that restores the selection
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
            _                     => string.Empty,
        };

        var label = new TextBlock
        {
            Text = glyph + field.Name,
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
            ToolTip = $"{field.Name} ({field.FieldType}) — drag to move, drag the handles to resize",
        };
        System.Windows.Automation.AutomationProperties.SetName(box, $"Form field layout: {field.Name}");

        box.MouseLeftButtonDown += OnLayoutBoxMouseDown;
        box.MouseMove += OnLayoutBoxMouseMove;
        box.MouseLeftButtonUp += OnLayoutBoxMouseUp;
        box.MouseRightButtonDown += (_, e) => { SelectLayoutBox(box); e.Handled = false; };

        var menu = new ContextMenu();
        var del = new MenuItem { Header = $"Delete field \"{field.Name}\"", InputGestureText = "Del" };
        del.Click += (_, _) => DeleteLayoutSelection();
        var fill = new MenuItem { Header = "Switch to fill mode (Hand tool)" };
        fill.Click += (_, _) => { if (_vm != null) _vm.ActiveTool = ActiveTool.Hand; };
        menu.Items.Add(del);
        menu.Items.Add(new Separator());
        menu.Items.Add(fill);
        box.ContextMenu = menu;

        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);
        FieldOverlayCanvas.Children.Add(box);

        if (_layoutSelectedKey is { } key && key.Name == field.Name && key.WidgetIndex == field.WidgetIndex)
            SelectLayoutBox(box);
    }

    // ── Selection ────────────────────────────────────────────────────────────

    private void SelectLayoutBox(Border box)
    {
        if (box.Tag is not FormFieldInfo field) return;

        if (_layoutSelectedBox != null && _layoutSelectedBox != box)
            _layoutSelectedBox.BorderThickness = new Thickness(1);

        _layoutSelectedBox = box;
        _layoutSelectedField = field;
        _layoutSelectedKey = (field.Name, field.WidgetIndex);
        if (_vm != null) _vm.SelectedField = field;

        Panel.SetZIndex(box, 5000);
        EnsureLayoutChrome();
        PositionLayoutChrome();
    }

    private void ClearLayoutSelection()
    {
        _layoutSelectedBox = null;
        _layoutSelectedField = null;
        _layoutSelectedKey = null;
        _layoutMoving = _layoutResizing = false;
        if (_layoutFrame != null) _layoutFrame.Visibility = Visibility.Collapsed;
        foreach (var t in _layoutHandles)
            if (t != null) t.Visibility = Visibility.Collapsed;
    }

    /// <summary>Called at the start of BuildFieldOverlay — the canvas is about to be cleared.</summary>
    private void ResetLayoutChrome()
    {
        _layoutSelectedBox = null;
        _layoutSelectedField = null;
        _layoutFrame = null;
        _layoutMoving = _layoutResizing = false;
        for (int i = 0; i < _layoutHandles.Length; i++) _layoutHandles[i] = null;
    }

    private void DeleteLayoutSelection()
    {
        if (_vm == null || _layoutSelectedField == null) return;
        var field = _layoutSelectedField;
        ClearLayoutSelection();
        _vm.DeleteField(field);
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
        if (sender is not Border box) return;
        SelectLayoutBox(box);
        Focus();

        _layoutMoving = true;
        _layoutMoved = false;
        _layoutMouseStart = e.GetPosition(FieldOverlayCanvas);
        _layoutOrigin = new Rect(Canvas.GetLeft(box), Canvas.GetTop(box), box.Width, box.Height);
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

        var (maxW, maxH) = LayoutCanvasSize();
        Canvas.SetLeft(box, Math.Clamp(_layoutOrigin.X + dx, 0, Math.Max(0, maxW - box.Width)));
        Canvas.SetTop(box, Math.Clamp(_layoutOrigin.Y + dy, 0, Math.Max(0, maxH - box.Height)));
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
        if (_layoutMoved) CommitLayoutBox(box);
        _layoutMoved = false;
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
        if (_layoutSelectedBox != null) CommitLayoutBox(_layoutSelectedBox);
    }

    // ── Keyboard (nudge / resize / delete) ───────────────────────────────────

    /// <summary>Handled in the tunnelling phase so the ScrollViewer doesn't swallow the arrow keys.</summary>
    private void OnLayoutPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_layoutSelectedBox == null || !IsFieldLayoutMode) return;
        if (Keyboard.FocusedElement is TextBox or PasswordBox) return;

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

        var box = _layoutSelectedBox;
        // Canvas units are PDF points × Scale, so this nudges by 1 pt (10 pt with Shift).
        double step = (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1) * Scale;
        var (maxW, maxH) = LayoutCanvasSize();

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double l = Canvas.GetLeft(box), t = Canvas.GetTop(box);
            if (e.Key == Key.Right) box.Width  = Math.Min(box.Width + step, maxW - l);
            if (e.Key == Key.Left)  box.Width  = Math.Max(LayoutMinSize, box.Width - step);
            if (e.Key == Key.Down)  box.Height = Math.Min(box.Height + step, maxH - t);
            if (e.Key == Key.Up)    box.Height = Math.Max(LayoutMinSize, box.Height - step);
        }
        else
        {
            double l = Canvas.GetLeft(box), t = Canvas.GetTop(box);
            if (e.Key == Key.Left)  l -= step;
            if (e.Key == Key.Right) l += step;
            if (e.Key == Key.Up)    t -= step;
            if (e.Key == Key.Down)  t += step;
            Canvas.SetLeft(box, Math.Clamp(l, 0, Math.Max(0, maxW - box.Width)));
            Canvas.SetTop(box, Math.Clamp(t, 0, Math.Max(0, maxH - box.Height)));
        }

        PositionLayoutChrome();
        CommitLayoutBox(box);
        e.Handled = true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private (double Width, double Height) LayoutCanvasSize()
    {
        double w = FieldOverlayCanvas.Width > 0 ? FieldOverlayCanvas.Width : FieldOverlayCanvas.ActualWidth;
        double h = FieldOverlayCanvas.Height > 0 ? FieldOverlayCanvas.Height : FieldOverlayCanvas.ActualHeight;
        return (w, h);
    }

    /// <summary>Converts the box's canvas rectangle back to PDF points and stores it on the field.</summary>
    private void CommitLayoutBox(Border box)
    {
        if (_vm?.Document == null || box.Tag is not FormFieldInfo field) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

        double x = Canvas.GetLeft(box), y = Canvas.GetTop(box);
        var bounds = new FieldBounds(
            Left:   Math.Round(x / Scale, 2),
            Bottom: Math.Round(pageH - (y + box.Height) / Scale, 2),
            Width:  Math.Round(box.Width / Scale, 2),
            Height: Math.Round(box.Height / Scale, 2));
        _vm.SetFieldBounds(field, bounds);
    }
}
