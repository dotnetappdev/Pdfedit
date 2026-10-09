using Avalonia;

namespace PdfEdit.Avalonia;

internal static class Program
{
    /// <summary>PDFs given on the command line (or with Open with) open when the window is ready.</summary>
    public static string[] FilesToOpen { get; private set; } = [];

    // WebView2 on Windows needs the UI thread to be single-threaded (STA).
    [STAThread]
    public static void Main(string[] args)
    {
        FilesToOpen = args.Where(a => !a.StartsWith('-') && File.Exists(a)).Select(Path.GetFullPath).ToArray();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
        // PDFEDIT_SOFTWARE_RENDERING=1 draws the window without the GPU (for Macs without one, such
        // as the virtual Macs that take the screenshots).
        if (Environment.GetEnvironmentVariable("PDFEDIT_SOFTWARE_RENDERING") == "1")
            builder = builder.With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] });
        return builder;
    }
}
