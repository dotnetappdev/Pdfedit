using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PdfEdit.Services;

/// <summary>
/// Makes the native Windows title bar (caption, minimise / maximise / close
/// buttons) of every window follow the app theme, so dialogs don't show a
/// white caption on a dark window. Uses DWM, best-effort: older Windows simply
/// keeps its default caption.
/// </summary>
public static class TitleBarTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19; // Windows 10 1809–1909
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;     // Windows 10 2004+ / 11
    private const int DWMWA_CAPTION_COLOR = 35;               // Windows 11
    private const int DWMWA_TEXT_COLOR = 36;                  // Windows 11
    private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint value, int size);

    private static bool _registered;

    /// <summary>Hooks every window created from now on.</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (s is Window w) Apply(w); }));
    }

    /// <summary>Re-applies the current theme to every open window (after a theme switch).</summary>
    public static void ApplyAll()
    {
        if (Application.Current == null) return;
        foreach (Window w in Application.Current.Windows)
            Apply(w);
    }

    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            bool dark = App.IsDarkTheme;
            int useDark = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, sizeof(int));

            // Windows 11: paint the caption with the window's own panel colour so
            // the title bar blends into the dialog (no-op on Windows 10).
            uint caption = DWMWA_COLOR_DEFAULT, text = DWMWA_COLOR_DEFAULT;
            if (dark && Application.Current?.TryFindResource("PanelBgColor") is Color bg)
            {
                caption = ToColorRef(bg);
                if (Application.Current.TryFindResource("ForegroundColor") is Color fg)
                    text = ToColorRef(fg);
            }
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(uint));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(uint));
        }
        catch
        {
            // Non-fatal: the window keeps the default Windows caption.
        }
    }

    // COLORREF is 0x00BBGGRR.
    private static uint ToColorRef(Color c) => (uint)(c.R | (c.G << 8) | (c.B << 16));
}
