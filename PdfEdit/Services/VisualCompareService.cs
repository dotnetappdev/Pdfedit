using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PdfEdit.Services;

/// <summary>One page compared visually: both renders and a difference image.</summary>
public sealed record PageDiff(int Page, BitmapSource? Old, BitmapSource? New, BitmapSource Diff, double ChangedPercent);

/// <summary>
/// Acrobat's visual Compare Files: renders each page of two PDFs and marks what changed — red
/// for content only in the old version, green for content only in the new one, unchanged
/// content faded.
/// </summary>
public static class VisualCompareService
{
    private const int InkThreshold = 200, DiffThreshold = 48;

    public static async Task<List<PageDiff>> CompareAsync(string oldPath, string newPath, IProgress<(int, int)>? progress, CancellationToken ct)
    {
        using var a = RendererFactory.Create();
        using var b = RendererFactory.Create();
        await a.LoadAsync(oldPath);
        await b.LoadAsync(newPath);
        int pages = Math.Max(a.PageCount, b.PageCount);
        var result = new List<PageDiff>();
        for (int i = 0; i < pages; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((i, pages));
            BitmapSource? oa = i < a.PageCount ? await a.RenderPageAsync(i, 1.0) : null;
            BitmapSource? nb = i < b.PageCount ? await b.RenderPageAsync(i, 1.0) : null;
            result.Add(Diff(i + 1, oa, nb));
        }
        progress?.Report((pages, pages));
        return result;
    }

    private static byte[] Gray(BitmapSource? src, int w, int h)
    {
        var buf = Enumerable.Repeat((byte)255, w * h).ToArray();
        if (src == null) return buf;
        var g = new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0);
        int sw = g.PixelWidth, sh = g.PixelHeight;
        var tmp = new byte[sw * sh];
        g.CopyPixels(tmp, sw, 0);
        for (int y = 0; y < Math.Min(h, sh); y++)
            Buffer.BlockCopy(tmp, y * sw, buf, y * w, Math.Min(w, sw));
        return buf;
    }

    private static PageDiff Diff(int page, BitmapSource? oldBmp, BitmapSource? newBmp)
    {
        int w = Math.Max(oldBmp?.PixelWidth ?? 0, newBmp?.PixelWidth ?? 0);
        int h = Math.Max(oldBmp?.PixelHeight ?? 0, newBmp?.PixelHeight ?? 0);
        if (w == 0 || h == 0) { w = h = 1; }
        var ga = Gray(oldBmp, w, h);
        var gb = Gray(newBmp, w, h);
        var px = new byte[w * h * 4];
        int changed = 0, ink = 0;
        for (int i = 0; i < w * h; i++)
        {
            byte va = ga[i], vb = gb[i];
            bool inkA = va < InkThreshold, inkB = vb < InkThreshold;
            if (inkA || inkB) ink++;
            int o = i * 4;
            if (Math.Abs(va - vb) > DiffThreshold && (inkA || inkB))
            {
                changed++;
                if (inkA && !inkB) { px[o] = 0x3B; px[o + 1] = 0x3B; px[o + 2] = 0xE5; }       // removed: red (BGR)
                else if (inkB && !inkA) { px[o] = 0x4E; px[o + 1] = 0xB0; px[o + 2] = 0x2E; }  // added: green
                else { px[o] = 0x00; px[o + 1] = 0x8C; px[o + 2] = 0xE6; }                       // changed: orange
                px[o + 3] = 255;
            }
            else
            {
                byte faded = (byte)(255 - (255 - vb) * 0.25); // unchanged content, faded
                px[o] = px[o + 1] = px[o + 2] = faded;
                px[o + 3] = 255;
            }
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        double pct = ink == 0 ? 0 : 100.0 * changed / ink;
        return new PageDiff(page, oldBmp, newBmp, bmp, Math.Round(pct, 1));
    }
}
