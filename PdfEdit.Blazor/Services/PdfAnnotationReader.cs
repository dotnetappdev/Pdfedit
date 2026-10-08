using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using PdfEdit.Models;

namespace PdfEdit.Blazor.Services;

/// <summary>A comment or mark already in the PDF, for the Comments panel and the annotation summary.</summary>
public sealed record PdfAnnotationItem(int PageNumber, int Index, string Kind, string Author, string Text, DateTime? Modified)
{
    public double Left { get; init; }
    public double Bottom { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string Colour { get; init; } = "";
    /// <summary>PdfEdit's id for its own annotations (/NM "pdfedit:&lt;id&gt;"), else null.</summary>
    public string? Id { get; init; }
}

/// <summary>
/// One of PdfEdit's own annotations (/NM "pdfedit:&lt;id&gt;") read back so it can be edited again on the
/// page, in the PDF's coordinates. Kind is Text, Mark, Stamp, Ink, Highlight, Note, Rectangle or Ellipse.
/// </summary>
public sealed record OwnAnnotation(string Id, int PageNumber, string Kind)
{
    public double Left { get; init; }
    public double Bottom { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string Text { get; init; } = "";
    public string? Subtitle { get; init; }
    public string Colour { get; init; } = "#000000";
    public double FontSize { get; init; } = 12;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public double CharSpacing { get; init; }
    /// <summary>/Rotate: how far the content is turned anticlockwise on the unrotated page.</summary>
    public int Rotate { get; init; }
    public double LineWidth { get; init; } = 2;
    public double Opacity { get; init; } = 1;
    public HighlightKind Markup { get; init; } = HighlightKind.Highlight;
    public List<PointD>? Points { get; init; }
    public string Author { get; init; } = "";
}

/// <summary>Lists the annotations already saved in a PDF (not form fields or links).</summary>
public static class PdfAnnotationReader
{
    private static readonly HashSet<string> Marks = ["✓", "✕", "○", "—", "●"];

    /// <summary>
    /// PdfEdit's own annotations that the page can show as editable items: text, marks, stamps, ink,
    /// highlights / underlines, sticky notes, rectangles and ellipses. Signatures, callouts, clouds,
    /// lines and measurements stay part of the page.
    /// </summary>
    public static List<OwnAnnotation> ReadOwn(string path)
    {
        var list = new List<OwnAnnotation>();
        try
        {
            using var doc = new PdfDocument(new PdfReader(path));
            for (int p = 1; p <= doc.GetNumberOfPages(); p++)
            {
                foreach (var a in doc.GetPage(p).GetAnnotations())
                {
                    var o = a.GetPdfObject();
                    if (PdfEdit.Services.PdfFormService.TrackedId(o) is not { } id) continue;
                    if (o.ContainsKey(new PdfName("IT")) || o.ContainsKey(new PdfName("BE"))) continue;   // callouts, text edits, measures, clouds
                    var r = a.GetRectangle()?.ToRectangle();
                    if (r == null) continue;
                    string contents = a.GetContents()?.ToUnicodeString() ?? "";
                    string colour = Hex(o.GetAsArray(PdfName.C));
                    string author = a is PdfMarkupAnnotation m ? m.GetText()?.ToUnicodeString() ?? "" : "";
                    int rotate = o.GetAsNumber(PdfName.Rotate)?.IntValue() ?? 0;
                    double width = o.GetAsDictionary(PdfName.BS)?.GetAsNumber(PdfName.W)?.DoubleValue() ?? 2;
                    var own = new OwnAnnotation(id, p, "") with
                    {
                        Left = r.GetX(), Bottom = r.GetY(), Width = r.GetWidth(), Height = r.GetHeight(),
                        Colour = colour.Length > 0 ? colour : "#000000", Rotate = rotate, Author = author, LineWidth = width,
                        Opacity = o.GetAsNumber(PdfName.CA)?.DoubleValue() ?? 1,
                    };
                    switch (a.GetSubtype()?.GetValue())
                    {
                        case "FreeText":
                            var da = ParseDa(o.GetAsString(PdfName.DA)?.ToUnicodeString());
                            bool mark = Marks.Contains(contents.Trim());
                            list.Add(own with
                            {
                                Kind = mark ? "Mark" : "Text", Text = contents, FontSize = da.Size, Bold = da.Bold, Italic = da.Italic,
                                CharSpacing = da.Spacing, Colour = da.Colour ?? own.Colour,
                            });
                            break;
                        case "Stamp":
                            if (contents == "Signature" || contents.Length == 0) continue;   // a placed signature: stays put
                            var lines = contents.Split('\n', 2);
                            list.Add(own with { Kind = "Stamp", Text = lines[0], Subtitle = lines.Length > 1 ? lines[1] : null });
                            break;
                        case "Ink":
                            var ink = o.GetAsArray(PdfName.InkList);
                            if (ink == null || ink.Size() != 1 || ink.GetAsArray(0) is not { } pts || pts.Size() < 4) continue;
                            var points = new List<PointD>();
                            for (int k = 0; k + 1 < pts.Size(); k += 2)
                                points.Add(new PointD(pts.GetAsNumber(k).DoubleValue(), pts.GetAsNumber(k + 1).DoubleValue()));
                            list.Add(own with { Kind = "Ink", Points = points });
                            break;
                        case "Highlight" or "Underline" or "StrikeOut" or "Squiggly":
                            list.Add(own with
                            {
                                Kind = "Highlight",
                                Colour = colour.Length > 0 ? colour : "#FFFF00",
                                Markup = a.GetSubtype()!.GetValue() switch
                                {
                                    "Underline" => HighlightKind.Underline,
                                    "StrikeOut" => HighlightKind.Strikethrough,
                                    "Squiggly" => HighlightKind.Squiggly,
                                    _ => HighlightKind.Highlight,
                                },
                            });
                            break;
                        case "Text":
                            list.Add(own with { Kind = "Note", Text = contents });
                            break;
                        case "Square" or "Circle":
                            if (o.GetAsArray(PdfName.IC) != null) continue;   // filled toolbox shapes stay as drawn
                            list.Add(own with { Kind = a.GetSubtype()!.GetValue() == "Circle" ? "Ellipse" : "Rectangle" });
                            break;
                    }
                }
            }
        }
        catch { /* unreadable: nothing editable */ }
        return list;
    }

