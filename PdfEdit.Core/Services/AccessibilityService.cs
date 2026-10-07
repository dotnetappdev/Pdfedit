using System.Globalization;
using System.Text.RegularExpressions;
using iText.Forms;
using iText.IO.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Tagging;

namespace PdfEdit.Services;

public enum CheckStatus { Passed, Failed, Warning, Manual }

/// <summary>One rule of the accessibility check and how the document did.</summary>
public sealed record AccessibilityCheck(string Category, string Rule, CheckStatus Status, string Detail, string? FixKey = null)
{
    public string Glyph => Status switch
    {
        CheckStatus.Passed => "",    // check mark
        CheckStatus.Failed => "",    // cross
        CheckStatus.Warning => "",   // warning
        _ => "",                     // help
    };
    public string StatusText => Status switch
    {
        CheckStatus.Passed => "Passed", CheckStatus.Failed => "Failed",
        CheckStatus.Warning => "Warning", _ => "Check by hand",
    };
    public bool CanFix => FixKey != null && Status is CheckStatus.Failed or CheckStatus.Warning;
}

/// <summary>What <see cref="AccessibilityService.Fix"/> should repair.</summary>
public sealed class AccessibilityFixOptions
{
    public string? Title { get; set; }
    public string? Language { get; set; }
    public bool DisplayTitle { get; set; } = true;
    public bool TabOrder { get; set; } = true;
    public bool FieldTooltips { get; set; } = true;
    public bool LinkDescriptions { get; set; } = true;
    public bool MarkTagged { get; set; }
}

