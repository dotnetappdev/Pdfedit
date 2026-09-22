namespace PdfEdit.Models;

public enum ShapeKind { Rectangle, Ellipse, Arrow, Callout }

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
}
