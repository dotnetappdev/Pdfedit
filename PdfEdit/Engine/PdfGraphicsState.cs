using System.Windows.Media;

namespace PdfEdit.Engine;

/// <summary>PDF graphics state, including text state sub-object.</summary>
internal sealed class PdfGraphicsState
{
    // CTM — column-major [a b c d e f] = WPF Matrix(a,b,c,d,e,f)
    public Matrix Ctm { get; set; } = Matrix.Identity;

    // Colors — BGRA stored as WPF Color
    public Color StrokeColor { get; set; } = Colors.Black;
    public Color FillColor   { get; set; } = Colors.Black;

    // Line style
    public double LineWidth    { get; set; } = 1.0;
    public PenLineCap  LineCap  { get; set; } = PenLineCap.Flat;
    public PenLineJoin LineJoin { get; set; } = PenLineJoin.Miter;
    public double MiterLimit   { get; set; } = 10.0;

    // Dash pattern
    public double[]? DashArray  { get; set; }
    public double    DashPhase  { get; set; }

    // Fill rule
    public bool EvenOddFill { get; set; } = false;

    // Text state
    public TextState Text { get; } = new();

    // Alpha (transparency)
    public double AlphaStroke { get; set; } = 1.0;
    public double AlphaFill   { get; set; } = 1.0;

    public PdfGraphicsState Clone()
    {
        var c = new PdfGraphicsState
        {
            Ctm          = Ctm,
            StrokeColor  = StrokeColor,
            FillColor    = FillColor,
            LineWidth    = LineWidth,
            LineCap      = LineCap,
            LineJoin     = LineJoin,
            MiterLimit   = MiterLimit,
            DashArray    = DashArray?.ToArray(),
            DashPhase    = DashPhase,
            EvenOddFill  = EvenOddFill,
            AlphaStroke  = AlphaStroke,
            AlphaFill    = AlphaFill,
        };
        Text.CopyTo(c.Text);
        return c;
    }
}

internal sealed class TextState
{
    public double CharSpacing  { get; set; } = 0;
    public double WordSpacing  { get; set; } = 0;
    public double HorizScale   { get; set; } = 100;
    public double Leading      { get; set; } = 0;
    public string FontName     { get; set; } = "Helvetica";
    public double FontSize     { get; set; } = 12;
    public int    RenderMode   { get; set; } = 0;  // 0=fill, 1=stroke, 2=fill+stroke, 3=invisible
    public double Rise         { get; set; } = 0;

    // Text matrix and text-line matrix (set by BT/Td/TD/Tm/T*)
    public Matrix Tm  { get; set; } = Matrix.Identity;
    public Matrix Tlm { get; set; } = Matrix.Identity;

    public void CopyTo(TextState t)
    {
        t.CharSpacing = CharSpacing;
        t.WordSpacing = WordSpacing;
        t.HorizScale  = HorizScale;
        t.Leading     = Leading;
        t.FontName    = FontName;
        t.FontSize    = FontSize;
        t.RenderMode  = RenderMode;
        t.Rise        = Rise;
        t.Tm          = Tm;
        t.Tlm         = Tlm;
    }
}
