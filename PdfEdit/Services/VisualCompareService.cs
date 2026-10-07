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
        // The pixel comparison is shared with the web version (PdfEdit.Core's VisualDiff).
        var (px, pct) = VisualDiff.Diff(Gray(oldBmp, w, h), Gray(newBmp, w, h), w, h);
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return new PageDiff(page, oldBmp, newBmp, bmp, pct);
    }
}