/// <summary>
/// the usual Accessibility Checker (the parts that can be checked by reading the file): title,
/// language, tagging, alternative text on figures, fonts, tab order, form field descriptions,
/// link descriptions, scanned pages without text, bookmarks and screen-reader permission.
/// <see cref="Fix"/> repairs the document-level ones.
/// </summary>
public static class AccessibilityService
{
    public static List<AccessibilityCheck> Check(string path)
    {
        var list = new List<AccessibilityCheck>();
        using var reader = new PdfReader(path);
        reader.SetUnethicalReading(true);
        using var pdf = new PdfDocument(reader);
        var catalog = pdf.GetCatalog().GetPdfObject();
        int pages = pdf.GetNumberOfPages();

        // ── Document ─────────────────────────────────────────────
        const string doc = "Document";
        var markInfo = catalog.GetAsDictionary(PdfName.MarkInfo);
        bool marked = markInfo?.GetAsBool(PdfName.Marked) == true;
        bool tagged = pdf.IsTagged();
        list.Add(tagged
            ? new(doc, "Tagged PDF", CheckStatus.Passed, "The document has a structure tree that screen readers can follow.")
            : new(doc, "Tagged PDF", CheckStatus.Failed,
                "There are no tags, so a screen reader can't tell headings, paragraphs, lists and tables apart. Re-export the source document with tags " +
                "(in Word: Save as PDF → Options → Document structure tags), or design it in PdfEdit's Design view."));
        if (tagged && !marked)
            list.Add(new(doc, "Marked as tagged", CheckStatus.Warning, "The document has tags but isn't flagged as a tagged PDF.", "marked"));

        string title = pdf.GetDocumentInfo().GetTitle() ?? "";
        list.Add(title.Trim().Length > 0
            ? new(doc, "Title", CheckStatus.Passed, $"“{title.Trim()}”")
            : new(doc, "Title", CheckStatus.Failed, "The document has no title; screen readers announce the file name instead.", "title"));

        var prefs = catalog.GetAsDictionary(PdfName.ViewerPreferences);
        bool showTitle = prefs?.GetAsBool(PdfName.DisplayDocTitle) == true;
        list.Add(showTitle
            ? new(doc, "Show title in title bar", CheckStatus.Passed, "Viewers show the title rather than the file name.")
            : new(doc, "Show title in title bar", CheckStatus.Warning, "Viewers will show the file name instead of the title.", "displaytitle"));

        string lang = catalog.GetAsString(PdfName.Lang)?.ToUnicodeString() ?? "";
        list.Add(lang.Trim().Length > 0
            ? new(doc, "Language", CheckStatus.Passed, $"Set to {Describe(lang)}.")
            : new(doc, "Language", CheckStatus.Failed, "No language is set, so a screen reader may read it with the wrong voice and pronunciation.", "language"));

        bool encrypted = reader.IsEncrypted();
        bool srAllowed = !encrypted || (reader.GetPermissions() & EncryptionConstants.ALLOW_SCREENREADERS) != 0;
        list.Add(srAllowed
            ? new(doc, "Screen reader permission", CheckStatus.Passed, encrypted ? "Security settings allow assistive technology." : "The document isn't restricted.")
            : new(doc, "Screen reader permission", CheckStatus.Failed, "Security settings stop screen readers from reading the text. Remove the password or allow accessibility."));

        bool hasOutlines = catalog.GetAsDictionary(PdfName.Outlines)?.GetAsDictionary(PdfName.First) != null;
        if (pages > 20)
            list.Add(hasOutlines
                ? new(doc, "Bookmarks", CheckStatus.Passed, "A long document with bookmarks for navigation.")
                : new(doc, "Bookmarks", CheckStatus.Warning, $"{pages} pages and no bookmarks. Add some (View → Add Bookmark) so people can find their way round."));

        // ── Page content ─────────────────────────────────────────
        const string content = "Page content";
        var scanned = new List<int>();
        var fontsNotEmbedded = new SortedSet<string>();
        int pagesNoTabOrder = 0;
        for (int p = 1; p <= pages; p++)
        {
            var page = pdf.GetPage(p);
            string text = "";
            try { text = PdfTextExtractor.GetTextFromPage(page); } catch { }
            var xobjects = page.GetResources()?.GetResource(PdfName.XObject);
            if (text.Trim().Length == 0 && xobjects != null && xobjects.Size() > 0) scanned.Add(p);

            var fonts = page.GetResources()?.GetResource(PdfName.Font);
            if (fonts != null)
                foreach (var key in fonts.KeySet())
                    if (fonts.GetAsDictionary(key) is { } font && !IsEmbedded(font))
                        fontsNotEmbedded.Add(font.GetAsName(PdfName.BaseFont)?.GetValue() ?? key.GetValue());

            if (page.GetAnnotations().Count > 0 && !PdfName.S.Equals(page.GetPdfObject().GetAsName(PdfName.Tabs))) pagesNoTabOrder++;
        }
        list.Add(scanned.Count == 0
            ? new(content, "Text is real text", CheckStatus.Passed, "Every page has text that can be read out.")
            : new(content, "Text is real text", CheckStatus.Failed,
                $"{Pages(scanned)} look scanned (images with no text). Run text recognition: Toolkit → Scan & text recognition → Make scanned pages searchable."));

        list.Add(fontsNotEmbedded.Count == 0
            ? new(content, "Fonts", CheckStatus.Passed, "All fonts are embedded.")
            : new(content, "Fonts", CheckStatus.Warning,
                $"Not embedded: {string.Join(", ", fontsNotEmbedded.Take(6))}{(fontsNotEmbedded.Count > 6 ? "…" : "")}. Text may display or read differently elsewhere. Archive (PDF/A) embeds fonts where possible."));

        if (tagged)
        {
            var (figures, missingAlt) = CountFigures(pdf.GetStructTreeRoot());
            list.Add(figures == 0
                ? new(content, "Alternative text", CheckStatus.Passed, "No tagged figures.")
                : missingAlt == 0
                    ? new(content, "Alternative text", CheckStatus.Passed, $"All {figures} figure(s) have alternative text.")
                    : new(content, "Alternative text", CheckStatus.Failed, $"{missingAlt} of {figures} figure(s) have no alternative text describing them."));
        }
        else
            list.Add(new(content, "Alternative text", CheckStatus.Manual, "Without tags, images can't carry alternative text. Check that no information is given only by a picture."));

        list.Add(new(content, "Colour contrast", CheckStatus.Manual, "Make sure text stands out from its background and that colour isn't the only way information is shown."));
        list.Add(new(content, "Reading order", CheckStatus.Manual, "Read the document with a screen reader (Narrator: Win+Ctrl+Enter) to check it follows a sensible order."));

        // ── Forms and links ──────────────────────────────────────
        const string forms = "Forms and links";
        var acro = PdfAcroForm.GetAcroForm(pdf, false);
        if (acro != null)
        {
            var fields = acro.GetAllFormFields().Where(kv => kv.Value.GetWidgets().Count > 0).ToList();
            int noTip = fields.Count(kv => string.IsNullOrWhiteSpace(kv.Value.GetPdfObject().GetAsString(PdfName.TU)?.ToUnicodeString()));
            if (fields.Count > 0)
                list.Add(noTip == 0
                    ? new(forms, "Field descriptions", CheckStatus.Passed, $"All {fields.Count} field(s) have a description (tooltip).")
                    : new(forms, "Field descriptions", CheckStatus.Failed, $"{noTip} of {fields.Count} field(s) have no description, so a screen reader only reads their internal name.", "tooltips"));
        }

        int links = 0, linksNoText = 0;
        for (int p = 1; p <= pages; p++)
            foreach (var a in pdf.GetPage(p).GetAnnotations())
                if (a is PdfLinkAnnotation)
                {
                    links++;
                    if (string.IsNullOrWhiteSpace(a.GetContents()?.ToUnicodeString())) linksNoText++;
                }
        if (links > 0)
            list.Add(linksNoText == 0
                ? new(forms, "Link descriptions", CheckStatus.Passed, $"All {links} link(s) have a description.")
                : new(forms, "Link descriptions", CheckStatus.Warning, $"{linksNoText} of {links} link(s) have no description of where they go.", "links"));

        if (acro != null || links > 0 || pagesNoTabOrder > 0)
            list.Add(pagesNoTabOrder == 0
                ? new(forms, "Tab order", CheckStatus.Passed, "Tab order follows the document structure.")
                : new(forms, "Tab order", CheckStatus.Warning, $"{pagesNoTabOrder} page(s) with fields or links don't set a tab order that follows the structure.", "taborder"));

        return list;
    }

