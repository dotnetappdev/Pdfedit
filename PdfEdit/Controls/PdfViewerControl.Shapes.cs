using System.Globalization;
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
/// Acrobat drawing and Pro measuring tools in the live view:
/// <list type="bullet">
/// <item>Line and Cloud (drag), Polygon and Polyline (click the points; double-click or Enter
/// finishes, Esc cancels).</item>
/// <item>Measure: Distance (drag), Perimeter and Area (click the points) with a live label in
/// in / mm / cm / pt.</item>
/// <item>Every shape and drawing has Acrobat's properties on right-click — colour, line width,
/// fill, opacity — and boxes get a corner handle to resize them.</item>
/// </list>
/// </summary>
public partial class PdfViewerControl
{
    private static bool IsPolyTool(ActiveTool t) => t is ActiveTool.DrawPolygon or ActiveTool.DrawPolyline
        or ActiveTool.MeasurePerimeter or ActiveTool.MeasureArea;

    private static bool IsDragLineTool(ActiveTool t) => t is ActiveTool.DrawLine or ActiveTool.MeasureDistance;

    // ── Measurement text ─────────────────────────────────────────────────────

    private static double PointsPerUnit(string unit) => unit switch
    {
        "mm" => 72.0 / 25.4,
        "cm" => 72.0 / 2.54,
        "pt" => 1.0,
        _    => 72.0,   // in
    };

    private static string FormatLength(double pts, string unit) =>
        (pts / PointsPerUnit(unit)).ToString(unit == "pt" ? "0" : "0.00", CultureInfo.CurrentCulture) + " " + unit;

    private static string FormatArea(double sqPts, string unit)
    {
        double k = PointsPerUnit(unit);
        return (sqPts / (k * k)).ToString(unit == "pt" ? "0" : "0.00", CultureInfo.CurrentCulture) + " sq " + unit;
    }

    private static double PolyLength(IReadOnlyList<Point> pts, bool closed)
    {
        double len = 0;
        for (int i = 1; i < pts.Count; i++) len += (pts[i] - pts[i - 1]).Length;
        if (closed && pts.Count > 2) len += (pts[0] - pts[^1]).Length;
        return len;
    }

    private static double PolyArea(IReadOnlyList<Point> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i]; var q = pts[(i + 1) % pts.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return Math.Abs(a) / 2;
    }

    /// <summary>The label a measurement shows (also written to the saved annotation).</summary>
    public static string MeasureLabel(ShapeAnnotation s) => s.Kind switch
    {
        ShapeKind.Distance  => FormatLength((new Point(s.X2, s.Y2) - new Point(s.X1, s.Y1)).Length, s.MeasureUnit),
        ShapeKind.Perimeter => FormatLength(PolyLength(s.Points ?? new(), closed: false), s.MeasureUnit),
        ShapeKind.Area      => FormatArea(PolyArea(s.Points ?? new()), s.MeasureUnit),
        _ => string.Empty,
    };

    // ── Cloud (Acrobat revision cloud): scalloped rectangle ──────────────────

    private static Geometry CloudGeometry(Rect r, double scallop)
    {
        var fig = new PathFigure { StartPoint = r.TopLeft, IsClosed = true };
        void Edge(Point a, Point b)
        {
            double len = (b - a).Length;
            int n = Math.Max(1, (int)Math.Round(len / scallop));
            var step = (b - a) / n;
            double radius = step.Length / 2;
            for (int i = 1; i <= n; i++)
                fig.Segments.Add(new ArcSegment(a + step * i, new Size(radius, radius), 0, false,
                    SweepDirection.Clockwise, true));
        }
        Edge(r.TopLeft, r.TopRight);
        Edge(r.TopRight, r.BottomRight);
        Edge(r.BottomRight, r.BottomLeft);
        Edge(r.BottomLeft, r.TopLeft);
        return new PathGeometry(new[] { fig });
    }

    // ── Shared brushes ───────────────────────────────────────────────────────

