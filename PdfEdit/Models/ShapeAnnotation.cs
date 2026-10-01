namespace PdfEdit.Models;

// New kinds are appended so saved document state (stored by number) keeps its meaning.
public enum ShapeKind
{
    Rectangle, Ellipse, Arrow, Callout,
    // Acrobat "Drawing" tools
    Line, Cloud, Polygon, Polyline,
    // Acrobat Pro "Measure" tools — drawn with a live measurement label
    Distance, Perimeter, Area,
}

public class ShapeAnnotation
{
    public int       PageNumber  { get; set; }
    public double    X1          { get; set; }  // PDF coords (bottom-left origin)
    public double    Y1          { get; set; }
    public double    X2          { get; set; }
    public double    Y2          { get; set; }
    public ShapeKind Kind        { get; set; }
    public string    StrokeColor { get; set; } = "#C62828";
    public double    LineWidth   { get; set; } = 2.0;
    // null = auto semi-transparent fill; "" = no fill (transparent); "#RRGGBB" = solid fill
    public string?   FillColor   { get; set; } = null;
    // Callout text (used when Kind == Callout)
    public string    CalloutText { get; set; } = "";
    // 0–1 (Acrobat's opacity slider)
    public double    Opacity     { get; set; } = 1.0;
    // Vertices in PDF points for Polygon / Polyline / Perimeter / Area (X1..Y2 hold their bounds)
    public List<System.Windows.Point>? Points { get; set; }
    // Unit the measurement label is shown in: "in", "mm", "cm" or "pt"
    public string    MeasureUnit { get; set; } = "in";

    public bool IsClosedShape => Kind is ShapeKind.Rectangle or ShapeKind.Ellipse or ShapeKind.Cloud
        or ShapeKind.Polygon or ShapeKind.Area or ShapeKind.Callout;
    public bool IsBoxShape => Kind is ShapeKind.Rectangle or ShapeKind.Ellipse or ShapeKind.Cloud or ShapeKind.Callout;
    public bool IsPolyShape => Kind is ShapeKind.Polygon or ShapeKind.Polyline or ShapeKind.Perimeter or ShapeKind.Area;
}