    /// <summary>Repairs the document-level problems. Returns what was changed.</summary>
    public static string Fix(string inputPath, string outputPath, AccessibilityFixOptions opt)
    {
        var done = new List<string>();

        // Field labels from the words beside each field (read before writing).
        var labels = new Dictionary<string, string>();
        if (opt.FieldTooltips)
        {
            using var rd = new PdfDocument(new PdfReader(inputPath));
            var acroR = PdfAcroForm.GetAcroForm(rd, false);
            var chunkCache = new Dictionary<int, List<TextChunk>>();
            if (acroR != null)
                foreach (var (name, field) in acroR.GetAllFormFields())
                {
                    var w = field.GetWidgets().FirstOrDefault();
                    if (w == null) continue;
                    int pn = w.GetPage() is { } pg ? rd.GetPageNumber(pg) : 0;
                    string? label = null;
                    if (pn > 0)
                    {
                        try
                        {
                            if (!chunkCache.TryGetValue(pn, out var chunks)) chunkCache[pn] = chunks = PageTextLocator.GetChunks(inputPath, pn);
                            var r = w.GetRectangle().ToRectangle();
                            bool cb = PdfName.Btn.Equals(field.GetFormType());
                            label = PageTextLocator.LabelFor(chunks, r.GetLeft(), r.GetBottom(), r.GetRight(), r.GetTop(), cb);
                        }
                        catch { }
                    }
                    labels[name] = string.IsNullOrWhiteSpace(label) ? Humanise(name) : label.Trim().TrimEnd(':').Trim();
                }
        }

        using var pdf = new PdfDocument(new PdfReader(inputPath), new PdfWriter(outputPath));
        var catalog = pdf.GetCatalog();

        if (!string.IsNullOrWhiteSpace(opt.Title))
        {
            pdf.GetDocumentInfo().SetTitle(opt.Title.Trim());
            done.Add("title");
        }
        if (!string.IsNullOrWhiteSpace(opt.Language))
        {
            catalog.SetLang(new PdfString(opt.Language.Trim()));
            done.Add("language");
        }
        if (opt.DisplayTitle)
        {
            var prefs = catalog.GetPdfObject().GetAsDictionary(PdfName.ViewerPreferences);
            if (prefs == null) { prefs = new PdfDictionary(); catalog.GetPdfObject().Put(PdfName.ViewerPreferences, prefs); }
            prefs.Put(PdfName.DisplayDocTitle, PdfBoolean.TRUE);
            done.Add("title shown in title bar");
        }
        if (opt.MarkTagged && pdf.IsTagged())
        {
            var mi = catalog.GetPdfObject().GetAsDictionary(PdfName.MarkInfo) ?? new PdfDictionary();
            mi.Put(PdfName.Marked, PdfBoolean.TRUE);
            catalog.GetPdfObject().Put(PdfName.MarkInfo, mi);
            done.Add("marked as tagged");
        }

        int tabs = 0, tips = 0, linkTexts = 0;
        for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
        {
            var page = pdf.GetPage(p);
            var annots = page.GetAnnotations();
            if (opt.TabOrder && annots.Count > 0 && !PdfName.S.Equals(page.GetPdfObject().GetAsName(PdfName.Tabs)))
            {
                page.GetPdfObject().Put(PdfName.Tabs, PdfName.S);
                tabs++;
            }
            if (opt.LinkDescriptions)
                foreach (var a in annots.OfType<PdfLinkAnnotation>())
                {
                    if (!string.IsNullOrWhiteSpace(a.GetContents()?.ToUnicodeString())) continue;
                    string? desc = LinkDescription(pdf, a);
                    if (desc == null) continue;
                    a.SetContents(desc);
                    linkTexts++;
                }
        }
        if (opt.FieldTooltips)
        {
            var acro = PdfAcroForm.GetAcroForm(pdf, false);
            if (acro != null)
                foreach (var (name, field) in acro.GetAllFormFields())
                {
                    if (field.GetWidgets().Count == 0) continue;
                    var obj = field.GetPdfObject();
                    if (!string.IsNullOrWhiteSpace(obj.GetAsString(PdfName.TU)?.ToUnicodeString())) continue;
                    obj.Put(PdfName.TU, new PdfString(labels.TryGetValue(name, out var l) ? l : Humanise(name), PdfEncodings.UNICODE_BIG));
                    obj.SetModified();
                    tips++;
                }
        }
        if (tabs > 0) done.Add($"tab order on {tabs} page(s)");
        if (tips > 0) done.Add($"{tips} field description(s)");
        if (linkTexts > 0) done.Add($"{linkTexts} link description(s)");
        return done.Count == 0 ? "Nothing needed fixing" : "Fixed " + string.Join(", ", done);
    }

