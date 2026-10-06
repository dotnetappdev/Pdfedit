namespace PdfEdit.Services;

/// <summary>A form field found on a flat page (pixel rectangle, top-left origin).</summary>
public sealed record DetectedBox(int Left, int Top, int Right, int Bottom, bool IsCheckBox, bool IsUnderline)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// Finds where the fields of a flat (printed) form are, like Acrobat's Prepare Form auto-detect:
/// ruled boxes become text fields, small squares become check boxes, and long underlines with
/// blank space above become text fields. Works on a grey-scale render of the page.
/// </summary>
public static class FieldDetector
{
    private const int Ink = 200;

    /// <param name="pxPerPt">Render scale (pixels per PDF point).</param>
    public static List<DetectedBox> Detect(byte[] lum, int width, int height, double pxPerPt)
    {
        bool IsInk(int x, int y) => lum[y * width + x] < Ink;
        int Pt(double pt) => (int)Math.Round(pt * pxPerPt);

        // 1. Horizontal ruled lines (runs of ink at least 14 pt long), merged across thick rows.
        var segs = new List<(int X0, int X1, int Y)>();
        int minRun = Pt(14);
        for (int y = 1; y < height - 1; y++)
        {
            int x = 0;
            while (x < width)
            {
                if (!IsInk(x, y)) { x++; continue; }
                int s = x;
                while (x < width && IsInk(x, y)) x++;
                if (x - s >= minRun)
                {
                    // A line is thin: the rows a few pixels above and below are mostly paper.
                    int above = Math.Max(0, y - Pt(3)), below = Math.Min(height - 1, y + Pt(3));
                    int inkAbove = 0, inkBelow = 0, n = 0;
                    for (int xx = s; xx < x; xx += 2, n++) { if (IsInk(xx, above)) inkAbove++; if (IsInk(xx, below)) inkBelow++; }
                    if (inkAbove < n * 0.5 || inkBelow < n * 0.5)
                        if (!segs.Any(g => Math.Abs(g.Y - y) <= 3 && Math.Abs(g.X0 - s) <= 3 && Math.Abs(g.X1 - x) <= 3))
                            segs.Add((s, x, y));
                }
            }
        }

        // 2. Boxes: start the box finder just below each line, every ~30 pt along it (table cells).
        var boxes = new List<DetectedBox>();
        int maxW = Pt(560), maxH = Pt(160), step = Math.Max(4, Pt(30));
        foreach (var (x0, x1, y) in segs)
        {
            for (int sx = x0 + Pt(4); sx < x1 - Pt(2); sx += step)
            foreach (int dy in new[] { Pt(4), Pt(8) })
            {
                int sy = y + dy;
                if (sy >= height - 1) continue;
                if (BoxDetector.Find(lum, width, height, sx, sy, maxW, maxH) is not { } b) continue;
                var box = new DetectedBox(b.Left, b.Top, b.Right, b.Bottom, false, false);
                if (boxes.Any(o => Overlap(o, box) > 0.8)) continue;
                boxes.Add(box);
            }
        }

        // Keep the innermost boxes (drop panels that just frame other boxes), then classify.
        var result = new List<DetectedBox>();
        foreach (var b in boxes)
        {
            if (boxes.Any(o => !ReferenceEquals(o, b) && Contains(b, o))) continue;
            double wPt = b.Width / pxPerPt, hPt = b.Height / pxPerPt;
            bool square = wPt <= 26 && hPt <= 26 && wPt / Math.Max(1, hPt) is > 0.6 and < 1.6;
            if (square) { result.Add(b with { IsCheckBox = true }); continue; }
            if (hPt < 9 || hPt > 140 || wPt < 24) continue;
            if (!MostlyEmpty(lum, width, b.Left + 2, b.Top + 2, b.Right - 2, b.Bottom - 2)) continue; // already has text
            result.Add(b);
        }

        // 3. Underlines with blank space above that aren't the edge of a box: fill-in-the-blank lines.
        foreach (var (x0, x1, y) in segs)
        {
            if (x1 - x0 < Pt(48)) continue;
            // Skip the top / bottom edges of boxes (kept or not): those aren't blanks to fill in.
            bool boxEdge = boxes.Any(b => x0 >= b.Left - Pt(6) && x1 <= b.Right + Pt(6)
                                          && (Math.Abs(y - b.Bottom) < Pt(5) || Math.Abs(y - b.Top) < Pt(5)));
            if (boxEdge) continue;
            int top = y - Pt(16);
            if (top < 0 || !MostlyEmpty(lum, width, x0 + 2, top, x1 - 2, y - Pt(2))) continue;
            var u = new DetectedBox(x0, top, x1, y - 1, false, true);
            if (result.Any(o => Overlap(o, u) > 0.3)) continue;
            result.Add(u);
        }
        return result.OrderBy(b => b.Top / Math.Max(1, Pt(6))).ThenBy(b => b.Left).ToList();

        bool MostlyEmpty(byte[] l, int w, int left, int t, int r, int bottom)
        {
            int ink = 0, n = 0;
            for (int yy = Math.Max(0, t); yy < Math.Min(height, bottom); yy += 2)
                for (int xx = Math.Max(0, left); xx < Math.Min(w, r); xx += 2, n++)
                    if (l[yy * w + xx] < Ink) ink++;
            return n > 0 && ink < n * 0.02;
        }
    }

    private static bool Contains(DetectedBox outer, DetectedBox inner) =>
        inner.Left >= outer.Left - 1 && inner.Right <= outer.Right + 1 && inner.Top >= outer.Top - 1 && inner.Bottom <= outer.Bottom + 1
        && (inner.Width < outer.Width - 4 || inner.Height < outer.Height - 4);

    private static double Overlap(DetectedBox a, DetectedBox b)
    {
        int w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left), h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        if (w <= 0 || h <= 0) return 0;
        double inter = (double)w * h, small = Math.Min((double)a.Width * a.Height, (double)b.Width * b.Height);
        return small <= 0 ? 0 : inter / small;
    }
}
