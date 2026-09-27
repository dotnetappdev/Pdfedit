using System.Windows;
using PdfEdit.Models;
using Xunit;

namespace PdfEdit.Tests;

/// <summary>Design-view geometry: freehand strokes follow move/resize, lines keep their drawn direction.</summary>
public class DesignElementGeometryTests
{
    private static FreehandDesignElement MakeStroke() => new()
    {
        // Drawn from (100,100) to (150,120): bounds 50 x 20 at (100,100)
        X = 100, Y = 100, Width = 50, Height = 20,
        Strokes = { new List<Point> { new(100, 100), new(150, 120) } },
    };

    [Fact]
    public void Freehand_Unmoved_ReturnsOriginalPoints()
    {
        var pts = MakeStroke().GetTransformedStrokes().Single().ToList();
        Assert.Equal(new Point(100, 100), pts[0]);
        Assert.Equal(new Point(150, 120), pts[1]);
    }

    [Fact]
    public void Freehand_MovedAndResized_PointsFollowBounds()
    {
        var fh = MakeStroke();
        fh.X = 10; fh.Y = 20; fh.Width = 100; fh.Height = 40;

        var pts = fh.GetTransformedStrokes().Single().ToList();
        Assert.Equal(new Point(10, 20), pts[0]);
        Assert.Equal(new Point(110, 60), pts[1]);

        var local = fh.GetLocalStrokes().Single().ToList();
        Assert.Equal(new Point(0, 0), local[0]);
        Assert.Equal(new Point(100, 40), local[1]);
    }

    [Theory]
    [InlineData(false, false, 0, 0, 80, 30)]
    [InlineData(true,  false, 80, 0, 0, 30)]
    [InlineData(false, true,  0, 30, 80, 0)]
    [InlineData(true,  true,  80, 30, 0, 0)]
    public void Line_FlipFlags_SetDirection(bool flipX, bool flipY, double sx, double sy, double ex, double ey)
    {
        var line = new ShapeDesignElement(DesignElementType.Arrow) { Width = 80, Height = 30, FlipX = flipX, FlipY = flipY };
        var (start, end) = line.GetLocalEndpoints();
        Assert.Equal(new Point(sx, sy), start);
        Assert.Equal(new Point(ex, ey), end);
    }
}