    private static string? LinkDescription(PdfDocument pdf, PdfLinkAnnotation link)
    {
        var action = link.GetAction();
        if (action != null)
        {
            if (PdfName.URI.Equals(action.GetAsName(PdfName.S)))
            {
                string uri = action.GetAsString(PdfName.URI)?.ToUnicodeString() ?? "";
                if (uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return "Email " + uri[7..];
                if (uri.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) return "Call " + uri[4..];
                return uri.Length > 0 ? "Link to " + uri : null;
            }
            if (PdfName.GoTo.Equals(action.GetAsName(PdfName.S)) && PageOf(pdf, action.Get(PdfName.D)) is int n) return $"Go to page {n}";
        }
        return PageOf(pdf, link.GetDestinationObject()) is int m ? $"Go to page {m}" : null;
    }

    private static int? PageOf(PdfDocument pdf, PdfObject? dest)
    {
        if (dest is PdfArray arr && arr.Size() > 0 && arr.Get(0) is PdfDictionary pageDict)
        {
            for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
                if (pdf.GetPage(p).GetPdfObject() == pageDict) return p;
        }
        return null;
    }

    private static bool IsEmbedded(PdfDictionary font)
    {
        var subtype = font.GetAsName(PdfName.Subtype);
        if (PdfName.Type3.Equals(subtype)) return true;
        if (PdfName.Type0.Equals(subtype))
        {
            var desc = font.GetAsArray(PdfName.DescendantFonts)?.GetAsDictionary(0);
            return desc != null && IsEmbedded(desc);
        }
        var fd = font.GetAsDictionary(PdfName.FontDescriptor);
        return fd != null && (fd.ContainsKey(PdfName.FontFile) || fd.ContainsKey(PdfName.FontFile2) || fd.ContainsKey(PdfName.FontFile3));
    }

    private static (int Figures, int MissingAlt) CountFigures(PdfStructTreeRoot? root)
    {
        int figures = 0, missing = 0;
        if (root == null) return (0, 0);
        var stack = new Stack<IStructureNode>(root.GetKids().Where(k => k != null));
        int guard = 0;
        while (stack.Count > 0 && guard++ < 200_000)
        {
            if (stack.Pop() is not PdfStructElem e) continue;
            if (PdfName.Figure.Equals(e.GetRole()))
            {
                figures++;
                if (string.IsNullOrWhiteSpace(e.GetAlt()?.ToUnicodeString()) && string.IsNullOrWhiteSpace(e.GetActualText()?.ToUnicodeString())) missing++;
            }
            foreach (var k in e.GetKids()) if (k != null) stack.Push(k);
        }
        return (figures, missing);
    }

    private static string Pages(List<int> pages) =>
        pages.Count == 1 ? $"Page {pages[0]}" : pages.Count <= 8 ? $"Pages {string.Join(", ", pages)}" : $"{pages.Count} pages (from page {pages[0]})";

    private static string Describe(string lang)
    {
        try { return $"{CultureInfo.GetCultureInfo(lang).DisplayName} ({lang})"; }
        catch { return lang; }
    }

    /// <summary>"first_name" / "FirstName1" → "First name".</summary>
    public static string Humanise(string name)
    {
        string last = name.Split('.').Last();
        last = Regex.Replace(last, @"[_\-]+", " ");
        last = Regex.Replace(last, @"(?<=[a-z])(?=[A-Z])", " ");
        last = Regex.Replace(last, @"\s*\d+$", "").Trim();
        if (last.Length == 0) return name;
        return char.ToUpper(last[0]) + last[1..].ToLowerInvariant();
    }
}
