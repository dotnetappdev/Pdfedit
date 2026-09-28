using System.Windows;

namespace PdfEdit.Services;

/// <summary>Visual Studio / Acrobat style layout commands for a group of controls.</summary>
public enum ArrangeOperation
{
    AlignLefts, AlignCenters, AlignRights,
    AlignTops, AlignMiddles, AlignBottoms,
    DistributeHorizontally, DistributeVertically,
    MakeSameWidth, MakeSameHeight, MakeSameSize,
    CenterOnPageHorizontally, CenterOnPageVertically,
}

/// <summary>
/// Pure layout math shared by the live view (form fields) and the design canvas. Works in
/// top-left (y-down) coordinates. With two or more rectangles, alignment and sizing use
/// <c>reference</c> (the primary selection, like Visual Studio); with a single rectangle the
/// alignment commands align it to the page instead.
/// </summary>
public static class ArrangeHelper
{
    public static bool NeedsTwo(ArrangeOperation op) => op is ArrangeOperation.MakeSameWidth
        or ArrangeOperation.MakeSameHeight or ArrangeOperation.MakeSameSize;

    public static bool NeedsThree(ArrangeOperation op) => op is ArrangeOperation.DistributeHorizontally
        or ArrangeOperation.DistributeVertically;

    public static Rect[] Arrange(IReadOnlyList<Rect> rects, int reference, ArrangeOperation op, Size page)
    {
        var result = rects.ToArray();
        if (result.Length == 0) return result;
        reference = Math.Clamp(reference, 0, result.Length - 1);

        // A single item aligns to the page.
        Rect r0 = result.Length == 1 ? new Rect(0, 0, page.Width, page.Height) : result[reference];

        switch (op)
        {
            case ArrangeOperation.AlignLefts:   Each(result, r => Move(r, r0.Left, r.Top)); break;
            case ArrangeOperation.AlignRights:  Each(result, r => Move(r, r0.Right - r.Width, r.Top)); break;
            case ArrangeOperation.AlignCenters: Each(result, r => Move(r, r0.Left + (r0.Width - r.Width) / 2, r.Top)); break;
            case ArrangeOperation.AlignTops:    Each(result, r => Move(r, r.Left, r0.Top)); break;
            case ArrangeOperation.AlignBottoms: Each(result, r => Move(r, r.Left, r0.Bottom - r.Height)); break;
            case ArrangeOperation.AlignMiddles: Each(result, r => Move(r, r.Left, r0.Top + (r0.Height - r.Height) / 2)); break;

            case ArrangeOperation.CenterOnPageHorizontally:
            {
                // Move the group as a whole so its bounding box is centred on the page.
                var box = Bounds(result);
                double dx = (page.Width - box.Width) / 2 - box.Left;
                Each(result, r => Move(r, r.Left + dx, r.Top));
                break;
            }
            case ArrangeOperation.CenterOnPageVertically:
            {
                var box = Bounds(result);
                double dy = (page.Height - box.Height) / 2 - box.Top;
                Each(result, r => Move(r, r.Left, r.Top + dy));
                break;
            }

            case ArrangeOperation.MakeSameWidth:  if (result.Length > 1) Each(result, r => new Rect(r.Left, r.Top, r0.Width, r.Height)); break;
            case ArrangeOperation.MakeSameHeight: if (result.Length > 1) Each(result, r => new Rect(r.Left, r.Top, r.Width, r0.Height)); break;
            case ArrangeOperation.MakeSameSize:   if (result.Length > 1) Each(result, r => new Rect(r.Left, r.Top, r0.Width, r0.Height)); break;

            case ArrangeOperation.DistributeHorizontally: Distribute(result, horizontal: true); break;
            case ArrangeOperation.DistributeVertically:   Distribute(result, horizontal: false); break;
        }
        return result;
    }

    /// <summary>Equal gaps between neighbours; the outermost two items stay where they are.</summary>
    private static void Distribute(Rect[] rects, bool horizontal)
    {
        if (rects.Length < 3) return;
        var order = Enumerable.Range(0, rects.Length)
            .OrderBy(i => horizontal ? rects[i].Left : rects[i].Top).ToArray();
        double start = horizontal ? rects[order[0]].Left : rects[order[0]].Top;
        double end   = horizontal ? rects[order[^1]].Right : rects[order[^1]].Bottom;
        double total = order.Sum(i => horizontal ? rects[i].Width : rects[i].Height);
        double gap = (end - start - total) / (rects.Length - 1);

        double pos = start;
        foreach (int i in order)
        {
            var r = rects[i];
            rects[i] = horizontal ? Move(r, pos, r.Top) : Move(r, r.Left, pos);
            pos += (horizontal ? r.Width : r.Height) + gap;
        }
    }

    private static Rect Bounds(Rect[] rects)
    {
        var b = rects[0];
        foreach (var r in rects.Skip(1)) b.Union(r);
        return b;
    }

    private static Rect Move(Rect r, double left, double top) => new(left, top, r.Width, r.Height);

    private static void Each(Rect[] rects, Func<Rect, Rect> f)
    {
        for (int i = 0; i < rects.Length; i++) rects[i] = f(rects[i]);
    }
}
