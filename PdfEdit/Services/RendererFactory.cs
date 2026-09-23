using PdfEdit.Engine;

namespace PdfEdit.Services;

/// <summary>
/// Creates the active IPdfRenderer from the current AppSettings.RenderEngine setting.
/// RenderEngine values: "Custom" (default, pure C#), "Pdfium", "WinRT".
/// </summary>
public static class RendererFactory
{
    // PDF user-space is 72 pt/inch; WPF is 96 DIP/inch.
    public const double PointsToDips = 96.0 / 72.0; // ~1.3333

    public static IPdfRenderer Create() =>
        AppSettings.Current.RenderEngine switch
        {
            "WinRT"  => new PdfRenderService(),
            "Pdfium" => new PdfiumRenderEngine(),
            _        => new CustomPdfEngine(),   // "Custom" is the default
        };
}
