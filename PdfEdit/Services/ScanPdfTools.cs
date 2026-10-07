using System.IO;
using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;

namespace PdfEdit.Services;

/// <summary>
/// Scanned pages (WPF bitmaps from the scanner) to PDF. The rest of the file tools are in
/// PdfEdit.Core's PdfToolsService.
/// </summary>
public static class ScanPdfTools
{
    /// <summary>
    /// One page per scanned image, sized exactly to the scan (pixels ÷ DPI × 72), like a scanner's
    /// own PDF output — so OCR word positions line up with the page. Colour / grey pages are stored
    /// as JPEG (compact), black-and-white as PNG (sharp).
    /// </summary>
    public static void CreatePdfFromScans(IEnumerable<ScannedPage> pages, string dest, bool blackWhite)
    {
        using var doc = new PdfDocument(new PdfWriter(dest));
        foreach (var p in pages)
        {
            var bytes = EncodeScan(p.Image, blackWhite);
            var data = ImageDataFactory.Create(bytes);
            double dpi = p.Dpi > 1 ? p.Dpi : 200;
            float w = (float)(p.Image.PixelWidth / dpi * 72), h = (float)(p.Image.PixelHeight / dpi * 72);
            var page = doc.AddNewPage(new PageSize(w, h));
            new PdfCanvas(page).AddImageFittedIntoRectangle(data, new Rectangle(0, 0, w, h), false);
        }
        if (doc.GetNumberOfPages() == 0) doc.AddNewPage(PageSize.A4);
    }

    /// <summary>Page size in points of a scanned page (for OCR).</summary>
    public static (double W, double H) ScanPageSize(ScannedPage p)
    {
        double dpi = p.Dpi > 1 ? p.Dpi : 200;
        return (p.Image.PixelWidth / dpi * 72, p.Image.PixelHeight / dpi * 72);
    }

    private static byte[] EncodeScan(System.Windows.Media.Imaging.BitmapSource bmp, bool blackWhite)
    {
        // JPEG only takes grey or 24-bit colour; 1-bit / indexed scans go to PNG unchanged.
        var fmt = bmp.Format;
        if (!blackWhite && fmt.BitsPerPixel < 8) blackWhite = true;
        if (!blackWhite && fmt != System.Windows.Media.PixelFormats.Gray8 && fmt != System.Windows.Media.PixelFormats.Bgr24)
            bmp = new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp, System.Windows.Media.PixelFormats.Bgr24, null, 0);
        System.Windows.Media.Imaging.BitmapEncoder enc = blackWhite
            ? new System.Windows.Media.Imaging.PngBitmapEncoder()
            : new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 85 };
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