    /// <summary>Font size, bold / italic, character spacing and colour from a /DA string ("/Helv 12.0 Tf 1.50 Tc 0 0 0 rg").</summary>
    private static (double Size, bool Bold, bool Italic, double Spacing, string? Colour) ParseDa(string? da)
    {
        double size = 12, spacing = 0;
        bool bold = false, italic = false;
        string? colour = null;
        if (string.IsNullOrWhiteSpace(da)) return (size, bold, italic, spacing, colour);
        var t = da.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double N(int i) => i >= 0 && i < t.Length && double.TryParse(t[i], System.Globalization.NumberStyles.Float, inv, out var v) ? v : double.NaN;
        for (int i = 0; i < t.Length; i++)
        {
            switch (t[i])
            {
                case "Tf":
                    if (!double.IsNaN(N(i - 1)) && N(i - 1) > 0) size = N(i - 1);
                    string font = i >= 2 ? t[i - 2] : "";
                    bold = font.Contains("Bold"); italic = font.Contains("Oblique") || font.Contains("Italic");
                    break;
                case "Tc":
                    if (!double.IsNaN(N(i - 1))) spacing = N(i - 1);
                    break;
                case "rg" when i >= 3 && !double.IsNaN(N(i - 3)) && !double.IsNaN(N(i - 2)) && !double.IsNaN(N(i - 1)):
                    int C(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
                    colour = $"#{C(N(i - 3)):X2}{C(N(i - 2)):X2}{C(N(i - 1)):X2}";
                    break;
                case "g" when i >= 1 && !double.IsNaN(N(i - 1)):
                    int G = (int)Math.Round(Math.Clamp(N(i - 1), 0, 1) * 255);
                    colour = $"#{G:X2}{G:X2}{G:X2}";
                    break;
            }
        }
        return (size, bold, italic, spacing, colour);
    }

    private static readonly HashSet<string> Skip = ["Widget", "Link", "Popup"];

    public static List<PdfAnnotationItem> Read(string path)
    {
        var list = new List<PdfAnnotationItem>();
        try
        {
            using var doc = new PdfDocument(new PdfReader(path));
            for (int p = 1; p <= doc.GetNumberOfPages(); p++)
            {
                int i = 0;
                foreach (var a in doc.GetPage(p).GetAnnotations())
                {
                    var kind = a.GetSubtype()?.GetValue() ?? "Annotation";
                    if (Skip.Contains(kind)) { i++; continue; }
                    string author = a is PdfMarkupAnnotation m ? m.GetText()?.ToUnicodeString() ?? "" : "";
                    string text = a.GetContents()?.ToUnicodeString() ?? "";
                    DateTime? modified = null;
                    var date = a.GetDate()?.ToUnicodeString();
                    if (date != null) try { modified = PdfDate.Decode(date); } catch { /* not a PDF date */ }
                    var r = a.GetRectangle()?.ToRectangle();
                    list.Add(new PdfAnnotationItem(p, i, Friendly(kind), author, text, modified)
                    {
                        Left = r?.GetX() ?? 0, Bottom = r?.GetY() ?? 0, Width = r?.GetWidth() ?? 0, Height = r?.GetHeight() ?? 0,
                        Colour = Hex(a.GetPdfObject().GetAsArray(PdfName.C)),
                        Id = PdfEdit.Services.PdfFormService.TrackedId(a.GetPdfObject()),
                    });
                    i++;
                }
            }
        }
        catch { /* unreadable: no comments */ }
        return list;
    }

    /// <summary>The PDF's comments as PdfEdit's annotation models (for XFDF export).</summary>
    public static (List<HighlightAnnotation> Highlights, List<StickyNoteAnnotation> Notes, List<FreeTextAnnotation> Texts, List<ShapeAnnotation> Shapes) ReadModels(string path)
    {
        var highlights = new List<HighlightAnnotation>();
        var notes = new List<StickyNoteAnnotation>();
        var texts = new List<FreeTextAnnotation>();
        var shapes = new List<ShapeAnnotation>();
        using var doc = new PdfDocument(new PdfReader(path));
        for (int p = 1; p <= doc.GetNumberOfPages(); p++)
        {
            foreach (var a in doc.GetPage(p).GetAnnotations())
            {
                var r = a.GetRectangle()?.ToRectangle();
                if (r == null) continue;
                string colour = Hex(a.GetPdfObject().GetAsArray(PdfName.C)), contents = a.GetContents()?.ToUnicodeString() ?? "";
                string author = a is PdfMarkupAnnotation m ? m.GetText()?.ToUnicodeString() ?? "" : "";
                var comment = new CommentInfo { Author = author, Note = contents };
                switch (a.GetSubtype()?.GetValue())
                {
                    case "Highlight" or "Underline" or "StrikeOut" or "Squiggly":
                        highlights.Add(new HighlightAnnotation
                        {
                            PageNumber = p, Left = r.GetX(), Bottom = r.GetY(), Width = r.GetWidth(), Height = r.GetHeight(),
                            Color = colour.Length > 0 ? colour : "#FFFF00",
                            Opacity = a.GetPdfObject().GetAsNumber(PdfName.CA)?.FloatValue() ?? 1f,
                            Kind = a.GetSubtype()!.GetValue() switch
                            {
                                "Underline" => HighlightKind.Underline,
                                "StrikeOut" => HighlightKind.Strikethrough,
                                "Squiggly" => HighlightKind.Squiggly,
                                _ => HighlightKind.Highlight,
                            },
                            Comment = comment,
                        });
                        break;
                    case "Text":
                        notes.Add(new StickyNoteAnnotation { PageNumber = p, Left = r.GetX(), Bottom = r.GetY(), Text = contents, Author = author, Comment = comment });
                        break;
                    case "FreeText":
                        texts.Add(new FreeTextAnnotation
                        {
                            PageNumber = p, Left = r.GetX(), Bottom = r.GetY(), Width = r.GetWidth(), Height = r.GetHeight(),
                            Text = contents, Comment = comment,
                        });
                        break;
                    case "Square" or "Circle" or "Line":
                        shapes.Add(new ShapeAnnotation
                        {
                            PageNumber = p, X1 = r.GetX(), Y1 = r.GetY(), X2 = r.GetX() + r.GetWidth(), Y2 = r.GetY() + r.GetHeight(),
                            Kind = a.GetSubtype()!.GetValue() switch { "Circle" => ShapeKind.Ellipse, "Line" => ShapeKind.Line, _ => ShapeKind.Rectangle },
                            StrokeColor = colour.Length > 0 ? colour : "#C62828", Comment = comment,
                        });
                        break;
                }
            }
        }
        return (highlights, notes, texts, shapes);
    }

    private static string Hex(PdfArray? c)
    {
        if (c == null || c.Size() < 3) return "";
        int Ch(int i) => (int)Math.Round(Math.Clamp(c.GetAsNumber(i)?.FloatValue() ?? 0, 0, 1) * 255);
        return $"#{Ch(0):X2}{Ch(1):X2}{Ch(2):X2}";
    }

    private static string Friendly(string kind) => kind switch
    {
        "Text" => "Sticky note",
        "FreeText" => "Text",
        "Highlight" => "Highlight",
        "Underline" => "Underline",
        "StrikeOut" => "Strikethrough",
        "Squiggly" => "Squiggly",
        "Ink" => "Drawing",
        "Square" => "Rectangle",
        "Circle" => "Ellipse",
        "Line" => "Line",
        "Stamp" => "Stamp",
        "Redact" => "Redaction",
        _ => kind,
    };
}
