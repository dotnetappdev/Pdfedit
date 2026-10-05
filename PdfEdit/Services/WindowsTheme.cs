using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PdfEdit.Services;

/// <summary>
/// What Windows 10 / 11 is set to: light or dark apps, a contrast theme (Aquatic, Desert, Dusk,
/// Night sky …) and the accent colour, and a notification when any of them changes.
/// </summary>
public static class WindowsTheme
{
    /// <summary>Raised (on the UI thread) when the Windows theme, contrast or accent changes.</summary>
    public static event Action? Changed;
    private static bool _listening;

    /// <summary>Settings → Personalisation → Colours → "Choose your app mode".</summary>
    public static bool AppsUseLightTheme
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
            }
            catch { return true; }
        }
    }

    /// <summary>A Windows contrast theme is on.</summary>
    public static bool HighContrast => SystemParameters.HighContrast;

    /// <summary>The Windows accent colour (and its darker and lighter shades).</summary>
    public static (Color Accent, Color Dark, Color Light)? Accent
    {
        get
        {
            try
            {
                var ui = new Windows.UI.ViewManagement.UISettings();
                return (Convert(ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent)),
                        Convert(ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark1)),
                        Convert(ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight1)));
            }
            catch
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                    if (key?.GetValue("AccentColor") is int abgr)
                    {
                        var c = Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
                        return (c, Shade(c, 0.8), Shade(c, 1.25));
                    }
                }
                catch { }
                return null;
            }
        }
    }

    private static Color Convert(Windows.UI.Color c) => Color.FromArgb(255, c.R, c.G, c.B);

    public static Color Shade(Color c, double f) =>
        Color.FromRgb((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));

    /// <summary>Perceived brightness 0–1.</summary>
    public static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    /// <summary>Starts watching for changes to the Windows theme.</summary>
    public static void Listen()
    {
        if (_listening) return;
        _listening = true;
        try
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle
                    or UserPreferenceCategory.Accessibility)
                    Raise();
            };
        }
        catch { }
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast)) Raise();
        };
    }

    private static DateTime _last;

    private static void Raise()
    {
        // Windows sends several notifications for one change: act once.
        var now = DateTime.UtcNow;
        if ((now - _last).TotalMilliseconds < 300) return;
        _last = now;
        Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
    }
}
