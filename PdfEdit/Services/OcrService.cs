using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace PdfEdit.Services;

/// <summary>
/// Text recognition with the OCR engine built into Windows 10/11 (Windows.Media.Ocr) — no extra
/// downloads. Uses the user's Windows display languages.
/// </summary>
public static class OcrService
{
    public static bool IsAvailable => OcrEngine.TryCreateFromUserProfileLanguages() != null;

    /// <summary>
    /// Recognises the words in a rendered page image and maps them to PDF points
    /// (origin bottom-left) using the page size.
    /// </summary>
    public static async Task<(string Text, List<OcrWord> Words)> RecognizeAsync(
        BitmapSource page, double pageWidthPt, double pageHeightPt)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException(
                "Windows has no OCR language installed. Add one in Settings → Time & language → Language (e.g. English with 'Optical character recognition').");

        // Keep within the engine's limit (it rejects very large images).
        BitmapSource src = page;
        double max = OcrEngine.MaxImageDimension;
        if (src.PixelWidth > max || src.PixelHeight > max)
        {
            double k = max / Math.Max(src.PixelWidth, src.PixelHeight);
            src = new TransformedBitmap(src, new ScaleTransform(k, k));
        }

        var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);

        var writer = new DataWriter();
        writer.WriteBytes(pixels);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);

        var result = await engine.RecognizeAsync(bitmap);

        double sx = pageWidthPt / w, sy = pageHeightPt / h;
        var words = new List<OcrWord>();
        foreach (var line in result.Lines)
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                words.Add(new OcrWord(word.Text, r.X * sx, pageHeightPt - (r.Y + r.Height) * sy, r.Width * sx, r.Height * sy));
            }
        return (string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)), words);
    }
}
