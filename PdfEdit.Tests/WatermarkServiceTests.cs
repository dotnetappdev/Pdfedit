using System.IO;
using iText.Kernel.Pdf;
using PdfEdit.Models;
using PdfEdit.Services;
using Xunit;

namespace PdfEdit.Tests;

/// <summary>Watermarks: applying again replaces PdfEdit's earlier one instead of stacking on it.</summary>
public class WatermarkServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pdfedit-watermark-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private string P(string name) => Path.Combine(_dir, name);

    private static int ContentStreams(string path)
    {
        using var pdf = new PdfDocument(new PdfReader(path));
        return pdf.GetPage(1).GetPdfObject().Get(PdfName.Contents) is PdfArray arr ? arr.Size() : 1;
    }

    [Fact]
    public void Apply_Again_ReplacesTheEarlierWatermark()
    {
        PdfToolsService.CreateBlankPdf(P("blank.pdf"), pages: 1);
        WatermarkService.Apply(P("blank.pdf"), P("one.pdf"), new WatermarkOptions { Text = "DRAFT", FontSize = 60 }, 1);
        WatermarkService.Apply(P("one.pdf"), P("two.pdf"), new WatermarkOptions { Text = "DRAFT", FontSize = 120 }, 1);

        Assert.Equal(ContentStreams(P("one.pdf")), ContentStreams(P("two.pdf")));
        Assert.Equal(1, WatermarkService.Remove(P("two.pdf"), P("none.pdf")));
        Assert.False(WatermarkService.HasWatermark(P("none.pdf")));
    }
}
