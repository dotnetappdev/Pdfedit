using System.Globalization;
using System.Xml.Linq;
using PdfEdit.Models;

namespace PdfEdit.Services;

public static class XfdfService
{
    private static readonly XNamespace Ns = "http://ns.adobe.com/xfdf/";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── Export ────────────────────────────────────────────────────────────────

    public static void Export(
        string outputPath,
        string pdfFileName,
        IEnumerable<HighlightAnnotation>  highlights,
        IEnumerable<StickyNoteAnnotation> stickyNotes,
        IEnumerable<FreeTextAnnotation>   freeTexts,
        IEnumerable<ShapeAnnotation>      shapes)
    {
        var annotsEl = new XElement(Ns + "annots");

        foreach (var hl in highlights)
        {
            string tag = hl.Kind switch
            {
                HighlightKind.Underline     => "underline",
                HighlightKind.Strikethrough => "strikeout",
                _                           => "highlight",
            };
            annotsEl.Add(new XElement(Ns + tag,
                new XAttribute("page",    hl.PageNumber - 1),
                new XAttribute("rect",    Rect4(hl.Left, hl.Bottom, hl.Left + hl.Width, hl.Bottom + hl.Height)),
                new XAttribute("color",   NormColour(hl.Color)),
                new XAttribute("opacity", hl.Opacity.ToString("F2", Inv))));
        }

        foreach (var sn in stickyNotes)
        {
            var el = new XElement(Ns + "text",
                new XAttribute("page",   sn.PageNumber - 1),
                new XAttribute("rect",   Rect4(sn.Left, sn.Bottom, sn.Left + 16, sn.Bottom + 16)),
                new XAttribute("color",  NormColour(sn.Color)),
                new XAttribute("open",   "no"),
                new XAttribute("icon",   "Note"),
                new XAttribute("author", sn.Author));
            el.Add(new XElement(Ns + "contents", sn.Text));
            annotsEl.Add(el);
        }

        foreach (var ft in freeTexts)
        {
            var el = new XElement(Ns + "freetext",
                new XAttribute("page", ft.PageNumber - 1),
                new XAttribute("rect", Rect4(ft.Left, ft.Bottom, ft.Left + ft.Width, ft.Bottom + ft.Height)));
            el.Add(new XElement(Ns + "contents", ft.Text));
            el.Add(new XElement(Ns + "defaultappearance",
                $"/{ft.FontFamily} {ft.FontSize:F0} Tf {HexToRgbOp(ft.FontColor)}"));
            annotsEl.Add(el);
        }

        foreach (var sh in shapes)
        {
            double left   = Math.Min(sh.X1, sh.X2);
            double bottom = Math.Min(sh.Y1, sh.Y2);
            double right  = Math.Max(sh.X1, sh.X2);
            double top    = Math.Max(sh.Y1, sh.Y2);

            string tag = sh.Kind switch
            {
                ShapeKind.Rectangle => "square",
                ShapeKind.Ellipse   => "circle",
                _                   => "line",
            };
            var el = new XElement(Ns + tag,
                new XAttribute("page",  sh.PageNumber - 1),
                new XAttribute("rect",  Rect4(left, bottom, right, top)),
                new XAttribute("color", NormColour(sh.StrokeColor)),
                new XAttribute("width", sh.LineWidth.ToString("F1", Inv)));

            if (sh.Kind == ShapeKind.Arrow)
                el.Add(new XElement(Ns + "lpts",
                    $"{sh.X1.ToString("F2", Inv)},{sh.Y1.ToString("F2", Inv)},{sh.X2.ToString("F2", Inv)},{sh.Y2.ToString("F2", Inv)}"));

            annotsEl.Add(el);
        }

        var xdoc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "xfdf",
                new XAttribute("xml:space", "preserve"),
                annotsEl,
                new XElement(Ns + "f",
                    new XAttribute("href", System.IO.Path.GetFileName(pdfFileName)))));

        xdoc.Save(outputPath);
    }

    // ── Import ────────────────────────────────────────────────────────────────

    public static (
        List<HighlightAnnotation>  Highlights,
        List<StickyNoteAnnotation> StickyNotes,
        List<FreeTextAnnotation>   FreeTexts,
        List<ShapeAnnotation>      Shapes
    ) Import(string xfdfPath)
    {
        var highlights  = new List<HighlightAnnotation>();
        var stickyNotes = new List<StickyNoteAnnotation>();
        var freeTexts   = new List<FreeTextAnnotation>();
        var shapes      = new List<ShapeAnnotation>();

        var xdoc   = XDocument.Load(xfdfPath);
        var annots = xdoc.Descendants(Ns + "annots").FirstOrDefault();
        if (annots == null) return (highlights, stickyNotes, freeTexts, shapes);

        foreach (var el in annots.Elements())
        {
            int page     = (int)F(el.Attribute("page")?.Value ?? "0") + 1;
            float[]? r   = ParseRect(el.Attribute("rect")?.Value);
            if (r == null) continue;
            string colour = el.Attribute("color")?.Value ?? "#FFFF00";

            switch (el.Name.LocalName.ToLowerInvariant())
            {
                case "highlight":
                    highlights.Add(MakeHighlight(page, r, colour, el, HighlightKind.Highlight));
                    break;
                case "underline":
                    highlights.Add(MakeHighlight(page, r, colour, el, HighlightKind.Underline));
                    break;
                case "strikeout":
                    highlights.Add(MakeHighlight(page, r, colour, el, HighlightKind.Strikethrough));
                    break;
                case "text":
                    stickyNotes.Add(new StickyNoteAnnotation
                    {
                        PageNumber = page,
                        Left       = r[0],
                        Bottom     = r[1],
                        Color      = colour,
                        Author     = el.Attribute("author")?.Value ?? string.Empty,
                        Text       = el.Element(Ns + "contents")?.Value ?? string.Empty,
                    });
                    break;
                case "freetext":
                    freeTexts.Add(new FreeTextAnnotation
                    {
                        PageNumber = page,
                        Left       = r[0],
                        Bottom     = r[1],
                        Width      = r[2] - r[0],
                        Height     = r[3] - r[1],
                        Text       = el.Element(Ns + "contents")?.Value ?? string.Empty,
                    });
                    break;
                case "square":
                    shapes.Add(MakeShape(page, r, colour, el, ShapeKind.Rectangle));
                    break;
                case "circle":
                    shapes.Add(MakeShape(page, r, colour, el, ShapeKind.Ellipse));
                    break;
                case "line":
                {
                    var lpts = el.Element(Ns + "lpts")?.Value;
                    double x1 = r[0], y1 = r[1], x2 = r[2], y2 = r[3];
                    if (lpts != null)
                    {
                        var pts = ParseRect(lpts);
                        if (pts != null && pts.Length >= 4) { x1 = pts[0]; y1 = pts[1]; x2 = pts[2]; y2 = pts[3]; }
                    }
                    shapes.Add(new ShapeAnnotation
                    {
                        PageNumber  = page,
                        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                        Kind        = ShapeKind.Arrow,
                        StrokeColor = colour,
                        LineWidth   = D(el.Attribute("width")?.Value ?? "2"),
                    });
                    break;
                }
            }
        }

        return (highlights, stickyNotes, freeTexts, shapes);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HighlightAnnotation MakeHighlight(int page, float[] r, string colour, XElement el, HighlightKind kind) =>
        new()
        {
            PageNumber = page,
            Left       = r[0], Bottom = r[1],
            Width      = r[2] - r[0], Height = r[3] - r[1],
            Color      = colour,
            Kind       = kind,
            Opacity    = F(el.Attribute("opacity")?.Value ?? "0.4"),
        };

    private static ShapeAnnotation MakeShape(int page, float[] r, string colour, XElement el, ShapeKind kind) =>
        new()
        {
            PageNumber  = page,
            X1 = r[0], Y1 = r[1], X2 = r[2], Y2 = r[3],
            Kind        = kind,
            StrokeColor = colour,
            LineWidth   = D(el.Attribute("width")?.Value ?? "2"),
        };

    private static string Rect4(double left, double bottom, double right, double top) =>
        $"{left.ToString("F2", Inv)},{bottom.ToString("F2", Inv)},{right.ToString("F2", Inv)},{top.ToString("F2", Inv)}";

    private static string NormColour(string hex) =>
        (hex.StartsWith('#') ? hex : "#" + hex).ToUpperInvariant();

    private static string HexToRgbOp(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            double r = Convert.ToInt32(hex[..2], 16) / 255.0;
            double g = Convert.ToInt32(hex[2..4], 16) / 255.0;
            double b = Convert.ToInt32(hex[4..6], 16) / 255.0;
            return $"{r.ToString("F3", Inv)} {g.ToString("F3", Inv)} {b.ToString("F3", Inv)} rg";
        }
        return "0 0 0 rg";
    }

    private static float[]? ParseRect(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(',');
        if (parts.Length < 4) return null;
        try { return parts.Select(p => F(p.Trim())).ToArray(); }
        catch { return null; }
    }

    private static float  F(string s) => float.Parse(s, Inv);
    private static double D(string s) => double.Parse(s, Inv);
}
