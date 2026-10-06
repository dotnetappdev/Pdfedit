namespace PdfEdit.Services;

/// <summary>
/// Finds the drawn box (rectangle outline) around a point on a rendered page, the way Acrobat
/// Fill &amp; Sign detects the boxes of a flat (non-fillable) form so you can click in one and type.
/// Works on a grey-scale copy of the rendered page, so it finds boxes however they were drawn
/// (rectangles, four lines, table cells).
/// </summary>
public static class BoxDetector
{
    // Luminance (0–255) below which a pixel counts as part of a ruled line. Light-grey rules
    // (anti-aliased over two pixels) still fall well below this; white paper is ~255.
    private const int InkThreshold = 215;
    // The clicked spot must be (near) empty paper, otherwise it's text or artwork, not a box.
    private const int PaperThreshold = 230;
    // Fraction of an edge that must be ink for it to count as a ruled line.
    private const double EdgeCoverage = 0.85;

    /// <summary>
    /// The inside of the box containing (<paramref name="px"/>, <paramref name="py"/>) as
    /// (left, top, right, bottom) pixel coordinates (exclusive of the border), or null when the
    /// point is not inside a ruled box no bigger than <paramref name="maxW"/> × <paramref name="maxH"/>.
    /// </summary>
    /// <param name="lum">Row-major luminance, <paramref name="width"/> × <paramref name="height"/>.</param>
    public static (int Left, int Top, int Right, int Bottom)? Find(
        byte[] lum, int width, int height, int px, int py, int maxW, int maxH)
    {
        // A mark already in the box (a tick, a letter) can block the walk from the exact click
        // point, so also try a few nearby starting points before giving up.
        foreach (var (dx, dy) in new[] { (0, 0), (-6, 0), (6, 0), (-14, 0), (14, 0), (0, -3), (0, 3) })
            if (FindFrom(lum, width, height, px + dx, py + dy, maxW, maxH) is { } box
                && px >= box.Left - 1 && px <= box.Right + 1 && py >= box.Top - 1 && py <= box.Bottom + 1)
                return box;
        return null;
    }

    private static (int Left, int Top, int Right, int Bottom)? FindFrom(
        byte[] lum, int width, int height, int px, int py, int maxW, int maxH)
    {
        if (px < 1 || py < 1 || px >= width - 1 || py >= height - 1) return null;
        if (lum.Length < width * height) return null;
        if (lum[py * width + px] < PaperThreshold) return null;

        bool Ink(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && lum[y * width + x] < InkThreshold;

        // Walk out from the click to the first ink in each direction.
        int l = px; while (l > 0 && !Ink(l, py) && px - l <= maxW) l--;
        int r = px; while (r < width - 1 && !Ink(r, py) && r - px <= maxW) r++;
        int t = py; while (t > 0 && !Ink(px, t) && py - t <= maxH) t--;
        int b = py; while (b < height - 1 && !Ink(px, b) && b - py <= maxH) b++;
        if (!Ink(l, py) || !Ink(r, py) || !Ink(px, t) || !Ink(px, b)) return null;

        int innerW = r - l - 1, innerH = b - t - 1;
        if (innerW < 4 || innerH < 4 || innerW > maxW || innerH > maxH) return null;

        // Each side must be a ruled line along (nearly) the whole edge — this rejects text,
        // underlines and artwork that the walk happened to hit. Lines may be 1–3 px thick.
        bool VerticalLine(int x, int dir)
        {
            int hits = 0, n = 0;
            for (int y = t + 2; y <= b - 2; y++, n++)
                if (Ink(x, y) || Ink(x + dir, y) || Ink(x + 2 * dir, y)) hits++;
            return n > 0 && hits >= n * EdgeCoverage;
        }
        bool HorizontalLine(int y, int dir)
        {
            int hits = 0, n = 0;
            for (int x = l + 2; x <= r - 2; x++, n++)
                if (Ink(x, y) || Ink(x, y + dir) || Ink(x, y + 2 * dir)) hits++;
            return n > 0 && hits >= n * EdgeCoverage;
        }
        if (!VerticalLine(l, -1) || !VerticalLine(r, +1) || !HorizontalLine(t, -1) || !HorizontalLine(b, +1))
            return null;

        return (l + 1, t + 1, r - 1, b - 1);
    }
}