    private Brush ShapeFill(ShapeAnnotation shape)
    {
        var stroke = ParseColor(shape.StrokeColor);
        if (shape.FillColor == "") return Brushes.Transparent;
        if (!string.IsNullOrEmpty(shape.FillColor)) return new SolidColorBrush(ParseColor(shape.FillColor));
        return new SolidColorBrush(Color.FromArgb(30, stroke.R, stroke.G, stroke.B));
    }

    // ── Visuals for the new kinds ────────────────────────────────────────────

    /// <summary>Line, Cloud, Polygon, Polyline and the Measure shapes. Returns false for other kinds.</summary>
    private bool TryPlaceExtendedShape(ShapeAnnotation shape, double pageH)
    {
        Point ToCanvas(double x, double y) => new(x * Scale, (pageH - y) * Scale);
        var stroke = new SolidColorBrush(ParseColor(shape.StrokeColor));
        double sw = Math.Max(0.5, shape.LineWidth * Scale);
        FrameworkElement visual;

        switch (shape.Kind)
        {
            case ShapeKind.Line:
            {
                var a = ToCanvas(shape.X1, shape.Y1); var b = ToCanvas(shape.X2, shape.Y2);
                visual = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = stroke, StrokeThickness = sw,
                                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                break;
            }
            case ShapeKind.Cloud:
            {
                var r = new Rect(ToCanvas(shape.X1, shape.Y2), ToCanvas(shape.X2, shape.Y1));
                visual = new Path { Data = CloudGeometry(r, Math.Max(8, 14 * Scale)),
                                    Stroke = stroke, StrokeThickness = sw, Fill = ShapeFill(shape) };
                break;
            }
            case ShapeKind.Polygon or ShapeKind.Polyline:
            {
                var pts = new PointCollection((shape.Points ?? new()).Select(p => ToCanvas(p.X, p.Y)));
                visual = shape.Kind == ShapeKind.Polygon
                    ? new Polygon { Points = pts, Stroke = stroke, StrokeThickness = sw, Fill = ShapeFill(shape), StrokeLineJoin = PenLineJoin.Round }
                    : new Polyline { Points = pts, Stroke = stroke, StrokeThickness = sw, StrokeLineJoin = PenLineJoin.Round,
                                     StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                break;
            }
            case ShapeKind.Distance or ShapeKind.Perimeter or ShapeKind.Area:
                visual = BuildMeasureVisual(shape, pageH, stroke, sw);
                break;
            default:
                return false;
        }

        visual.Tag = shape;
        visual.Opacity = shape.Opacity;
        visual.ToolTip = shape.Kind is ShapeKind.Distance or ShapeKind.Perimeter or ShapeKind.Area
            ? $"{shape.Kind}: {MeasureLabel(shape)} — drag with Select to move, right-click for properties"
            : $"{shape.Kind} — drag with Select to move, right-click for properties";
        visual.ContextMenu = BuildShapeMenu(shape, visual);
        MakeDraggable(visual, (dx, dy) => MoveShape(shape, dx, dy), shape);
        if (shape.IsBoxShape) AttachShapeResize(shape, visual, pageH);
        AnnotationCanvas.Children.Add(visual);
        if (ReferenceEquals(_vm?.SelectedGraphic, shape))
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => ShowSelectionOutline(visual));
        return true;
    }

