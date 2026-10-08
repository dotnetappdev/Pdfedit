namespace PdfEdit.Blazor.Services;

/// <summary>The Windows app's Arrange commands (Visual Studio / Acrobat style), for fields and design elements.</summary>
public enum ArrangeOp
{
    AlignLefts, AlignCenters, AlignRights,
    AlignTops, AlignMiddles, AlignBottoms,
    DistributeHorizontally, DistributeVertically,
    MakeSameWidth, MakeSameHeight, MakeSameSize,
    CenterOnPage,
}

/// <summary>A box from the top-left (y down).</summary>
public readonly record struct Box(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public Box MoveTo(double left, double top) => this with { Left = left, Top = top };
}

/// <summary>
/// The layout maths of the Windows app's ArrangeHelper. With two or more boxes, alignment and
/// sizing follow <c>reference</c> (the last one clicked); a single box aligns to the page.
/// </summary>
public static class Arrange
{
    public static readonly (ArrangeOp Op, string Label, string Icon, string Hint)[] Commands =
    [
        (ArrangeOp.AlignLefts, "Lefts", "bi-align-start", "Align lefts"),
        (ArrangeOp.AlignCenters, "Centres", "bi-align-center", "Align centres"),
        (ArrangeOp.AlignRights, "Rights", "bi-align-end", "Align rights"),
        (ArrangeOp.AlignTops, "Tops", "bi-align-top", "Align tops"),
        (ArrangeOp.AlignMiddles, "Middles", "bi-align-middle", "Align middles"),
        (ArrangeOp.AlignBottoms, "Bottoms", "bi-align-bottom", "Align bottoms"),
        (ArrangeOp.DistributeHorizontally, "Space Across", "bi-distribute-horizontal", "Distribute horizontally — equal gaps (3+ selected)"),
        (ArrangeOp.DistributeVertically, "Space Down", "bi-distribute-vertical", "Distribute vertically — equal gaps (3+ selected)"),
        (ArrangeOp.MakeSameWidth, "Same Width", "bi-arrows", "Make the same width as the last one clicked"),
        (ArrangeOp.MakeSameHeight, "Same Height", "bi-arrows-vertical", "Make the same height as the last one clicked"),
        (ArrangeOp.MakeSameSize, "Same Size", "bi-arrows-angle-expand", "Make the same size as the last one clicked"),
        (ArrangeOp.CenterOnPage, "Center Page", "bi-symmetry-vertical", "Centre the selection horizontally on the page"),
    ];

    public static Box[] Apply(IReadOnlyList<Box> boxes, int reference, ArrangeOp op, double pageWidth, double pageHeight)
    {
        var r = boxes.ToArray();
        if (r.Length == 0) return r;
        reference = Math.Clamp(reference, 0, r.Length - 1);
        var r0 = r.Length == 1 ? new Box(0, 0, pageWidth, pageHeight) : r[reference];
        switch (op)
        {
            case ArrangeOp.AlignLefts: Each(r, b => b.MoveTo(r0.Left, b.Top)); break;
            case ArrangeOp.AlignRights: Each(r, b => b.MoveTo(r0.Right - b.Width, b.Top)); break;
            case ArrangeOp.AlignCenters: Each(r, b => b.MoveTo(r0.Left + (r0.Width - b.Width) / 2, b.Top)); break;
            case ArrangeOp.AlignTops: Each(r, b => b.MoveTo(b.Left, r0.Top)); break;
            case ArrangeOp.AlignBottoms: Each(r, b => b.MoveTo(b.Left, r0.Bottom - b.Height)); break;
            case ArrangeOp.AlignMiddles: Each(r, b => b.MoveTo(b.Left, r0.Top + (r0.Height - b.Height) / 2)); break;
            case ArrangeOp.CenterOnPage:
                double left = r.Min(b => b.Left), right = r.Max(b => b.Right);
                double dx = (pageWidth - (right - left)) / 2 - left;
                Each(r, b => b.MoveTo(b.Left + dx, b.Top));
                break;
            case ArrangeOp.MakeSameWidth: if (r.Length > 1) Each(r, b => b with { Width = r0.Width }); break;
            case ArrangeOp.MakeSameHeight: if (r.Length > 1) Each(r, b => b with { Height = r0.Height }); break;
            case ArrangeOp.MakeSameSize: if (r.Length > 1) Each(r, b => b with { Width = r0.Width, Height = r0.Height }); break;
            case ArrangeOp.DistributeHorizontally: Distribute(r, true); break;
            case ArrangeOp.DistributeVertically: Distribute(r, false); break;
        }
        return r;
    }

    /// <summary>Why a command can't run on <paramref name="count"/> selected boxes (null when it can).</summary>
    public static string? Needs(ArrangeOp op, int count) => op switch
    {
        _ when count == 0 => "Select something first.",
        ArrangeOp.DistributeHorizontally or ArrangeOp.DistributeVertically when count < 3 => "Select three or more (Ctrl+click) to space them out.",
        ArrangeOp.MakeSameWidth or ArrangeOp.MakeSameHeight or ArrangeOp.MakeSameSize when count < 2 => "Select two or more (Ctrl+click) to make them the same size.",
        _ => null,
    };

    private static void Distribute(Box[] r, bool horizontal)
    {
        if (r.Length < 3) return;
        var order = Enumerable.Range(0, r.Length).OrderBy(i => horizontal ? r[i].Left : r[i].Top).ToArray();
        double start = horizontal ? r[order[0]].Left : r[order[0]].Top;
        double end = horizontal ? r[order[^1]].Right : r[order[^1]].Bottom;
        double total = order.Sum(i => horizontal ? r[i].Width : r[i].Height);
        double gap = (end - start - total) / (r.Length - 1), pos = start;
        foreach (int i in order)
        {
            var b = r[i];
            r[i] = horizontal ? b.MoveTo(pos, b.Top) : b.MoveTo(b.Left, pos);
            pos += (horizontal ? b.Width : b.Height) + gap;
        }
    }

    private static void Each(Box[] r, Func<Box, Box> f)
    {
        for (int i = 0; i < r.Length; i++) r[i] = f(r[i]);
    }
}
