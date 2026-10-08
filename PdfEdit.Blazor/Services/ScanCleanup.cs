using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;

namespace PdfEdit.Blazor.Services;

/// <summary>What the clean-up found on one page.</summary>
public sealed record PageScan(int Index, bool Blank, double SkewDegrees, bool Spread, double InkPercent);

/// <summary>What to do to one page.</summary>
public sealed record PagePlan(int Index, bool Remove, double RotateDegrees, bool Split);

/// <summary>The three lines the clean-up shows, and which fixes there are to choose.</summary>
public sealed record ScanFindings(string Blank, string Skew, string Split, string Status,
                                  bool CanRemoveBlank, bool CanStraighten, bool CanSplit);

/// <summary>
/// Scan clean-up (Acrobat's Enhance Scans, PDFgear's page tools), ported from the Windows app:
/// finds blank pages, pages scanned crooked, and two-page spreads, then removes, straightens (by
/// rotating the page content about its centre) and splits them into single pages.
/// Pages are looked at as BGRA pixels from PdfEdit.Render; <c>RenderPageAsync(i, 1.0, 1.0)</c>
/// (about 96 dpi, as the Windows app uses) is plenty.
/// </summary>
public static class ScanCleanup
{
    /// <summary>A page counts as crooked from this many degrees.</summary>
    public const double MinSkew = 0.3;

    /// <summary>Looks at a rendered page: <paramref name="pixels"/> is BGRA, 4 bytes per pixel, rows top to bottom.</summary>
    public static PageScan Analyse(int index, byte[] pixels, int width, int height)
    {
        int w = width, h = height;
        var px = new byte[w * h];
        for (int p = 0, q = 0; p < px.Length; p++, q += 4)
            px[p] = q + 2 < pixels.Length ? (byte)((pixels[q] * 29 + pixels[q + 1] * 150 + pixels[q + 2] * 77) >> 8) : (byte)255;

        // Ink, ignoring a 4% margin where scanner edges and punch holes live.
        int mx = w * 4 / 100, my = h * 4 / 100, ink = 0, counted = 0;
        var darkX = new List<int>();
        var darkY = new List<int>();
        var columnInk = new int[w];
        for (int y = my; y < h - my; y++)
            for (int x = mx; x < w - mx; x++)
            {
                counted++;
                byte v = px[y * w + x];
                if (v < 200) { ink++; columnInk[x]++; }
                if (v < 140 && ((x + y) & 1) == 0) { darkX.Add(x); darkY.Add(y); }
            }
        double inkPct = counted == 0 ? 0 : ink * 100.0 / counted;
        bool blank = inkPct < 0.15;

        double skew = blank || darkX.Count < 400 ? 0 : EstimateSkew(darkX, darkY, h);
        bool spread = !blank && w > h * 1.25 && HasGutter(columnInk, w, mx);
        return new PageScan(index, blank, skew, spread, inkPct);
    }

    /// <summary>
    /// The slope of the text lines in degrees (image coordinates, positive = lines fall to the right):
    /// the angle at which the dark pixels pile up into the sharpest rows.
    /// </summary>
    private static double EstimateSkew(List<int> xs, List<int> ys, int height)
    {
        double Score(double deg)
        {
            double t = Math.Tan(deg * Math.PI / 180);
            int pad = height, bins = height * 3;
            var hist = new int[bins];
            for (int i = 0; i < xs.Count; i++)
            {
                int r = (int)(ys[i] - xs[i] * t) + pad;
                if (r >= 0 && r < bins) hist[r]++;
            }
            double s = 0;
            foreach (var c in hist) s += (double)c * c;
            return s;
        }
        double best = 0, bestScore = Score(0), zero = bestScore;
        for (double d = -6; d <= 6.001; d += 0.25)
        {
            double s = Score(d);
            if (s > bestScore) { bestScore = s; best = d; }
        }
        for (double d = best - 0.25; d <= best + 0.25; d += 0.05)
        {
            double s = Score(d);
            if (s > bestScore) { bestScore = s; best = d; }
        }
        // Only report a clear improvement over "straight".
        return bestScore > zero * 1.05 ? Math.Round(best, 2) : 0;
    }

    /// <summary>A landscape page with an empty band down the middle is two pages side by side.</summary>
    private static bool HasGutter(int[] columnInk, int w, int margin)
    {
        double avg = columnInk.Skip(margin).Take(w - 2 * margin).Average();
        if (avg <= 0) return false;
        int from = (int)(w * 0.44), to = (int)(w * 0.56), run = 0, best = 0;
        for (int x = from; x < to; x++)
        {
            run = columnInk[x] < avg * 0.08 ? run + 1 : 0;
            best = Math.Max(best, run);
        }
        // Both halves need content.
        double left = columnInk.Take(w / 2).Sum(), right = columnInk.Skip(w / 2).Sum();
        return best >= w * 0.01 && left > 0 && right > 0 && Math.Min(left, right) / Math.Max(left, right) > 0.15;
    }

    /// <summary>
    /// What the Windows dialog proposes for one page with its default ticks: remove it if blank,
    /// straighten it if crooked; spreads aren't split unless asked (a wide page may be meant to be wide).
    /// </summary>
    public static PagePlan Suggest(PageScan scan) =>
        new(scan.Index, scan.Blank, !scan.Blank && Math.Abs(scan.SkewDegrees) >= MinSkew ? scan.SkewDegrees : 0, false);

