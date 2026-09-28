using System.Windows;
using PdfEdit.Services;
using Xunit;

namespace PdfEdit.Tests;

/// <summary>Align / distribute / size maths shared by the live view and the design canvas.</summary>
public class ArrangeHelperTests
{
    private static readonly Size Page = new(600, 800);

    // Reference (primary selection) is the last rect.
    private static readonly Rect[] Three =
    {
        new(10, 10, 100, 20),
        new(50, 60, 80, 30),
        new(200, 100, 120, 40),
    };

    private static Rect[] Run(ArrangeOperation op, Rect[]? src = null, int reference = 2)
        => ArrangeHelper.Arrange(src ?? Three, reference, op, Page);

    [Fact]
    public void AlignLefts_UsesReference()
        => Assert.All(Run(ArrangeOperation.AlignLefts), r => Assert.Equal(200, r.Left));

    [Fact]
    public void AlignRights_UsesReference()
        => Assert.All(Run(ArrangeOperation.AlignRights), r => Assert.Equal(320, r.Right));

    [Fact]
    public void AlignMiddles_UsesReference()
        => Assert.All(Run(ArrangeOperation.AlignMiddles), r => Assert.Equal(120, r.Top + r.Height / 2));

    [Fact]
    public void AlignBottoms_UsesReference()
        => Assert.All(Run(ArrangeOperation.AlignBottoms), r => Assert.Equal(140, r.Bottom));

    [Fact]
    public void DistributeHorizontally_EqualGaps_EndsFixed()
    {
        var r = Run(ArrangeOperation.DistributeHorizontally);
        Assert.Equal(10, r[0].Left);
        Assert.Equal(115, r[1].Left);  // gap of 5 either side
        Assert.Equal(200, r[2].Left);
    }

    [Fact]
    public void DistributeVertically_EqualGaps_EndsFixed()
    {
        var r = Run(ArrangeOperation.DistributeVertically);
        Assert.Equal(10, r[0].Top);
        Assert.Equal(50, r[1].Top);    // gap of 20 either side
        Assert.Equal(100, r[2].Top);
    }

    [Fact]
    public void MakeSameSize_CopiesReferenceSize_KeepsPositions()
    {
        var r = Run(ArrangeOperation.MakeSameSize);
        Assert.All(r, x => { Assert.Equal(120, x.Width); Assert.Equal(40, x.Height); });
        Assert.Equal(new Point(50, 60), new Point(r[1].Left, r[1].Top));
    }

    [Fact]
    public void CenterOnPage_MovesGroupAsAWhole()
    {
        var r = Run(ArrangeOperation.CenterOnPageHorizontally);
        Assert.Equal(145, r[0].Left);
        Assert.Equal(455, r[2].Right); // group box 145..455 is centred on a 600-wide page
        Assert.Equal(r[1].Left - r[0].Left, 40); // relative layout preserved
    }

    [Fact]
    public void SingleItem_AlignsToPage()
    {
        var one = new[] { new Rect(10, 10, 100, 20) };
        Assert.Equal(500, Run(ArrangeOperation.AlignRights, one, 0)[0].Left);
        Assert.Equal(390, Run(ArrangeOperation.AlignMiddles, one, 0)[0].Top);
    }

    [Fact]
    public void Distribute_NeedsThree_OtherwiseUnchanged()
    {
        var two = Three.Take(2).ToArray();
        Assert.Equal(two, Run(ArrangeOperation.DistributeHorizontally, two, 1));
    }
}
