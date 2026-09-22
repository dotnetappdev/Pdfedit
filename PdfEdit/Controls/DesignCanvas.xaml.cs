using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using PdfEdit.Models;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

/// <summary>DataTemplateSelector that dispatches element DataTemplates by type.</summary>
public class DesignElementTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate     { get; set; }
    public DataTemplate? RectTemplate     { get; set; }
    public DataTemplate? EllipseTemplate  { get; set; }
    public DataTemplate? LineTemplate     { get; set; }
    public DataTemplate? ImageTemplate    { get; set; }
    public DataTemplate? FreehandTemplate { get; set; }
    public DataTemplate? TableTemplate    { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        TextDesignElement                                       => TextTemplate,
        ShapeDesignElement { ElementType: DesignElementType.Rectangle } => RectTemplate,
        ShapeDesignElement { ElementType: DesignElementType.Ellipse }   => EllipseTemplate,
        ShapeDesignElement { ElementType: DesignElementType.Line or DesignElementType.Arrow } => LineTemplate,
        ImageDesignElement                                      => ImageTemplate,
        FreehandDesignElement                                   => FreehandTemplate,
        TableDesignElement                                      => TableTemplate,
        _                                                       => base.SelectTemplate(item, container)
    };
}

/// <summary>WPF design canvas — mouse drawing, selection, resize handles.</summary>
public partial class DesignCanvas : UserControl
{
    // ── Mouse state ──────────────────────────────────────────────────────────
    private enum DragMode { None, Moving, Drawing, ResizingSE, ResizingNW, ResizingNE, ResizingSW, ResizingE, ResizingS, RubberBand, MovingMulti }

    private DragMode _dragMode = DragMode.None;
    private Point _dragStart;
    private Point _elemOrigin;
    private Size  _elemSizeOrigin;
    private DesignElement? _dragElement;
    // Origin positions for multi-selection move
    private Dictionary<DesignElement, Point> _multiOrigins = new();

    // Ink (freehand)
    private bool _isInking;
    private List<Point> _currentStroke = new();
    private List<List<Point>> _currentFreehandStrokes = new();

    // Selection handles (8 Thumb elements placed on SelectionOverlay)
    private readonly Thumb[] _handles = new Thumb[8];
    private readonly double _handleHalf = 5;

    private DesignCanvasViewModel? VM => DataContext as DesignCanvasViewModel;

    public DesignCanvas()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => OnVmChanged();

