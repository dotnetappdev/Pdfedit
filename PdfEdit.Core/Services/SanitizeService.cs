using iText.Kernel.Pdf;

namespace PdfEdit.Services;

/// <summary>What <see cref="SanitizeService"/> should take out of a PDF.</summary>
public sealed record SanitizeOptions(bool Metadata = true, bool Scripts = true, bool Attachments = true,
                                     bool Comments = false, bool Bookmarks = false);

/// <summary>
/// Acrobat's "Remove Hidden Information" / "Sanitize Document": strips document metadata (Info and
/// XMP), JavaScript and automatic actions, embedded files, and optionally comments and bookmarks.
/// Visible page content and form fields are left alone.
/// </summary>
public static class SanitizeService
{
    private static readonly PdfName[] MarkupSubtypes =
    {
        PdfName.Text, PdfName.FreeText, PdfName.Line, PdfName.Square, PdfName.Circle, PdfName.Polygon, PdfName.PolyLine,
        PdfName.Highlight, PdfName.Underline, PdfName.Squiggly, PdfName.StrikeOut, PdfName.Stamp, PdfName.Caret,
        PdfName.Ink, PdfName.Popup, PdfName.Sound, PdfName.Redact,
    };

    /// <returns>A short description of what was removed.</returns>
    public static string Sanitize(string inputPath, string outputPath, SanitizeOptions opt)
    {
        var removed = new List<string>();
        using var pdf = new PdfDocument(new PdfReader(inputPath), new PdfWriter(outputPath));
        var catalog = pdf.GetCatalog().GetPdfObject();

        if (opt.Metadata)
        {
            var info = pdf.GetTrailer().GetAsDictionary(PdfName.Info);
            if (info != null && info.Size() > 0) { info.Clear(); removed.Add("metadata"); }
            if (catalog.ContainsKey(PdfName.Metadata)) { catalog.Remove(PdfName.Metadata); removed.Add("XMP metadata"); }
            catalog.Remove(PdfName.PieceInfo);
            pdf.GetDocumentInfo().SetProducer(""); // iText would otherwise stamp itself back in
        }

        int scripts = 0, files = 0, comments = 0;
        var names = catalog.GetAsDictionary(PdfName.Names);
        if (opt.Scripts)
        {
            if (names?.Remove(PdfName.JavaScript) != null) scripts++;
            if (catalog.Remove(PdfName.OpenAction) != null) scripts++;
            if (catalog.Remove(PdfName.AA) != null) scripts++;
            // Field calculation / format scripts stay: they are part of how the form works.
        }
        if (opt.Attachments)
        {
            if (names?.Remove(PdfName.EmbeddedFiles) != null) files++;
            catalog.Remove(PdfName.AF);
        }
        if (opt.Bookmarks && catalog.Remove(PdfName.Outlines) != null) removed.Add("bookmarks");

        for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
        {
            var page = pdf.GetPage(p).GetPdfObject();
            if (opt.Scripts && page.Remove(PdfName.AA) != null) scripts++;
            var annots = page.GetAsArray(PdfName.Annots);
            if (annots == null) continue;
            for (int i = annots.Size() - 1; i >= 0; i--)
            {
                if (annots.GetAsDictionary(i) is not { } a) continue;
                var sub = a.GetAsName(PdfName.Subtype);
                if (opt.Attachments && PdfName.FileAttachment.Equals(sub)) { annots.Remove(i); files++; continue; }
                if (opt.Comments && MarkupSubtypes.Any(m => m.Equals(sub))) { annots.Remove(i); comments++; continue; }
                if (opt.Scripts)
                {
                    if (a.Remove(PdfName.AA) != null) scripts++;
                    var act = a.GetAsDictionary(PdfName.A);
                    var s = act?.GetAsName(PdfName.S);
                    if (s != null && (PdfName.JavaScript.Equals(s) || PdfName.Launch.Equals(s))) { a.Remove(PdfName.A); scripts++; }
                }
            }
            annots.SetModified();
        }
        if (scripts > 0) removed.Add($"{scripts} script(s)/action(s)");
        if (files > 0) removed.Add($"{files} attachment(s)");
        if (comments > 0) removed.Add($"{comments} comment(s)");
        return removed.Count == 0 ? "nothing to remove" : string.Join(", ", removed);
    }
}
