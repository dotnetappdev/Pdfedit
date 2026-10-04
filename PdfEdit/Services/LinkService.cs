using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Action;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Navigation;

namespace PdfEdit.Services;

/// <summary>What a link does: open a web address / email / phone number, or jump to a page.</summary>
public sealed record LinkTarget(string? Uri, int? PageNumber)
{
    public static LinkTarget ToUri(string uri) => new(uri, null);
    public static LinkTarget ToPage(int page) => new(null, page);

    public bool IsPage => PageNumber != null;

    public string Describe() => PageNumber is { } p ? $"Go to page {p}"
        : Uri is { } u && u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? $"Email {u[7..]}"
        : Uri is { } t && t.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ? $"Call {t[4..]}"
        : Uri ?? "(no action)";
}

/// <summary>A link annotation on a page (rectangle in PDF points, bottom-left origin).</summary>
public sealed record PdfLinkInfo(int PageNumber, int AnnotIndex, double Left, double Bottom, double Width, double Height,
                                 LinkTarget Target, bool HasBorder);

/// <summary>
/// Hyperlinks in the PDF itself (/Link annotations), like Acrobat's Edit PDF → Link:
/// list them for the viewer, add, change and remove them. A link is identified by its page and
/// its position in the page's /Annots array.
/// </summary>
public static class LinkService
{
    public static List<PdfLinkInfo> GetLinks(string path)
    {
        var list = new List<PdfLinkInfo>();
        using var pdf = new PdfDocument(new PdfReader(path));
        for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
        {
            var annots = pdf.GetPage(p).GetPdfObject().GetAsArray(PdfName.Annots);
            if (annots == null) continue;
            for (int i = 0; i < annots.Size(); i++)
            {
                if (annots.GetAsDictionary(i) is not { } d || !PdfName.Link.Equals(d.GetAsName(PdfName.Subtype))) continue;
                var r = d.GetAsRectangle(PdfName.Rect);
                if (r == null) continue;
                list.Add(new PdfLinkInfo(p, i, r.GetLeft(), r.GetBottom(), r.GetWidth(), r.GetHeight(),
                    ReadTarget(pdf, d), HasVisibleBorder(d)));
            }
        }
        return list;
    }

    private static bool HasVisibleBorder(PdfDictionary d)
    {
        if (d.GetAsDictionary(PdfName.BS) is { } bs && bs.GetAsNumber(PdfName.W) is { } w) return w.DoubleValue() > 0;
        if (d.GetAsArray(PdfName.Border) is { } b && b.Size() >= 3 && b.GetAsNumber(2) is { } bw) return bw.DoubleValue() > 0;
        return false;
    }

    private static LinkTarget ReadTarget(PdfDocument pdf, PdfDictionary link)
    {
        if (link.GetAsDictionary(PdfName.A) is { } a)
        {
            var s = a.GetAsName(PdfName.S);
            if (PdfName.URI.Equals(s)) return LinkTarget.ToUri(a.GetAsString(PdfName.URI)?.ToUnicodeString() ?? "");
            if (PdfName.GoTo.Equals(s) && DestPage(pdf, a.Get(PdfName.D)) is { } gp) return LinkTarget.ToPage(gp);
            if (PdfName.Launch.Equals(s)) return LinkTarget.ToUri(a.GetAsString(PdfName.F)?.ToUnicodeString() ?? "(open file)");
        }
        if (DestPage(pdf, link.Get(PdfName.Dest)) is { } dp) return LinkTarget.ToPage(dp);
        return new LinkTarget(null, null);
    }

