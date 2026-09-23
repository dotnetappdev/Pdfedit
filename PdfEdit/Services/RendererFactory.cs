namespace PdfEdit.Services;

/// <summary>
/// Creates the active IPdfRenderer from the current AppSettings.RenderEngine setting.
/// Also exposes the shared PointsToDips constant so callers don't need to reference
/// a specific renderer class.
/// </summary>
public static class RendererFactory
{
    // PDF user-space is 72 pt/inch; WPF is 96 DIP/inch.
    public const double PointsToDips = 96.0 / 72.0; // ~1.3333

    public static IPdfRenderer Create()
    {
        return AppSettings.Current.RenderEngine == "WinRT"
            ? new PdfRenderService()
            : new PdfiumRenderEngine();
    }
}
