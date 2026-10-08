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
}

/// <summary>Lists the annotations already saved in a PDF (not form fields or links).</summary>
public static class PdfAnnotationReader
{
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