    private static int? DestPage(PdfDocument pdf, PdfObject? dest)
    {
        try
        {
            if (dest == null) return null;
            if (dest is PdfString or PdfName)
            {
                // Named destination: look it up in the document's name tree.
                var names = pdf.GetCatalog().GetNameTree(PdfName.Dests).GetNames();
                string key = dest is PdfString ps ? ps.ToUnicodeString() : ((PdfName)dest).GetValue();
                foreach (var kv in names)
                    if (kv.Key.ToUnicodeString() == key) { dest = kv.Value; break; }
                if (dest is PdfDictionary dd) dest = dd.Get(PdfName.D);
            }
            if (dest is PdfArray arr && arr.Size() > 0)
            {
                if (arr.Get(0) is PdfDictionary pageDict) return pdf.GetPageNumber(pageDict);
                if (arr.GetAsNumber(0) is { } n) return n.IntValue() + 1; // remote-style 0-based index
            }
        }
        catch { /* unreadable destination */ }
        return null;
    }

    private static void ApplyTarget(PdfDocument pdf, PdfLinkAnnotation link, LinkTarget target)
    {
        link.GetPdfObject().Remove(PdfName.Dest);
        if (target.PageNumber is { } p)
        {
            p = Math.Clamp(p, 1, pdf.GetNumberOfPages());
            var dest = PdfExplicitDestination.CreateFit(pdf.GetPage(p));
            link.SetAction(PdfAction.CreateGoTo(dest));
        }
        else
            link.SetAction(PdfAction.CreateURI(target.Uri ?? ""));
    }

    private static void SetBorder(PdfLinkAnnotation link, bool visible)
    {
        link.GetPdfObject().Remove(PdfName.BS);
        link.SetBorder(new PdfArray(new float[] { 0, 0, visible ? 1 : 0 }));
        link.SetColor(new PdfArray(new float[] { 0, 0, 1 }));
        link.SetHighlightMode(PdfAnnotation.HIGHLIGHT_INVERT);
    }

    public static void AddLink(string inputPath, string outputPath, int pageNumber,
        double left, double bottom, double width, double height, LinkTarget target, bool visibleBorder)
    {
        using var pdf = new PdfDocument(new PdfReader(inputPath), new PdfWriter(outputPath));
        if (pageNumber < 1 || pageNumber > pdf.GetNumberOfPages()) return;
        var link = new PdfLinkAnnotation(new Rectangle((float)left, (float)bottom, (float)width, (float)height));
        ApplyTarget(pdf, link, target);
        SetBorder(link, visibleBorder);
        pdf.GetPage(pageNumber).AddAnnotation(link);
    }

    public static void UpdateLink(string inputPath, string outputPath, int pageNumber, int annotIndex,
        LinkTarget target, bool visibleBorder)
    {
        using var pdf = new PdfDocument(new PdfReader(inputPath), new PdfWriter(outputPath));
        if (pageNumber < 1 || pageNumber > pdf.GetNumberOfPages()) return;
        var annots = pdf.GetPage(pageNumber).GetPdfObject().GetAsArray(PdfName.Annots);
        if (annots?.GetAsDictionary(annotIndex) is not { } d || !PdfName.Link.Equals(d.GetAsName(PdfName.Subtype))) return;
        var link = (PdfLinkAnnotation)PdfAnnotation.MakeAnnotation(d);
        ApplyTarget(pdf, link, target);
        SetBorder(link, visibleBorder);
    }

    /// <summary>Removes one link, or every link on <paramref name="pageNumber"/> (0 = the whole document) when annotIndex is -1.</summary>
    public static int RemoveLinks(string inputPath, string outputPath, int pageNumber, int annotIndex = -1)
    {
        int removed = 0;
        using var pdf = new PdfDocument(new PdfReader(inputPath), new PdfWriter(outputPath));
        for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
        {
            if (pageNumber != 0 && p != pageNumber) continue;
            var annots = pdf.GetPage(p).GetPdfObject().GetAsArray(PdfName.Annots);
            if (annots == null) continue;
            for (int i = annots.Size() - 1; i >= 0; i--)
            {
                if (annotIndex >= 0 && i != annotIndex) continue;
                if (annots.GetAsDictionary(i) is { } d && PdfName.Link.Equals(d.GetAsName(PdfName.Subtype)))
                {
                    annots.Remove(i);
                    removed++;
                }
            }
            annots.SetModified();
        }
        return removed;
    }
}
