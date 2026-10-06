using PdfEdit.Drawing.Wpf;
using PdfEdit.Render;
using PdfEdit.Render.Engine;

namespace PdfEdit.Services;

/// <summary>
/// Creates the active renderer from the current AppSettings.RenderEngine setting.
/// RenderEngine values: "Custom" (default, pure C#), "Pdfium", "WinRT".
/// The engines live in PdfEdit.Render; PdfEdit.Drawing.Wpf draws them with WPF.
/// </summary>
public static class RendererFactory
{
    // PDF user-space is 72 pt/inch; WPF is 96 DIP/inch.
    public const double PointsToDips = RenderUnits.PointsToDips; // ~1.3333

    public static IPdfRenderer Create() =>
        AppSettings.Current.RenderEngine switch
        {
            "WinRT"  => new WinRtPdfRenderer(),
            "Pdfium" => new WpfPdfRenderer(new PdfiumRenderEngine()),
            _        => new WpfPdfRenderer(new CustomPdfEngine(WpfDrawingBackend.Instance)),   // "Custom" is the default
        };
}
