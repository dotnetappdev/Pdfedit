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

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
