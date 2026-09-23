using System.Windows.Media.Imaging;

namespace PdfEdit.Services;

/// <summary>
/// Common interface for PDF page renderers.
/// Implementations: PdfRenderService (WinRT fallback) and PdfiumRenderEngine (Pdfium/Adobe quality).
/// </summary>
public interface IPdfRenderer : IDisposable
{
    int PageCount { get; }

    /// <summary>Whether this renderer bakes PDF annotations (stamps, watermarks, ink) into the page bitmap.</summary>
    bool RendersAnnotations { get; }

    Task LoadAsync(string absolutePath);

    /// <summary>
    /// Renders a page to a frozen BitmapSource.
    /// zoom 1.0 = 100% at 96 WPF DIP/inch.
    /// dpiScale = physical pixels per logical pixel (1.0 on 96 DPI, 1.5 on 144 DPI/150%).
    /// Pass dpiScale > 1 to get a crisp bitmap on high-DPI displays.
    /// </summary>
    Task<BitmapSource> RenderPageAsync(int pageIndex, double zoom = 1.0, double dpiScale = 1.0);

    /// <summary>
    /// Returns unrotated page size in PDF points (72 pts = 1 inch).
    /// Always returns the MediaBox dimensions regardless of the page's /Rotate value,
    /// so that form-field Y-coordinate math (pageHeightPts - field.Bottom) is always correct.
    /// </summary>
    (double WidthPts, double HeightPts) GetPageSizeInPoints(int pageIndex);

    /// <summary>Returns the page's /Rotate value (0, 90, 180, or 270).</summary>
    int GetPageRotation(int pageIndex);
}
