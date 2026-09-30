using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using iText.IO.Image;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Utils;

namespace PdfEdit.Services;

/// <summary>A word found by OCR, in PDF points (origin bottom-left).</summary>
public readonly record struct OcrWord(string Text, double Left, double Bottom, double Width, double Height);

/// <summary>
/// File-level tools behind the "All tools" panel (Acrobat-style): create, combine, export to Word
/// and make scanned pages searchable. Pure iText / .NET — no UI.
/// </summary>
public static class PdfToolsService
{
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    public static bool IsImage(string path) =>
        ImageExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    // ── Create ────────────────────────────────────────────────────────────────

    /// <summary>Creates a PDF with <paramref name="pages"/> blank pages (A4 portrait by default).</summary>
    public static void CreateBlankPdf(string dest, int pages = 1, double widthPt = 595.28, double heightPt = 841.89)
    {
        using var doc = new PdfDocument(new PdfWriter(dest));
        for (int i = 0; i < Math.Max(1, pages); i++)
            doc.AddNewPage(new PageSize((float)widthPt, (float)heightPt));
    }

    /// <summary>One A4 page per image (portrait or landscape to suit), image scaled to fit.</summary>
    public static void CreatePdfFromImages(IEnumerable<string> images, string dest)
    {
        using var doc = new PdfDocument(new PdfWriter(dest));
        foreach (var img in images) AddImagePage(doc, img);
        if (doc.GetNumberOfPages() == 0) doc.AddNewPage(PageSize.A4);
    }

    private static void AddImagePage(PdfDocument doc, string imagePath)
    {
        var data = ImageDataFactory.Create(imagePath);
        bool landscape = data.GetWidth() > data.GetHeight();
        var size = landscape ? PageSize.A4.Rotate() : PageSize.A4;
        var page = doc.AddNewPage(size);

        const float margin = 18f;
        float maxW = size.GetWidth() - 2 * margin, maxH = size.GetHeight() - 2 * margin;
        // Shrink to fit the page; small images keep their natural size.
        float scale = Math.Min(1f, Math.Min(maxW / data.GetWidth(), maxH / data.GetHeight()));
        float w = data.GetWidth() * scale, h = data.GetHeight() * scale;

        float x = (size.GetWidth() - w) / 2, y = (size.GetHeight() - h) / 2;
        new PdfCanvas(page).AddImageFittedIntoRectangle(data, new Rectangle(x, y, w, h), false);
    }

    // ── Combine ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Combines PDFs and images (in the given order) into one PDF — images become A4 pages,
    /// like Acrobat's Combine files.
    /// </summary>
    public static int CombineFiles(IEnumerable<string> files, string dest)
    {
        using var output = new PdfDocument(new PdfWriter(dest));
        var merger = new PdfMerger(output);
        foreach (var file in files)
        {
            if (IsImage(file))
            {
                AddImagePage(output, file);
                continue;
            }
            using var src = new PdfDocument(new PdfReader(file));
            merger.Merge(src, 1, src.GetNumberOfPages());
        }
        return output.GetNumberOfPages();
    }

    // ── Export to Word ────────────────────────────────────────────────────────

    /// <summary>
    /// Exports the PDF's text to a Word document (.docx): one paragraph per text line, a page
    /// break between PDF pages. Layout, images and fonts are not reproduced.
    /// </summary>
    public static int ExportToWord(string pdfPath, string docxPath)
    {
        var pages = new List<string>();
        using (var doc = new PdfDocument(new PdfReader(pdfPath)))
            for (int i = 1; i <= doc.GetNumberOfPages(); i++)
                pages.Add(PdfTextExtractor.GetTextFromPage(doc.GetPage(i)));

        var body = new StringBuilder();
        for (int p = 0; p < pages.Count; p++)
        {
            foreach (var line in pages[p].Replace("\r\n", "\n").Split('\n'))
                body.Append("<w:p><w:r><w:t xml:space=\"preserve\">")
                    .Append(SecurityElement.Escape(line) ?? string.Empty)
                    .Append("</w:t></w:r></w:p>");
            if (p < pages.Count - 1)
                body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
        }

        const string contentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "</Types>";
        const string rels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "</Relationships>";
        string document =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
            body + "<w:sectPr/></w:body></w:document>";

        if (File.Exists(docxPath)) File.Delete(docxPath);
        using var zip = ZipFile.Open(docxPath, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        Add("[Content_Types].xml", contentTypes);
        Add("_rels/.rels", rels);
        Add("word/document.xml", document);
        return pages.Count;
    }

    // ── Scan & OCR: searchable text layer ─────────────────────────────────────

    /// <summary>
    /// Writes a copy of the PDF with an invisible text layer (rendering mode 3, like Acrobat's
    /// "searchable image") so OCR'd pages can be searched, selected and copied.
    /// </summary>
    public static int AddInvisibleTextLayer(string src, string dest, IReadOnlyDictionary<int, List<OcrWord>> wordsByPage)
    {
        int written = 0;
        using var doc = new PdfDocument(new PdfReader(src), new PdfWriter(dest));
        var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
        foreach (var (pageNum, words) in wordsByPage)
        {
            if (pageNum < 1 || pageNum > doc.GetNumberOfPages() || words.Count == 0) continue;
            var canvas = new PdfCanvas(doc.GetPage(pageNum).NewContentStreamAfter(), doc.GetPage(pageNum).GetResources(), doc);
            foreach (var w in words)
            {
                string text = new string(w.Text.Where(c => c >= 32 && c < 256).ToArray());
                if (text.Length == 0 || w.Height <= 0 || w.Width <= 0) continue;
                float size = (float)Math.Max(1, w.Height * 0.9);
                float natural = font.GetWidth(text, size);
                float hScale = natural > 0 ? (float)(w.Width / natural * 100) : 100;
                canvas.BeginText()
                      .SetFontAndSize(font, size)
                      .SetTextRenderingMode(PdfCanvasConstants.TextRenderingMode.INVISIBLE)
                      .SetHorizontalScaling(hScale)
                      .MoveText(w.Left, w.Bottom + w.Height * 0.2)
                      .ShowText(text)
                      .EndText();
                written++;
            }
        }
        return written;
    }
}
