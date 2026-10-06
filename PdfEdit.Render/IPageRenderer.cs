namespace PdfEdit.Render;

/// <summary>A PDF page renderer that doesn't depend on any UI framework.</summary>
public interface IPageRenderer : IDisposable
{
    int PageCount { get; }

    /// <summary>Whether this renderer bakes PDF annotations (stamps, watermarks, ink) into the page.</summary>
    bool RendersAnnotations { get; }

    Task LoadAsync(string absolutePath);

    /// <summary>
    /// Renders a page. zoom 1.0 = 100% at 96 pixels per inch; dpiScale = physical pixels per
    /// logical pixel (1.5 on a 150% display), for a sharp result on high-DPI screens.
    /// </summary>
    Task<RenderedPage> RenderPageAsync(int pageIndex, double zoom = 1.0, double dpiScale = 1.0);

    /// <summary>Unrotated MediaBox size in PDF points (72 per inch), whatever the page's /Rotate.</summary>
    (double WidthPts, double HeightPts) GetPageSizeInPoints(int pageIndex);

    /// <summary>The page's /Rotate value (0, 90, 180 or 270).</summary>
    int GetPageRotation(int pageIndex);
}

public static class RenderUnits
{
    /// <summary>PDF points (72 per inch) to device-independent pixels (96 per inch).</summary>
    public const double PointsToDips = 96.0 / 72.0;
}
