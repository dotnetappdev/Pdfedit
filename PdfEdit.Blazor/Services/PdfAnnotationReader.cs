using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;

namespace PdfEdit.Blazor.Services;

/// <summary>A comment or mark already in the PDF, for the Comments panel.</summary>
public sealed record PdfAnnotationItem(int PageNumber, int Index, string Kind, string Author, string Text, DateTime? Modified);

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
                    list.Add(new PdfAnnotationItem(p, i, Friendly(kind), author, text, modified));
                    i++;
                }
            }
        }
        catch { /* unreadable: no comments */ }
        return list;
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
