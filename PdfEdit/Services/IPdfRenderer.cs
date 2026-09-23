using System.Windows.Media.Imaging;

namespace PdfEdit.Services;

/// <summary>
/// Common interface for PDF page renderers.
/// Implementations: PdfRenderService (WinRT) and PdfiumRenderEngine (Pdfium/Chrome quality).
/// </summary>
public interface IPdfRenderer : IDisposable
{
    int PageCount { get; }

    Task LoadAsync(string absolutePath);

    /// <summary>Renders a page to a frozen BitmapSource. zoom 1.0 = 96 DPI.</summary>
    Task<BitmapSource> RenderPageAsync(int pageIndex, double zoom = 1.0);

    /// <summary>Returns page size in PDF points (72 pts = 1 inch).</summary>
    (double WidthPts, double HeightPts) GetPageSizeInPoints(int pageIndex);
}