    /// <summary>Measurement: the line / outline plus its label (and dimension ticks for distance).</summary>
    private Canvas BuildMeasureVisual(ShapeAnnotation shape, double pageH, Brush stroke, double sw)
    {
        Point ToCanvas(double x, double y) => new(x * Scale, (pageH - y) * Scale);
        var host = new Canvas { Background = null };
        Point labelAt;

        if (shape.Kind == ShapeKind.Distance)
        {
            var a = ToCanvas(shape.X1, shape.Y1); var b = ToCanvas(shape.X2, shape.Y2);
            host.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = stroke, StrokeThickness = sw });
            // Perpendicular end ticks, like Acrobat's dimension line
            var dir = b - a; if (dir.Length > 0) dir.Normalize();
            var n = new Vector(-dir.Y, dir.X) * 6;
            foreach (var p in new[] { a, b })
                host.Children.Add(new Line { X1 = (p + n).X, Y1 = (p + n).Y, X2 = (p - n).X, Y2 = (p - n).Y, Stroke = stroke, StrokeThickness = sw });
            labelAt = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        }
        else
        {
            var pts = new PointCollection((shape.Points ?? new()).Select(p => ToCanvas(p.X, p.Y)));
            if (shape.Kind == ShapeKind.Area)
                host.Children.Add(new Polygon { Points = pts, Stroke = stroke, StrokeThickness = sw, Fill = ShapeFill(shape) });
            else
                host.Children.Add(new Polyline { Points = pts, Stroke = stroke, StrokeThickness = sw });
            labelAt = pts.Count == 0 ? default
                : shape.Kind == ShapeKind.Area
                    ? new Point(pts.Average(p => p.X), pts.Average(p => p.Y))
                    : pts[^1];
        }

        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            BorderBrush = stroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock { Text = MeasureLabel(shape), Foreground = stroke, FontSize = 11, FontWeight = FontWeights.SemiBold },
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, labelAt.X - label.DesiredSize.Width / 2);
        Canvas.SetTop(label, labelAt.Y - label.DesiredSize.Height - 4);
        host.Children.Add(label);
        return host;
    }

    // ── Properties (right-click) ─────────────────────────────────────────────

    private static readonly (string Hex, string Name)[] ShapeColors =
    {
        ("#C62828", "Red"), ("#000000", "Black"), ("#1565C0", "Blue"), ("#2E7D32", "Green"),
        ("#F9A825", "Yellow"), ("#6A1B9A", "Purple"), ("#757575", "Grey"), ("#FFFFFF", "White"),
    };

    /// <summary>Acrobat's shape properties: colour, line width, fill, opacity, delete. All undoable.</summary>
    private ContextMenu BuildShapeMenu(ShapeAnnotation shape, UIElement visual)
    {
        var menu = new ContextMenu();
        var properties = new MenuItem { Header = "Properties…", FontWeight = FontWeights.SemiBold };
        properties.Click += (_, _) =>
        {
            if (_vm == null) return;
            _vm.SelectedGraphic = shape;
            if (visual is FrameworkElement fe) ShowSelectionOutline(fe);
            _vm.ShowPropertiesPanel();
        };
        menu.Items.Add(properties);
        menu.Items.Add(new Separator());

        void Edit(string what, Action<ShapeAnnotation> change)
        {
            if (_vm == null) return;
            var before = Clone(shape);
            change(shape);
            var after = Clone(shape);
            var vm = _vm;
            vm.PushUndo(
                undo: () => { CopyInto(before, shape); RefreshPage(); },
                redo: () => { CopyInto(after, shape); RefreshPage(); });
            vm.StatusText = $"{shape.Kind}: {what} changed.";
            RefreshPage();
        }

        var colour = new MenuItem { Header = "Colour" };
        foreach (var (hex, name) in ShapeColors)
        {
            var item = new MenuItem { Header = name, Icon = Swatch(hex), IsChecked = string.Equals(shape.StrokeColor, hex, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => Edit("colour", s => s.StrokeColor = hex);
            colour.Items.Add(item);
        }
        menu.Items.Add(colour);

        var width = new MenuItem { Header = "Line width" };
        foreach (var w in new[] { 0.5, 1, 2, 3, 4, 6, 8 })
        {
            var item = new MenuItem { Header = $"{w:0.#} pt", IsChecked = Math.Abs(shape.LineWidth - w) < 0.01 };
            item.Click += (_, _) => Edit("line width", s => s.LineWidth = w);
            width.Items.Add(item);
        }
        menu.Items.Add(width);

        if (shape.IsClosedShape)
        {
            var fill = new MenuItem { Header = "Fill" };
            var none = new MenuItem { Header = "No fill", IsChecked = shape.FillColor == "" };
            none.Click += (_, _) => Edit("fill", s => s.FillColor = "");
            var tint = new MenuItem { Header = "Light tint of line colour", IsChecked = shape.FillColor == null };
            tint.Click += (_, _) => Edit("fill", s => s.FillColor = null);
            fill.Items.Add(none);
            fill.Items.Add(tint);
            foreach (var (hex, name) in ShapeColors)
            {
                var item = new MenuItem { Header = name, Icon = Swatch(hex), IsChecked = string.Equals(shape.FillColor, hex, StringComparison.OrdinalIgnoreCase) };
                item.Click += (_, _) => Edit("fill", s => s.FillColor = hex);
                fill.Items.Add(item);
            }
            menu.Items.Add(fill);
        }

        var opacity = new MenuItem { Header = "Opacity" };
        foreach (var o in new[] { 100, 75, 50, 25 })
        {
            var item = new MenuItem { Header = $"{o}%", IsChecked = Math.Abs(shape.Opacity * 100 - o) < 1 };
            item.Click += (_, _) => Edit("opacity", s => s.Opacity = o / 100.0);
            opacity.Items.Add(item);
        }
        menu.Items.Add(opacity);

        if (shape.Kind is ShapeKind.Distance or ShapeKind.Perimeter or ShapeKind.Area)
        {
            var units = new MenuItem { Header = "Units" };
            foreach (var u in new[] { "in", "mm", "cm", "pt" })
            {
                var item = new MenuItem { Header = u, IsChecked = shape.MeasureUnit == u };
                item.Click += (_, _) => Edit("units", s => s.MeasureUnit = u);
                units.Items.Add(item);
            }
            menu.Items.Add(units);
        }

        menu.Items.Add(new Separator());
        var del = new MenuItem { Header = $"Delete {shape.Kind}" };
        del.Click += (_, _) => { AnnotationCanvas.Children.Remove(visual); _vm?.RemoveShapeAnnotation(shape); };
        menu.Items.Add(del);
        return menu;
    }

    private static UIElement Swatch(string hex) => new Border
    {
        Width = 12, Height = 12, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
    };

    private static ShapeAnnotation Clone(ShapeAnnotation s) => new()
    {
        PageNumber = s.PageNumber, X1 = s.X1, Y1 = s.Y1, X2 = s.X2, Y2 = s.Y2, Kind = s.Kind,
        StrokeColor = s.StrokeColor, LineWidth = s.LineWidth, FillColor = s.FillColor, CalloutText = s.CalloutText,
        Opacity = s.Opacity, Points = s.Points?.ToList(), MeasureUnit = s.MeasureUnit,
    };

    private static void CopyInto(ShapeAnnotation from, ShapeAnnotation to)
    {
        to.X1 = from.X1; to.Y1 = from.Y1; to.X2 = from.X2; to.Y2 = from.Y2;
        to.StrokeColor = from.StrokeColor; to.LineWidth = from.LineWidth; to.FillColor = from.FillColor;
        to.CalloutText = from.CalloutText; to.Opacity = from.Opacity; to.Points = from.Points?.ToList();
        to.MeasureUnit = from.MeasureUnit;
    }

    // ── Resize (boxes: rectangle, ellipse, cloud, callout) ───────────────────

    private Thumb? _shapeResizeGrip;
    private ShapeAnnotation? _resizeShape;

    /// <summary>With Select, hovering a box shape shows a corner handle; drag it to resize.</summary>
    private void AttachShapeResize(ShapeAnnotation shape, FrameworkElement visual, double pageH)
    {
        visual.MouseEnter += (_, _) =>
        {
            if (_vm?.ActiveTool != ActiveTool.Select) return;
            EnsureShapeResizeGrip();
            _resizeShape = shape;
            Canvas.SetLeft(_shapeResizeGrip!, shape.X2 * Scale - 5);
            Canvas.SetTop(_shapeResizeGrip!, (pageH - shape.Y1) * Scale - 5);
            _shapeResizeGrip!.Visibility = Visibility.Visible;
        };
    }

    private void EnsureShapeResizeGrip()
    {
        if (_shapeResizeGrip != null && AnnotationCanvas.Children.Contains(_shapeResizeGrip)) return;
        _shapeResizeGrip = new Thumb
        {
            Width = 10, Height = 10, Cursor = Cursors.SizeNWSE, Visibility = Visibility.Collapsed,
            ToolTip = "Drag to resize",
            Template = GripTemplate(isMove: false),
        };
        System.Windows.Automation.AutomationProperties.SetName(_shapeResizeGrip, "Resize shape");
        Panel.SetZIndex(_shapeResizeGrip, 9998);
        double dxTotal = 0, dyTotal = 0;
        _shapeResizeGrip.DragStarted += (_, _) => { dxTotal = dyTotal = 0; };
        _shapeResizeGrip.DragDelta += (_, e) =>
        {
            dxTotal += e.HorizontalChange; dyTotal += e.VerticalChange;
            Canvas.SetLeft(_shapeResizeGrip, Canvas.GetLeft(_shapeResizeGrip) + e.HorizontalChange);
            Canvas.SetTop(_shapeResizeGrip, Canvas.GetTop(_shapeResizeGrip) + e.VerticalChange);
        };
        _shapeResizeGrip.DragCompleted += (_, _) =>
        {
            if (_resizeShape is not { } s || _vm == null || (Math.Abs(dxTotal) < 1 && Math.Abs(dyTotal) < 1)) return;
            var before = Clone(s);
            // Bottom-right corner: right edge X2, bottom edge Y1 (PDF y runs up)
            s.X2 = Math.Max(s.X1 + 4, s.X2 + dxTotal / Scale);
            s.Y1 = Math.Min(s.Y2 - 4, s.Y1 - dyTotal / Scale);
            var after = Clone(s);
            var vm = _vm;
            vm.PushUndo(
                undo: () => { CopyInto(before, s); RefreshPage(); },
                redo: () => { CopyInto(after, s); RefreshPage(); });
            RefreshPage();
        };
        AnnotationCanvas.Children.Add(_shapeResizeGrip);
    }

    // ── Polygon / polyline / perimeter / area drawing ────────────────────────

    private readonly List<Point> _polyPts = new();     // canvas points placed so far
    private Polyline? _polyPreview;
    private Border? _polyLabel;

    /// <summary>Click adds a point; double-click finishes. Returns true when handled.</summary>
    private bool HandlePolyClick(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(AnnotationCanvas);
        if (!IsOnPage(pos)) return false;
        if (_polyPreview == null)
        {
            var color = ParseColor(_vm?.CurrentDrawingColor ?? "#C62828");
            _polyPreview = new Polyline
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = Math.Max(1, (_vm?.CurrentStrokeWidth ?? 2) * Scale),
                StrokeDashArray = new DoubleCollection { 4, 2 },
                IsHitTestVisible = false,
            };
            AnnotationCanvas.Children.Add(_polyPreview);
            _polyPts.Clear();
        }

        if (e.ClickCount >= 2)
        {
            FinishPoly();
            return true;
        }
        _polyPts.Add(pos);
        UpdatePolyPreview(pos);
        if (_vm != null)
            _vm.StatusText = "Click to add points — double-click or Enter to finish, Esc to cancel.";
        Focus();
        return true;
    }

    private void UpdatePolyPreview(Point mouse)
    {
        if (_polyPreview == null || _vm == null) return;
        var pts = new PointCollection(_polyPts) { mouse };
        if (_vm.ActiveTool is ActiveTool.DrawPolygon or ActiveTool.MeasureArea && pts.Count > 2) pts.Add(_polyPts[0]);
        _polyPreview.Points = pts;

        // Live measurement while placing points
        if (_vm.ActiveTool is ActiveTool.MeasurePerimeter or ActiveTool.MeasureArea && _vm.Document != null)
        {
            var all = _polyPts.Append(mouse).Select(p => new Point(p.X / Scale, -p.Y / Scale)).ToList();
            string text = _vm.ActiveTool == ActiveTool.MeasureArea
                ? FormatArea(PolyArea(all), _vm.MeasureUnit)
                : FormatLength(PolyLength(all, closed: false), _vm.MeasureUnit);
            if (_polyLabel == null)
            {
                _polyLabel = new Border
                {
                    Background = Brushes.White, BorderBrush = _polyPreview.Stroke, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 1, 4, 1), IsHitTestVisible = false,
                    Child = new TextBlock { FontSize = 11, Foreground = _polyPreview.Stroke },
                };
                AnnotationCanvas.Children.Add(_polyLabel);
            }
            ((TextBlock)_polyLabel.Child).Text = text;
            Canvas.SetLeft(_polyLabel, mouse.X + 12);
            Canvas.SetTop(_polyLabel, mouse.Y + 12);
        }
    }

    private void CancelPoly()
    {
        if (_polyPreview != null) AnnotationCanvas.Children.Remove(_polyPreview);
        if (_polyLabel != null) AnnotationCanvas.Children.Remove(_polyLabel);
        _polyPreview = null;
        _polyLabel = null;
        _polyPts.Clear();
    }

    private void FinishPoly()
    {
        var tool = _vm?.ActiveTool;
        var pts = _polyPts.ToList();
        CancelPoly();
        if (_vm?.Document == null || tool is not { } t) return;
        // A double-click also fired a single click at the same spot: drop that duplicate.
        while (pts.Count >= 2 && (pts[^1] - pts[^2]).Length < 3) pts.RemoveAt(pts.Count - 1);
        bool closed = t is ActiveTool.DrawPolygon or ActiveTool.MeasureArea;
        if (pts.Count < (closed ? 3 : 2)) { _vm.StatusText = "Not enough points — shape cancelled."; return; }

        double pageH = _vm.Document.PageSizes[_vm.CurrentPageIndex].Height;
        var pdfPts = pts.Select(p => new Point(p.X / Scale, pageH - p.Y / Scale)).ToList();
        var shape = new ShapeAnnotation
        {
            Kind = t switch
            {
                ActiveTool.DrawPolygon => ShapeKind.Polygon,
                ActiveTool.DrawPolyline => ShapeKind.Polyline,
                ActiveTool.MeasurePerimeter => ShapeKind.Perimeter,
                _ => ShapeKind.Area,
            },
            Points = pdfPts,
            X1 = pdfPts.Min(p => p.X), Y1 = pdfPts.Min(p => p.Y),
            X2 = pdfPts.Max(p => p.X), Y2 = pdfPts.Max(p => p.Y),
            StrokeColor = _vm.CurrentDrawingColor ?? "#C62828",
            FillColor = string.IsNullOrEmpty(_vm.CurrentFillColor) ? null : _vm.CurrentFillColor,
            LineWidth = _vm.CurrentStrokeWidth > 0 ? _vm.CurrentStrokeWidth : 2,
            MeasureUnit = _vm.MeasureUnit,
        };
        _vm.AddShapeAnnotation(shape);
        TryPlaceExtendedShape(shape, pageH);
        _vm.StatusText = shape.Kind is ShapeKind.Perimeter or ShapeKind.Area
            ? $"{shape.Kind}: {MeasureLabel(shape)}"
            : $"{shape.Kind} added — right-click for properties.";
    }

    /// <summary>Enter finishes and Esc cancels a polygon / polyline / measurement in progress.</summary>
    private bool HandlePolyKey(KeyEventArgs e)
    {
        if (_polyPreview == null) return false;
        if (e.Key == Key.Enter) { FinishPoly(); return true; }
        if (e.Key == Key.Escape) { CancelPoly(); if (_vm != null) _vm.StatusText = "Cancelled."; return true; }
        if (e.Key == Key.Back && _polyPts.Count > 0)   // remove the last point, like Acrobat
        {
            _polyPts.RemoveAt(_polyPts.Count - 1);
            if (_polyPts.Count == 0) CancelPoly();
            else UpdatePolyPreview(_polyPts[^1]);
            return true;
        }
        return false;
    }
}