    /// <summary>
    /// The plans for the whole document with the given fixes ticked (the dialog's defaults are
    /// blank and skew on, split off). Never removes every page; pages with nothing to do are left out.
    /// </summary>
    public static List<PagePlan> Plan(IReadOnlyList<PageScan> scans, bool removeBlank = true, bool straighten = true, bool split = false)
    {
        bool anyContent = scans.Any(x => !x.Blank);
        var plans = new List<PagePlan>();
        foreach (var s in scans)
        {
            bool remove = removeBlank && s.Blank && anyContent;
            double rotate = straighten && !s.Blank && Math.Abs(s.SkewDegrees) >= MinSkew ? s.SkewDegrees : 0;
            bool cut = split && s.Spread;
            if (remove || rotate != 0 || cut) plans.Add(new PagePlan(s.Index, remove, rotate, cut));
        }
        return plans;
    }

    /// <summary>The dialog's wording for what was found.</summary>
    public static ScanFindings Describe(IReadOnlyList<PageScan> scans)
    {
        var blank = scans.Where(s => s.Blank).ToList();
        var crooked = scans.Where(s => !s.Blank && Math.Abs(s.SkewDegrees) >= MinSkew).ToList();
        var spreads = scans.Where(s => s.Spread).ToList();
        if (blank.Count == scans.Count) blank.Clear();   // never remove every page

        string blankText = blank.Count == 0 ? "No blank pages found" : $"Remove {blank.Count} blank page(s): {Pages(blank)}";
        string skewText = crooked.Count == 0 ? "No crooked pages found"
            : $"Straighten {crooked.Count} crooked page(s), up to {crooked.Max(s => Math.Abs(s.SkewDegrees)):0.#}°: {Pages(crooked)}";
        string splitText = spreads.Count == 0 ? "No two-page spreads found" : $"Split {spreads.Count} two-page spread(s): {Pages(spreads)}";
        bool any = blank.Count + crooked.Count + spreads.Count > 0;
        return new ScanFindings(blankText, skewText, splitText,
            any ? "Choose what to fix." : "Nothing to clean up — the pages look fine.",
            blank.Count > 0, crooked.Count > 0, spreads.Count > 0);
    }

    private static string Pages(List<PageScan> list) =>
        string.Join(", ", list.Take(12).Select(s => (s.Index + 1).ToString())) + (list.Count > 12 ? "…" : "");

    /// <summary>Writes the cleaned-up copy. Returns a description of what changed.</summary>
    public static string Apply(string source, string dest, IReadOnlyList<PagePlan> plans)
    {
        int removed = 0, straightened = 0, split = 0;
        using var src = new PdfDocument(new PdfReader(source));
        using var dst = new PdfDocument(new PdfWriter(dest));
        var byIndex = plans.ToDictionary(p => p.Index);
        for (int i = 0; i < src.GetNumberOfPages(); i++)
        {
            var plan = byIndex.TryGetValue(i, out var p) ? p : new PagePlan(i, false, 0, false);
            if (plan.Remove) { removed++; continue; }
            var page = src.GetPage(i + 1);
            bool canSplit = plan.Split && page.GetRotation() == 0;
            // Pages turned with /Rotate were measured turned; leave those as they are.
            double turn = page.GetRotation() == 0 ? plan.RotateDegrees : 0;
            int copies = canSplit ? 2 : 1;
            for (int c = 0; c < copies; c++)
            {
                var copy = page.CopyTo(dst);
                dst.AddPage(copy);
                if (Math.Abs(turn) >= 0.05) Straighten(dst, copy, turn);
                if (canSplit)
                {
                    var box = copy.GetCropBox();
                    float half = box.GetWidth() / 2;
                    var r = new Rectangle(box.GetX() + c * half, box.GetY(), half, box.GetHeight());
                    copy.SetCropBox(r);
                    copy.SetMediaBox(r);
                }
            }
            if (Math.Abs(turn) >= 0.05) straightened++;
            if (canSplit) split++;
        }
        var parts = new List<string>();
        if (removed > 0) parts.Add($"{removed} blank page(s) removed");
        if (straightened > 0) parts.Add($"{straightened} page(s) straightened");
        if (split > 0) parts.Add($"{split} spread(s) split");
        return parts.Count == 0 ? "No changes" : string.Join(", ", parts);
    }

    /// <summary>Rotates everything on the page by <paramref name="degrees"/> (anticlockwise) about its centre.</summary>
    private static void Straighten(PdfDocument pdf, PdfPage page, double degrees)
    {
        var box = page.GetCropBox();
        double cx = box.GetX() + box.GetWidth() / 2, cy = box.GetY() + box.GetHeight() / 2;
        double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        var before = new PdfCanvas(page.NewContentStreamBefore(), page.GetResources(), pdf);
        before.SaveState();
        before.ConcatMatrix(cos, sin, -sin, cos, cx - cos * cx + sin * cy, cy - sin * cx - cos * cy);
        new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdf).RestoreState();
    }
}
