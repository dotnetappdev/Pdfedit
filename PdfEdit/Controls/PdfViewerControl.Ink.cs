using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PdfEdit.Models;

namespace PdfEdit.Controls;

/// <summary>
/// standard drawing in the live view:
/// <list type="bullet">
/// <item>Draw / shape tools work anywhere on the page — also over form fields and placed text,
/// which used to swallow the mouse so nothing was drawn there.</item>
/// <item>Freehand strokes are smoothed, keep the chosen thickness (in points, so they scale with
/// zoom and save at the same width) and are undoable.</item>
/// <item>With the Select tool, shapes and strokes can be dragged anywhere on the page (undoable).</item>
/// </list>
/// Strokes are stored as FreeTextAnnotation text: <c>__INK__:#RRGGBB|width:x,y;x,y;…</c> (PDF points).
/// </summary>
public partial class PdfViewerControl
{
    private static bool IsDrawTool(ActiveTool t) => t is ActiveTool.DrawFreehand or ActiveTool.DrawRectangle
        or ActiveTool.DrawEllipse or ActiveTool.DrawArrow or ActiveTool.DrawCallout or ActiveTool.Eraser
        or ActiveTool.DrawLine or ActiveTool.DrawCloud or ActiveTool.DrawPolygon or ActiveTool.DrawPolyline
        or ActiveTool.MeasureDistance or ActiveTool.MeasurePerimeter or ActiveTool.MeasureArea;

