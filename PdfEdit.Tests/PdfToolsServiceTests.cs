using System.IO;
using System.IO.Compression;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Layout;
using iText.Layout.Element;
using PdfEdit.Services;
using Xunit;

namespace PdfEdit.Tests;

/// <summary>Toolkit file operations: create, combine, export to Word, OCR text layer.</summary>
public class PdfToolsServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pdfedit-tools-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private string P(string name) => Path.Combine(_dir, name);

    private string MakeTextPdf()
    {
        string path = P("text.pdf");
        using var pdf = new PdfDocument(new PdfWriter(path));
        var doc = new Document(pdf);
        doc.Add(new Paragraph("Hello <World> & friends"));
        doc.Add(new AreaBreak());
        doc.Add(new Paragraph("Page two"));
        doc.Close();
        return path;
    }

    [Fact]
    public void CreateBlankPdf_CreatesA4Pages()
    {
        PdfToolsService.CreateBlankPdf(P("blank.pdf"), pages: 2);
        using var doc = new PdfDocument(new PdfReader(P("blank.pdf")));
        Assert.Equal(2, doc.GetNumberOfPages());
        Assert.Equal(595.28f, doc.GetPage(1).GetPageSize().GetWidth(), 1);
    }

    [Fact]
    public void CombineFiles_AppendsPdfPagesInOrder()
    {
        string text = MakeTextPdf();
        PdfToolsService.CreateBlankPdf(P("blank.pdf"), pages: 3);
        int pages = PdfToolsService.CombineFiles(new[] { text, P("blank.pdf") }, P("combined.pdf"));
        Assert.Equal(5, pages);
        using var doc = new PdfDocument(new PdfReader(P("combined.pdf")));
        Assert.Contains("Hello", PdfTextExtractor.GetTextFromPage(doc.GetPage(1)));
    }

    [Fact]
    public void ExportToWord_WritesEscapedTextAndPageBreaks()
    {
        int pages = PdfToolsService.ExportToWord(MakeTextPdf(), P("out.docx"));
        Assert.Equal(2, pages);
        using var zip = ZipFile.OpenRead(P("out.docx"));
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        string xml = reader.ReadToEnd();
        Assert.Contains("Hello &lt;World&gt; &amp; friends", xml);
        Assert.Contains("w:br w:type=\"page\"", xml);
    }

    [Fact]
    public void AddInvisibleTextLayer_MakesWordsExtractable()
    {
        PdfToolsService.CreateBlankPdf(P("scan.pdf"));
        var words = new Dictionary<int, List<OcrWord>>
        {
            [1] = new() { new("Scanned", 100, 700, 80, 14), new("invoice", 190, 700, 60, 14) },
        };
        int written = PdfToolsService.AddInvisibleTextLayer(P("scan.pdf"), P("ocr.pdf"), words);
        Assert.Equal(2, written);
        using var doc = new PdfDocument(new PdfReader(P("ocr.pdf")));
        Assert.Equal("Scanned invoice", PdfTextExtractor.GetTextFromPage(doc.GetPage(1)).Trim());
    }
}
