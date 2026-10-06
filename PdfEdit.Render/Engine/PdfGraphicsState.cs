using PdfEdit.Render.Drawing;

namespace PdfEdit.Render.Engine;

/// <summary>PDF graphics state, including text state sub-object.</summary>
internal sealed class PdfGraphicsState
{
    // CTM — [a b c d e f] = Matrix2D(a,b,c,d,e,f)
    public Matrix2D Ctm { get; set; } = Matrix2D.Identity;

    // Colors
    public RgbaColor StrokeColor { get; set; } = RgbaColor.Black;
    public RgbaColor FillColor   { get; set; } = RgbaColor.Black;

    // Current color spaces (a name like /DeviceRGB or an array like [/ICCBased ...]).
    // Null means the device default implied by the last color operator.
    public PdfObject? StrokeColorSpace { get; set; }
    public PdfObject? FillColorSpace   { get; set; }

    // Number of drawing-surface clip pushes active at this state level (popped on Q).
    public int ClipPushes { get; set; }

    // Line style
    public double LineWidth    { get; set; } = 1.0;
    public LineCap  LineCap  { get; set; } = LineCap.Flat;
    public LineJoin LineJoin { get; set; } = LineJoin.Miter;
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
            StrokeColorSpace = StrokeColorSpace,
            FillColorSpace   = FillColorSpace,
            ClipPushes   = ClipPushes,
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
    public Matrix2D Tm  { get; set; } = Matrix2D.Identity;
    public Matrix2D Tlm { get; set; } = Matrix2D.Identity;

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
