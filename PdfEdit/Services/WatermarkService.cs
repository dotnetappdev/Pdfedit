using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using PdfEdit.Models;

namespace PdfEdit.Services;

public static class WatermarkService
{
    public static void Apply(string inputPath, string outputPath, WatermarkOptions opt)
    {
        var color = ParseColor(opt.Color);

        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(reader, writer);

        var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

        int pageCount = pdf.GetNumberOfPages();
        for (int i = 1; i <= pageCount; i++)
        {
            var page     = pdf.GetPage(i);
            var pageSize = page.GetPageSize();
            float cx = pageSize.GetWidth()  / 2;
            float cy = pageSize.GetHeight() / 2;

            var canvas = new PdfCanvas(page);
            canvas.SaveState();

            var gs = new iText.Kernel.Pdf.Extgstate.PdfExtGState()
                .SetFillOpacity(opt.Opacity)
                .SetStrokeOpacity(opt.Opacity);
            canvas.SetExtGState(gs);

            canvas.SetFillColor(color);
            canvas.SetFontAndSize(font, opt.FontSize);

            // Measure approximate text width to center it
            float textWidth = font.GetWidth(opt.Text, opt.FontSize);

            double rad = opt.AngleDeg * Math.PI / 180.0;
            canvas.ConcatMatrix(
                (float)Math.Cos(rad), (float)Math.Sin(rad),
                -(float)Math.Sin(rad), (float)Math.Cos(rad),
                cx, cy);

            canvas.BeginText();
            canvas.SetTextMatrix(1, 0, 0, 1, -textWidth / 2, -opt.FontSize / 2);
            canvas.ShowText(opt.Text);
            canvas.EndText();

            canvas.RestoreState();
        }
    }

    private static DeviceRgb ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8) hex = hex[2..]; // strip alpha
        if (hex.Length != 6) return new DeviceRgb(0.5f, 0.5f, 0.5f);
        float r = Convert.ToInt32(hex[0..2], 16) / 255f;
        float g = Convert.ToInt32(hex[2..4], 16) / 255f;
        float b = Convert.ToInt32(hex[4..6], 16) / 255f;
        return new DeviceRgb(r, g, b);
    }
}
