namespace PdfEdit.Render.Drawing;

public enum PathSegmentKind { Line, Bezier }

/// <summary>A straight line to P1, or a cubic Bézier through control points P1, P2 to P3.</summary>
public readonly record struct PathSegment(PathSegmentKind Kind, Point2D P1, Point2D P2, Point2D P3, bool IsStroked = true);

public sealed class PathFigure
{
    public PathFigure(Point2D start, bool isFilled, bool isClosed) { Start = start; IsFilled = isFilled; IsClosed = isClosed; }
    public Point2D Start { get; }
    public bool IsFilled { get; }
    public bool IsClosed { get; }
    public List<PathSegment> Segments { get; } = new();
}

/// <summary>A vector path in device pixels: one or more figures and a fill rule.</summary>
public sealed class PathData
{
    public List<PathFigure> Figures { get; } = new();
    public bool EvenOdd { get; set; }

    private PathFigure? Current => Figures.Count > 0 ? Figures[^1] : null;

    public void BeginFigure(Point2D start, bool isFilled = true, bool isClosed = false) => Figures.Add(new PathFigure(start, isFilled, isClosed));

    public void LineTo(Point2D p, bool isStroked = true) =>
        (Current ?? throw new InvalidOperationException("LineTo before a figure was started."))
            .Segments.Add(new PathSegment(PathSegmentKind.Line, p, p, p, isStroked));

    public void BezierTo(Point2D p1, Point2D p2, Point2D p3, bool isStroked = true) =>
        (Current ?? throw new InvalidOperationException("BezierTo before a figure was started."))
            .Segments.Add(new PathSegment(PathSegmentKind.Bezier, p1, p2, p3, isStroked));

    public static PathData Rectangle(Point2D a, Point2D b, Point2D c, Point2D d)
    {
        var p = new PathData();
        p.BeginFigure(a, true, true);
        p.LineTo(b, false); p.LineTo(c, false); p.LineTo(d, false);
        return p;
    }
}