        // Create resize handles
        for (int i = 0; i < 8; i++)
        {
            var t = new Thumb
            {
                Width = 10, Height = 10,
                Cursor = ResizeCursorForHandle(i),
                Template = BuildHandleTemplate(),
                Tag = i
            };
            t.DragDelta += Handle_DragDelta;
            t.DragCompleted += Handle_DragCompleted;
            _handles[i] = t;
            SelectionOverlay.Children.Add(t);
        }
        HideHandles();
    }

    private void OnVmChanged()
    {
        if (VM == null) return;
        VM.Elements.CollectionChanged += (_, _) => { RefreshSelectionHandles(); DrawGrid(); };
        VM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DesignCanvasViewModel.SelectedElement)
                               or nameof(DesignCanvasViewModel.ActiveTool))
                RefreshSelectionHandles();
            if (e.PropertyName is nameof(DesignCanvasViewModel.Zoom))
                ApplyZoom();
            if (e.PropertyName is nameof(DesignCanvasViewModel.ShowGrid)
                               or nameof(DesignCanvasViewModel.GridSize))
                DrawGrid();
            if (e.PropertyName is nameof(DesignCanvasViewModel.PageBackground))
                ApplyBackground();
        };
        ApplyZoom();
        ApplyBackground();
        DrawGrid();
    }

    private void ApplyBackground()
    {
        if (VM == null) return;
        PageBorder.Background = new SolidColorBrush(VM.PageBackground);
    }

    // ── Zoom ─────────────────────────────────────────────────────────────────

    private void ApplyZoom()
    {
        if (VM == null) return;
        var scale = new ScaleTransform(VM.Zoom, VM.Zoom);
        PageBorder.LayoutTransform = scale;
        PageShadow.LayoutTransform = scale;
    }

    // ── Grid drawing ──────────────────────────────────────────────────────────

    private void DrawGrid()
    {
        GridOverlay.Children.Clear();
        if (VM == null || !VM.ShowGrid) return;

        double step = VM.GridSize;
        var brush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        for (double x = 0; x <= VM.PageWidth; x += step)
            GridOverlay.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = VM.PageHeight, Stroke = brush, StrokeThickness = 0.5 });
        for (double y = 0; y <= VM.PageHeight; y += step)
            GridOverlay.Children.Add(new Line { X1 = 0, Y1 = y, X2 = VM.PageWidth, Y2 = y, Stroke = brush, StrokeThickness = 0.5 });
    }

    // ── Mouse events ─────────────────────────────────────────────────────────

    private void InteractionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (VM == null) return;
        var pos = e.GetPosition(InteractionCanvas);
        _dragStart = pos;

        // Finish any active text edit
        FinishTextEdit();

        if (VM.ActiveTool == DesignTool.Select)
        {
            var hit = HitTestElement(pos);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (hit != null)
            {
                if (shift)
                {
                    VM.AddToMultiSelection(hit);
                }
                else if (VM.HasMultiSelection && VM.MultiSelection.Contains(hit))
                {
                    // drag all selected elements
                    _dragMode = DragMode.MovingMulti;
                    _multiOrigins = VM.MultiSelection.ToDictionary(el => el, el => new Point(el.X, el.Y));
                    _dragStart = pos;
                    InteractionCanvas.CaptureMouse();
                }
                else
                {
                    VM.SetMultiSelection([hit]);
                    if (!hit.IsLocked)
                    {
                        _dragMode = DragMode.Moving;
                        _dragElement = hit;
                        _elemOrigin = new Point(hit.X, hit.Y);
                        InteractionCanvas.CaptureMouse();
                    }
                }
            }
            else
            {
                if (!shift) VM.ClearSelection();
                HideHandles();
                _dragMode = DragMode.RubberBand;
                _dragStart = pos;
                InteractionCanvas.CaptureMouse();
            }
            return;
        }

        if (VM.ActiveTool == DesignTool.Image)
        {
            InsertImage(pos);
            return;
        }

        if (VM.ActiveTool == DesignTool.Pen)
        {
            _isInking = true;
            _currentStroke = new List<Point> { pos };
            _currentFreehandStrokes = new List<List<Point>>();
            InkSurface.EditingMode = InkCanvasEditingMode.Ink;
            InkSurface.DefaultDrawingAttributes.Color = VM.PenColor;
            InkSurface.DefaultDrawingAttributes.Width = VM.PenThickness;
            InkSurface.DefaultDrawingAttributes.Height = VM.PenThickness;
            return;
        }

        // Shape / text drawing — show preview
        _dragMode = DragMode.Drawing;
        InteractionCanvas.CaptureMouse();

        DrawPreviewRect.Visibility = Visibility.Collapsed;
        DrawPreviewLine.Visibility = Visibility.Collapsed;

        if (VM.ActiveTool is DesignTool.Text)
        {
            var elem = VM.CreateTextElement(pos.X, pos.Y);
            VM.AddElement(elem);
            _dragMode = DragMode.None;
            InteractionCanvas.ReleaseMouseCapture();
            BeginTextEdit(elem);
            return;
        }

        if (VM.ActiveTool is DesignTool.Table)
        {
            var elem = VM.CreateTableElement(pos.X, pos.Y);
            VM.AddElement(elem);
            _dragMode = DragMode.None;
            InteractionCanvas.ReleaseMouseCapture();
            VM.ActiveTool = DesignTool.Select;
            return;
        }
    }

    private void InteractionCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (VM == null) return;
        var pos = e.GetPosition(InteractionCanvas);

        // Update cursor
        UpdateCursor(pos);

        if (_dragMode == DragMode.Moving && _dragElement != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var dx = pos.X - _dragStart.X;
            var dy = pos.Y - _dragStart.Y;
            _dragElement.X = VM.Snap(Math.Max(0, _elemOrigin.X + dx));
            _dragElement.Y = VM.Snap(Math.Max(0, _elemOrigin.Y + dy));
            RefreshSelectionHandles();
            return;
        }

        if (_dragMode == DragMode.MovingMulti && e.LeftButton == MouseButtonState.Pressed)
        {
            var dx = pos.X - _dragStart.X;
            var dy = pos.Y - _dragStart.Y;
            foreach (var (elem, origin) in _multiOrigins)
            {
                elem.X = VM.Snap(Math.Max(0, origin.X + dx));
                elem.Y = VM.Snap(Math.Max(0, origin.Y + dy));
            }
            RefreshSelectionHandles();
            return;
        }

        if (_dragMode == DragMode.RubberBand && e.LeftButton == MouseButtonState.Pressed)
        {
            double x = Math.Min(pos.X, _dragStart.X);
            double y = Math.Min(pos.Y, _dragStart.Y);
            double w = Math.Abs(pos.X - _dragStart.X);
            double h = Math.Abs(pos.Y - _dragStart.Y);
            Canvas.SetLeft(DrawPreviewRect, x); Canvas.SetTop(DrawPreviewRect, y);
            DrawPreviewRect.Width = w; DrawPreviewRect.Height = h;
            DrawPreviewRect.Visibility = Visibility.Visible;
            DrawPreviewLine.Visibility = Visibility.Collapsed;
            return;
        }

        if (_dragMode == DragMode.Drawing && e.LeftButton == MouseButtonState.Pressed)
        {
            // Show draw preview
            double x = Math.Min(pos.X, _dragStart.X);
            double y = Math.Min(pos.Y, _dragStart.Y);
            double w = Math.Abs(pos.X - _dragStart.X);
            double h = Math.Abs(pos.Y - _dragStart.Y);

            if (VM.ActiveTool is DesignTool.Line or DesignTool.Arrow)
            {
                DrawPreviewLine.X1 = _dragStart.X; DrawPreviewLine.Y1 = _dragStart.Y;
                DrawPreviewLine.X2 = pos.X;         DrawPreviewLine.Y2 = pos.Y;
                DrawPreviewLine.Visibility = Visibility.Visible;
                DrawPreviewRect.Visibility = Visibility.Collapsed;
            }
            else
            {
                Canvas.SetLeft(DrawPreviewRect, x); Canvas.SetTop(DrawPreviewRect, y);
                DrawPreviewRect.Width = w; DrawPreviewRect.Height = h;
                DrawPreviewRect.Visibility = Visibility.Visible;
                DrawPreviewLine.Visibility = Visibility.Collapsed;
            }
            return;
        }

        if (_isInking && e.LeftButton == MouseButtonState.Pressed)
        {
            _currentStroke.Add(pos);
        }
    }

    private void InteractionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (VM == null) return;
        var pos = e.GetPosition(InteractionCanvas);

        DrawPreviewRect.Visibility = Visibility.Collapsed;
        DrawPreviewLine.Visibility = Visibility.Collapsed;

        if (_dragMode == DragMode.Moving)
        {
            InteractionCanvas.ReleaseMouseCapture();
            _dragMode = DragMode.None;
            _dragElement = null;
            return;
        }

        if (_dragMode == DragMode.MovingMulti)
        {
            InteractionCanvas.ReleaseMouseCapture();
            _dragMode = DragMode.None;
            _multiOrigins.Clear();
            return;
        }

        if (_dragMode == DragMode.RubberBand)
        {
            InteractionCanvas.ReleaseMouseCapture();
            _dragMode = DragMode.None;
            DrawPreviewRect.Visibility = Visibility.Collapsed;

            double x = Math.Min(pos.X, _dragStart.X);
            double y = Math.Min(pos.Y, _dragStart.Y);
            double w = Math.Abs(pos.X - _dragStart.X);
            double h = Math.Abs(pos.Y - _dragStart.Y);

            if (w > 4 && h > 4 && VM != null)
            {
                var selRect = new Rect(x, y, w, h);
                var hits = VM.Elements.Where(e => new Rect(e.X, e.Y, e.Width, e.Height).IntersectsWith(selRect)).ToList();
                if (hits.Count > 0)
                    VM.SetMultiSelection(hits);
                else
                    VM.ClearSelection();
                RefreshSelectionHandles();
            }
            return;
        }

        if (_dragMode == DragMode.Drawing && VM.ActiveTool != DesignTool.Select)
        {
            InteractionCanvas.ReleaseMouseCapture();
            _dragMode = DragMode.None;

            double x = Math.Min(pos.X, _dragStart.X);
            double y = Math.Min(pos.Y, _dragStart.Y);
            double w = Math.Abs(pos.X - _dragStart.X);
            double h = Math.Abs(pos.Y - _dragStart.Y);

            if (w < 4 && h < 4) return; // too small — ignore

            DesignElementType? shapeType = VM.ActiveTool switch
            {
                DesignTool.Rectangle => DesignElementType.Rectangle,
                DesignTool.Ellipse   => DesignElementType.Ellipse,
                DesignTool.Line      => DesignElementType.Line,
                DesignTool.Arrow     => DesignElementType.Arrow,
                _                    => null
            };

            if (shapeType.HasValue)
            {
                var elem = VM.CreateShapeElement(shapeType.Value, x, y, w, h);
                VM.AddElement(elem);
            }

            VM.ActiveTool = DesignTool.Select;
            return;
        }

        if (_isInking)
        {
            _isInking = false;
            if (_currentStroke.Count > 1)
            {
                _currentFreehandStrokes.Add(_currentStroke);
                var bounds = ComputeStrokeBounds(_currentFreehandStrokes);
                var elem = VM.CreateFreehandElement(_currentFreehandStrokes, bounds);
                VM.AddElement(elem);
            }
            InkSurface.Strokes.Clear();
            InkSurface.EditingMode = InkCanvasEditingMode.None;
            VM.ActiveTool = DesignTool.Select;
            _currentFreehandStrokes = new();
            return;
        }
    }

    private void InteractionCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (VM == null) return;
        var pos = e.GetPosition(InteractionCanvas);
        var hit = HitTestElement(pos);
        if (hit != null) VM.SelectedElement = hit;
    }

    // Double-click to edit text
    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (VM?.SelectedElement is TextDesignElement t)
            BeginTextEdit(t);
    }

    // ── Text editing ──────────────────────────────────────────────────────────

    private void BeginTextEdit(TextDesignElement elem)
    {
        elem.IsEditing = true;
        // Focus the TextBox inside its container
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            var container = FindContainerForElement(elem);
            if (container?.ContentTemplate?.FindName("EditBox", container) is TextBox tb)
            {
                tb.Focus();
                tb.SelectAll();
                tb.LostFocus += (_, _) => FinishTextEdit();
            }
        });
    }

    private void FinishTextEdit()
    {
        if (VM == null) return;
        foreach (var e in VM.Elements.OfType<TextDesignElement>())
            e.IsEditing = false;
    }

    // ── Hit testing ───────────────────────────────────────────────────────────

    private DesignElement? HitTestElement(Point pos)
    {
        if (VM == null) return null;
        // Test in reverse ZOrder so topmost element wins
        for (int i = VM.Elements.Count - 1; i >= 0; i--)
        {
            var e = VM.Elements[i];
            var bounds = new Rect(e.X, e.Y, e.Width, e.Height);
            if (bounds.Contains(pos)) return e;
        }
        return null;
    }

    // ── Resize handles ────────────────────────────────────────────────────────

    private void RefreshSelectionHandles()
    {
        HideHandles();
        if (VM == null) return;

        var accentColor = TryFindResource("AccentColor") is Color c ? c : Color.FromRgb(0x0A, 0x84, 0xFF);

        // Draw individual selection rects for all multi-selected elements
        if (VM.HasMultiSelection)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = 0, maxY = 0;
            foreach (var e in VM.MultiSelection)
            {
                var selRect = new Rectangle
                {
                    Width = e.Width + 2, Height = e.Height + 2,
                    Stroke = new SolidColorBrush(accentColor) { Opacity = 0.6 },
                    StrokeThickness = 1, Fill = Brushes.Transparent,
                    StrokeDashArray = new DoubleCollection([4, 2]),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(selRect, e.X - 1); Canvas.SetTop(selRect, e.Y - 1);
                SelectionOverlay.Children.Insert(0, selRect);
                minX = Math.Min(minX, e.X); minY = Math.Min(minY, e.Y);
                maxX = Math.Max(maxX, e.X + e.Width); maxY = Math.Max(maxY, e.Y + e.Height);
            }
            // Group bounding box
            var groupRect = new Rectangle
            {
                Width = maxX - minX + 4, Height = maxY - minY + 4,
                Stroke = new SolidColorBrush(accentColor),
                StrokeThickness = 1.5, Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(groupRect, minX - 2); Canvas.SetTop(groupRect, minY - 2);
            SelectionOverlay.Children.Insert(0, groupRect);
            return;
        }

        var sel = VM.SelectedElement;
        if (sel == null) return;

        double l = sel.X, t = sel.Y, w = sel.Width, h = sel.Height;
        double hh = _handleHalf;

        // 0=NW 1=N 2=NE 3=W 4=E 5=SW 6=S 7=SE
        Point[] positions =
        {
            new(l - hh,     t - hh),
            new(l + w/2-hh, t - hh),
            new(l + w - hh, t - hh),
            new(l - hh,     t + h/2 - hh),
            new(l + w - hh, t + h/2 - hh),
            new(l - hh,     t + h - hh),
            new(l + w/2-hh, t + h - hh),
            new(l + w - hh, t + h - hh),
        };

        // Selection border rectangle
        var singleRect = new Rectangle
        {
            Width = w + 2, Height = h + 2,
            Stroke = new SolidColorBrush(accentColor),
            StrokeThickness = 1.5, Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(singleRect, l - 1); Canvas.SetTop(singleRect, t - 1);
        SelectionOverlay.Children.Insert(0, singleRect);

        for (int i = 0; i < 8; i++)
        {
            Canvas.SetLeft(_handles[i], positions[i].X);
            Canvas.SetTop(_handles[i],  positions[i].Y);
            _handles[i].Visibility = Visibility.Visible;
        }
    }

    private void HideHandles()
    {
        foreach (var r in SelectionOverlay.Children.OfType<Rectangle>().ToList())
            SelectionOverlay.Children.Remove(r);
        foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
    }

    private void Handle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var sel = VM?.SelectedElement;
        if (sel == null || sender is not Thumb t || t.Tag is not int idx) return;

        double dx = e.HorizontalChange, dy = e.VerticalChange;

        switch (idx)
        {
            case 0: sel.X += dx; sel.Y += dy; sel.Width -= dx; sel.Height -= dy; break; // NW
            case 1:              sel.Y += dy;                   sel.Height -= dy; break; // N
            case 2:              sel.Y += dy; sel.Width += dx;  sel.Height -= dy; break; // NE
            case 3: sel.X += dx;              sel.Width -= dx;                    break; // W
            case 4:               sel.Width += dx;                                break; // E
            case 5: sel.X += dx;              sel.Width -= dx;  sel.Height += dy; break; // SW
            case 6:                                             sel.Height += dy; break; // S
            case 7:               sel.Width += dx;              sel.Height += dy; break; // SE
        }

        RefreshSelectionHandles();
    }

    private void Handle_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Selection handles do their own drag; nothing extra needed
    }

    // ── Cursor ────────────────────────────────────────────────────────────────

    private void UpdateCursor(Point pos)
    {
        if (VM == null) return;
        Cursor = VM.ActiveTool switch
        {
            DesignTool.Text      => Cursors.IBeam,
            DesignTool.Pen       => Cursors.Pen,
            DesignTool.Rectangle or DesignTool.Ellipse or DesignTool.Line or DesignTool.Arrow => Cursors.Cross,
            DesignTool.Image or DesignTool.Table => Cursors.Cross,
            _ => HitTestElement(pos) != null && !(HitTestElement(pos)?.IsLocked ?? false) ? Cursors.SizeAll : Cursors.Arrow
        };
    }

    private static Cursor ResizeCursorForHandle(int idx) => idx switch
    {
        0 or 7 => Cursors.SizeNWSE,
        2 or 5 => Cursors.SizeNESW,
        1 or 6 => Cursors.SizeNS,
        _      => Cursors.SizeWE,
    };

    // ── Image insertion ───────────────────────────────────────────────────────

    private void InsertImage(Point pos)
    {
        if (VM == null) return;
        var dlg = new OpenFileDialog
        {
            Title = "Insert Image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var bmp = new BitmapImage(new Uri(dlg.FileName));
            var elem = VM.CreateImageElement(dlg.FileName, bmp, pos.X, pos.Y);
            VM.AddElement(elem);
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Could not load image.", ex);
        }
        VM.ActiveTool = DesignTool.Select;
    }

    // ── Context menu handlers ─────────────────────────────────────────────────

    private void Copy_Click(object sender, RoutedEventArgs e)      => VM?.CopySelected();
    private void Paste_Click(object sender, RoutedEventArgs e)     => VM?.PasteClipboard();
    private void Duplicate_Click(object sender, RoutedEventArgs e) => VM?.DuplicateSelected();
    private void BringForward_Click(object sender, RoutedEventArgs e) => VM?.BringForward();
    private void SendBackward_Click(object sender, RoutedEventArgs e) => VM?.SendBackward();
    private void BringToFront_Click(object sender, RoutedEventArgs e) => VM?.BringToFront();
    private void SendToBack_Click(object sender, RoutedEventArgs e)   => VM?.SendToBack();
    private void LockElement_Click(object sender, RoutedEventArgs e)  { if (VM?.SelectedElement != null) VM.SelectedElement.IsLocked = !VM.SelectedElement.IsLocked; }
    private void DeleteElement_Click(object sender, RoutedEventArgs e) { VM?.DeleteSelected(); HideHandles(); }
    private void SelectAll_Click(object sender, RoutedEventArgs e)    => VM?.SelectAll();

    // ── Keyboard shortcuts ────────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (VM == null) return;

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (VM.SelectedElement is TextDesignElement { IsEditing: true }) return;
            VM.DeleteSelected();
            HideHandles();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { VM.ClearSelection(); HideHandles(); }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { VM.Undo(); e.Handled = true; }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) { VM.Redo(); e.Handled = true; }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { VM.CopySelected(); e.Handled = true; }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { VM.PasteClipboard(); e.Handled = true; }
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control) { VM.DuplicateSelected(); e.Handled = true; }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { VM.SelectAll(); e.Handled = true; }
        // Nudge selected with arrow keys (1px; 10px with Shift; Ctrl+arrow = resize)
        else if (VM.SelectedElement is DesignElement sel && !sel.IsLocked && new[] { Key.Left, Key.Right, Key.Up, Key.Down }.Contains(e.Key))
        {
            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            bool resize = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (resize)
            {
                if (e.Key == Key.Right) sel.Width  += step;
                if (e.Key == Key.Left)  sel.Width  = Math.Max(4, sel.Width - step);
                if (e.Key == Key.Down)  sel.Height += step;
                if (e.Key == Key.Up)    sel.Height = Math.Max(4, sel.Height - step);
            }
            else
            {
                if (e.Key == Key.Left)  sel.X -= step;
                if (e.Key == Key.Right) sel.X += step;
                if (e.Key == Key.Up)    sel.Y -= step;
                if (e.Key == Key.Down)  sel.Y += step;
            }
            VM.NotifyPositionProperties();
            RefreshSelectionHandles();
            e.Handled = true;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ContentPresenter? FindContainerForElement(DesignElement elem)
    {
        if (ElementsHost.ItemContainerGenerator.ContainerFromItem(elem) is ContentPresenter cp)
            return cp;
        return null;
    }

    private static Rect ComputeStrokeBounds(List<List<Point>> strokes)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = 0, maxY = 0;
        foreach (var stroke in strokes)
            foreach (var pt in stroke)
            {
                minX = Math.Min(minX, pt.X); minY = Math.Min(minY, pt.Y);
                maxX = Math.Max(maxX, pt.X); maxY = Math.Max(maxY, pt.Y);
            }
        return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    private static ControlTemplate BuildHandleTemplate()
    {
        var tpl = new ControlTemplate(typeof(Thumb));
        var rect = new FrameworkElementFactory(typeof(Rectangle));
        rect.SetValue(Rectangle.FillProperty, Brushes.DodgerBlue);
        rect.SetValue(Rectangle.StrokeProperty, Brushes.White);
        rect.SetValue(Rectangle.StrokeThicknessProperty, 1.0);
        tpl.VisualTree = rect;
        return tpl;
    }
}