    /// <summary>
    /// Fields and annotation text boxes handle their own mouse-down, so a drawing tool never got it
    /// when the stroke started on them. Route drawing tools before those controls see the click.
    /// </summary>
    private void OnDrawToolPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null || !IsDrawTool(_vm.ActiveTool) || IsFieldLayoutMode) return;
        if (e.OriginalSource is DependencyObject src && IsInsideChrome(src)) return;
        if (!IsOnPage(e.GetPosition(AnnotationCanvas))) return;
        OnMouseLeftButtonDown(sender, e);
        e.Handled = true;
    }

    // Toolbar buttons, grips and scroll bars keep their clicks — but a form field's own check box /
    // radio button / dropdown arrow is just page content to draw over.
    private bool IsInsideChrome(DependencyObject d)
    {
        bool sawButton = false;
        for (var cur = d; cur != null; cur = cur is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
        {
            if (cur is Thumb or ScrollBar) return true;
            if (cur is ButtonBase) sawButton = true;
            if (ReferenceEquals(cur, FieldOverlayCanvas)) return false;
        }
        return sawButton;
    }

    // ── Ink encoding ─────────────────────────────────────────────────────────

    public readonly record struct InkData(string Color, double Width, List<Point> Points);

    public static InkData? ParseInk(string text)
    {
        if (!text.StartsWith("__INK__:", StringComparison.Ordinal)) return null;
        var parts = text.Split(':', 3);
        if (parts.Length != 3) return null;
        var head = parts[1].Split('|');
        double width = head.Length > 1 && double.TryParse(head[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 2.0;
        var pts = new List<Point>();
        foreach (var ptStr in parts[2].Split(';'))
        {
            var xy = ptStr.Split(',');
            if (xy.Length == 2
                && double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                && double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                pts.Add(new Point(x, y));
        }
        return new InkData(head[0], width, pts);
    }

    public static string EncodeInk(string color, double width, IEnumerable<Point> pdfPoints) =>
        $"__INK__:{color}|{width.ToString("0.##", CultureInfo.InvariantCulture)}:" +
        string.Join(";", pdfPoints.Select(p =>
            $"{p.X.ToString("F2", CultureInfo.InvariantCulture)},{p.Y.ToString("F2", CultureInfo.InvariantCulture)}"));

    private static void SetInkBounds(FreeTextAnnotation ann, IReadOnlyList<Point> pdfPts, double width)
    {
        double minX = pdfPts.Min(p => p.X), maxX = pdfPts.Max(p => p.X);
        double minY = pdfPts.Min(p => p.Y), maxY = pdfPts.Max(p => p.Y);
        double pad = width / 2;
        ann.Left = minX - pad;
        ann.Bottom = minY - pad;
        ann.Width = Math.Max(maxX - minX + width, 2);
        ann.Height = Math.Max(maxY - minY + width, 2);
    }

    /// <summary>Smooth curve through the points (quadratic Béziers between midpoints).</summary>
    private static Geometry SmoothGeometry(IReadOnlyList<Point> pts)
    {
        var fig = new PathFigure { StartPoint = pts[0], IsClosed = false, IsFilled = false };
        if (pts.Count == 2)
            fig.Segments.Add(new LineSegment(pts[1], true));
        else
        {
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var mid = new Point((pts[i].X + pts[i + 1].X) / 2, (pts[i].Y + pts[i + 1].Y) / 2);
                fig.Segments.Add(new QuadraticBezierSegment(pts[i], mid, true));
            }
            fig.Segments.Add(new LineSegment(pts[^1], true));
        }
        var g = new PathGeometry(new[] { fig });
        g.Freeze();
        return g;
    }

    // ── Ink visual ───────────────────────────────────────────────────────────

    /// <summary>Draws a saved stroke: smooth, at its own width (scaled with zoom), draggable.</summary>
    private void PlaceInkVisual(FreeTextAnnotation ann, double pageHeightPts)
    {
        if (ParseInk(ann.Text) is not { Points.Count: >= 1 } ink) return;
        var canvasPts = ink.Points.Select(p => new Point(p.X * Scale, (pageHeightPts - p.Y) * Scale)).ToList();
        if (canvasPts.Count == 1) canvasPts.Add(new Point(canvasPts[0].X + 0.1, canvasPts[0].Y)); // a dot

        var path = new Path
        {
            Data = SmoothGeometry(canvasPts),
            Stroke = new SolidColorBrush(ParseColor(ink.Color)),
            StrokeThickness = Math.Max(0.5, ink.Width * Scale),
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Tag = ann,
            ToolTip = "Drawing — drag with Select to move, right-click to delete",
        };

        var ctx = new ContextMenu();
        var del = new MenuItem { Header = "Delete Drawing" };
        del.Click += (_, _) => RemoveInk(ann, path);
        ctx.Items.Add(del);
        path.ContextMenu = ctx;

        MakeDraggable(path, (dxPt, dyPt) =>
        {
            if (ParseInk(ann.Text) is not { } cur) return;
            var moved = cur.Points.Select(p => new Point(p.X + dxPt, p.Y + dyPt)).ToList();
            ann.Text = EncodeInk(cur.Color, cur.Width, moved);
            SetInkBounds(ann, moved, cur.Width);
        }, ann);
        var props = new MenuItem { Header = "Properties…" };
        props.Click += (_, _) => { if (_vm != null) { _vm.SelectedGraphic = ann; ShowSelectionOutline(path); _vm.ShowPropertiesPanel(); } };
        ctx.Items.Insert(0, props);
        ctx.Items.Insert(1, new Separator());
        AnnotationCanvas.Children.Add(path);
        if (ReferenceEquals(_vm?.SelectedGraphic, ann))
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => ShowSelectionOutline(path));
    }

    private void RemoveInk(FreeTextAnnotation ann, UIElement visual)
    {
        if (_vm == null) return;
        var vm = _vm;
        vm.FreeTextAnnotations.Remove(ann);
        AnnotationCanvas.Children.Remove(visual);
        vm.PushUndo(
            undo: () => { vm.FreeTextAnnotations.Add(ann); RefreshPage(); },
            redo: () => { vm.FreeTextAnnotations.Remove(ann); RefreshPage(); });
    }

    /// <summary>Turns the stroke just drawn into a saved, smoothed, undoable drawing.</summary>
    private void FinishFreehandStroke(IReadOnlyList<Point> canvasPts)
    {
        if (_vm?.Document == null || canvasPts.Count < 1) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

        var pdfPts = canvasPts.Select(p => new Point(p.X / Scale, pageH - p.Y / Scale)).ToList();
        string color = _vm.CurrentDrawingColor ?? "#000000";
        double width = _vm.CurrentStrokeWidth > 0 ? _vm.CurrentStrokeWidth : 2.0;

        var ann = new FreeTextAnnotation
        {
            PageNumber = pageNum,
            Text = EncodeInk(color, width, pdfPts),
            FontSize = 0,
            FontFamily = "Ink",
            FontColor = color,
        };
        SetInkBounds(ann, pdfPts, width);

        var vm = _vm;
        vm.FreeTextAnnotations.Add(ann);
        PlaceInkVisual(ann, pageH);
        vm.PushUndo(
            undo: () => { vm.FreeTextAnnotations.Remove(ann); RefreshPage(); },
            redo: () => { vm.FreeTextAnnotations.Add(ann); RefreshPage(); });
        vm.StatusText = "Drawing added — drag it with the Select tool to move it, right-click to delete.";
    }

    // ── Drag to move (shapes, callouts, drawings) ─────────────────────────────

    /// <summary>
    /// With the Select tool, drag <paramref name="visual"/> anywhere on the page. On drop,
    /// <paramref name="applyMove"/> receives the move in PDF points (y up); undo moves it back.
    /// </summary>
    private void MakeDraggable(FrameworkElement visual, Action<double, double> applyMove, object? selectable = null)
    {
        Point start = default;
        bool dragging = false;
        var shift = new TranslateTransform();

        visual.MouseEnter += (_, _) =>
        {
            if (_vm?.ActiveTool == ActiveTool.Select) visual.Cursor = Cursors.SizeAll;
            else visual.ClearValue(CursorProperty);
        };
        visual.MouseLeftButtonDown += (_, e) =>
        {
            if (_vm?.ActiveTool != ActiveTool.Select) return;
            // Clicking a shape / drawing selects it for the Properties panel (colour, width, fill…).
            if (selectable != null)
            {
                _vm.SelectedGraphic = selectable;
                ShowSelectionOutline(visual);
            }
            start = e.GetPosition(AnnotationCanvas);
            dragging = true;
            visual.RenderTransform = shift;
            visual.CaptureMouse();
            e.Handled = true;
        };
        visual.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var pos = e.GetPosition(AnnotationCanvas);
            // Keep the item on the page.
            pos.X = Math.Clamp(pos.X, 0, AnnotationCanvas.Width > 0 ? AnnotationCanvas.Width : AnnotationCanvas.ActualWidth);
            pos.Y = Math.Clamp(pos.Y, 0, AnnotationCanvas.Height > 0 ? AnnotationCanvas.Height : AnnotationCanvas.ActualHeight);
            shift.X = pos.X - start.X;
            shift.Y = pos.Y - start.Y;
        };
        visual.MouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            dragging = false;
            visual.ReleaseMouseCapture();
            e.Handled = true;
            double dxPt = shift.X / Scale, dyPt = -shift.Y / Scale;   // PDF y runs up
            shift.X = shift.Y = 0;
            if (Math.Abs(dxPt) < 0.1 && Math.Abs(dyPt) < 0.1 || _vm == null) return;

            var vm = _vm;
            applyMove(dxPt, dyPt);
            vm.PushUndo(
                undo: () => { applyMove(-dxPt, -dyPt); RefreshPage(); },
                redo: () => { applyMove(dxPt, dyPt); RefreshPage(); });
            RefreshPage();
        };
    }

    /// <summary>Moves a rectangle / ellipse / arrow / callout by (dx, dy) PDF points.</summary>
    private static void MoveShape(ShapeAnnotation shape, double dxPt, double dyPt)
    {
        shape.X1 += dxPt; shape.X2 += dxPt;
        shape.Y1 += dyPt; shape.Y2 += dyPt;
    }

    // ── Selection outline (Live View shapes / drawings) ─────────────────────

    private System.Windows.Shapes.Rectangle? _selectionOutline;

    /// <summary>Dashed box around the selected shape / drawing like other PDF editors' selection.</summary>
    private void ShowSelectionOutline(FrameworkElement visual)
    {
        Rect bounds;
        try
        {
            bounds = VisualTreeHelper.GetDescendantBounds(visual);
            if (bounds.IsEmpty) bounds = new Rect(visual.RenderSize);
            bounds = visual.TransformToAncestor(AnnotationCanvas).TransformBounds(bounds);
        }
        catch { return; }   // not laid out yet
        if (_selectionOutline == null || !AnnotationCanvas.Children.Contains(_selectionOutline))
        {
            _selectionOutline = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215)), StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false,
            };
            Panel.SetZIndex(_selectionOutline, 9997);
            AnnotationCanvas.Children.Add(_selectionOutline);
        }
        bounds.Inflate(3, 3);
        Canvas.SetLeft(_selectionOutline, bounds.X);
        Canvas.SetTop(_selectionOutline, bounds.Y);
        _selectionOutline.Width = bounds.Width;
        _selectionOutline.Height = bounds.Height;
        _selectionOutline.Visibility = Visibility.Visible;
    }

    private void HideSelectionOutline()
    {
        if (_selectionOutline != null) _selectionOutline.Visibility = Visibility.Collapsed;
    }
}
