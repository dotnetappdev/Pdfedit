using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PdfEdit.Dialogs;
using PdfEdit.Services;

namespace PdfEdit;

public partial class App : Application
{
    private static ResourceDictionary? _themeDict;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Install global exception handlers first so any later failure (including
        // XAML parse errors while building the main window) surfaces a copyable
        // dialog instead of a silent crash.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        try
        {
            AppSettings.Initialize();
        }
        catch (Exception ex)
        {
            ErrorDialog.Show("Failed to load your settings. Default settings will be used for this session.", ex);
        }

        try
        {
            // Follows the Windows theme unless the user has picked one.
            ApplyTheme(AppSettings.Current?.EffectiveTheme ?? "System");
        }
        catch (Exception ex)
        {
            ErrorDialog.Show("Failed to apply the selected theme. The default appearance will be used.", ex);
        }

        try
        {
            FontService.ApplyAll();
        }
        catch (Exception ex)
        {
            ErrorDialog.Show("Failed to apply interface font sizes. Defaults will be used.", ex);
        }

        try
        {
            // Icon size and the menu text size for the ribbon's own icons and menus.
            InterfaceStyleService.Initialize();
        }
        catch (Exception ex)
        {
            ErrorDialog.Show("Failed to apply the icon size. The default size will be used.", ex);
        }

        // Dark caption / window buttons on every window that follows the theme.
        TitleBarTheme.Register();
        // Settings → Accessibility: speak focused controls and tooltips when that's turned on.
        NarrationService.Register();

        MainWindow mainWindow;
        try
        {
            mainWindow = new MainWindow();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            ErrorDialog.Show("PdfEdit could not start because the main window failed to load.", ex);
            Shutdown(1);
            return;
        }

        if (e.Args.Length > 0 && System.IO.File.Exists(e.Args[0]))
        {
            _ = OpenInitialFileAsync(mainWindow, e.Args[0]);
        }
    }

    private static async System.Threading.Tasks.Task OpenInitialFileAsync(MainWindow window, string path)
    {
        try
        {
            await window.OpenFileAsync(path);
        }
        catch (Exception ex)
        {
            ErrorDialog.Show($"Could not open the file:\n{path}", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorDialog.Show("An unexpected error occurred. You can copy the details below and continue working.", e.Exception);
        e.Handled = true; // keep the app alive
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ErrorDialog.Show("An unexpected error occurred on a background thread.", ex);
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        ErrorDialog.Show("An unexpected error occurred in a background task.", e.Exception);
        e.SetObserved(); // prevent process termination
    }

    /// <summary>The theme actually showing: "Light", "Dark" or "HighContrast" ("System" resolved).</summary>
    public static string ResolvedTheme { get; private set; } = "Dark";

    /// <summary>The theme is dark (title bars and docking chrome follow this).</summary>
    public static bool IsDarkTheme => ResolvedTheme switch
    {
        "Light" => false,
        _ when ThemeCatalog.TryGet(ResolvedTheme, out var palette) => palette.IsDark,
        "HighContrast" => !(Current?.TryFindResource("PanelBgColor") is Color c && WindowsTheme.Luminance(c) > 0.5),
        _ => true,
    };

    /// <summary>The user picked a theme ("System" = go back to following Windows).</summary>
    public static void SwitchTheme(string themeName)
    {
        AppSettings.Current.Theme = themeName;
        AppSettings.Current.ThemeChosenByUser = themeName != "System";
        AppSettings.Current.Save();
        ApplyTheme(themeName);
    }

    /// <summary>Re-applies the saved theme (after the Windows theme, contrast or accent changes).</summary>
    public static void RefreshTheme() => ApplyTheme(AppSettings.Current.EffectiveTheme);

    private static bool _watchingWindows;
    private static ResourceDictionary? _overrides;

    /// <summary>A theme's menu name; "Use Windows setting" says what Windows is set to now.</summary>
    public static string ThemeLabel(string id, string name) =>
        id == "System" ? $"{name} ({Resolve("System") switch { "Light" => "light", "HighContrast" => "contrast theme", _ => "dark" }})" : name;

    /// <summary>"System" → whatever Windows is set to: a contrast theme, else light or dark apps.</summary>
    public static string Resolve(string setting) => setting switch
    {
        "Light" or "Dark" or "HighContrast" => setting,
        _ when ThemeCatalog.TryGet(setting, out _) => setting,
        _ => WindowsTheme.HighContrast ? "HighContrast" : WindowsTheme.AppsUseLightTheme ? "Light" : "Dark",
    };

    private static void ApplyTheme(string themeName)
    {
        if (!_watchingWindows)
        {
            _watchingWindows = true;
            WindowsTheme.Listen();
            WindowsTheme.Changed += () =>
            {
                if (AppSettings.Current.EffectiveTheme is "System" or "HighContrast" || AppSettings.Current.UseWindowsAccent) RefreshTheme();
            };
        }

        string resolved = Resolve(themeName);
        ResolvedTheme = resolved;
        ResourceDictionary newDict;
        bool isPalette = ThemeCatalog.TryGet(resolved, out var palette);
        if (isPalette) newDict = palette.Build();
        else
        {
            string uri = resolved switch
            {
                "Light" => "Themes/LightTheme.xaml",
                "HighContrast" => "Themes/HighContrastTheme.xaml",
                _ => "Themes/DarkTheme.xaml"
            };
            newDict = new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) };
        }

        var merged = Current.Resources.MergedDictionaries;

        // Remove any theme dictionary already merged — including the default one
        // declared in App.xaml. Otherwise it would remain and, because later
        // merged dictionaries win in WPF, keep overriding the newly applied theme.
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source?.OriginalString ?? string.Empty;
            if (src.Contains("Themes/", StringComparison.OrdinalIgnoreCase) &&
                src.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase))
            {
                merged.RemoveAt(i);
            }
        }
        if (_overrides != null) merged.Remove(_overrides);
        if (_themeDict != null) merged.Remove(_themeDict);   // a palette theme has no Source

        // Insert right after the Fluent generic dictionary (index 0).
        int insertAt = merged.Count > 0 ? 1 : 0;
        merged.Insert(insertAt, newDict);
        _themeDict = newDict;

        // Windows colours on top: the contrast theme's own palette, or the accent colour.
        _overrides = WindowsOverrides(resolved);
        Color? ribbonAccent = isPalette ? palette.AccentColor : null;
        if (_overrides != null)
        {
            merged.Insert(insertAt + 1, _overrides);
            if (_overrides.Contains("AccentColor")) ribbonAccent = (Color)_overrides["AccentColor"];
        }

        // Keep the Fluent ribbon in sync with the app theme.
        ApplyRibbonTheme(resolved, ribbonAccent);

        // Keep the AvalonDock docking chrome in sync (main window may not exist yet
        // during the very first theme application at startup).
        (Current.MainWindow as MainWindow)?.ApplyDockTheme(resolved);

        // Native title bars of windows already open.
        TitleBarTheme.ApplyAll();
    }

    /// <summary>
    /// Colours taken from Windows: with a Windows contrast theme on, its window, text, highlight
    /// and link colours replace the built-in ones; otherwise, if chosen, the accent colour.
    /// </summary>
    private static ResourceDictionary? WindowsOverrides(string resolved)
    {
        var d = new ResourceDictionary();
        void Put(string name, Color c)
        {
            d[name + "Color"] = c;
            d[name + "Brush"] = new SolidColorBrush(c);
        }

        if (resolved == "HighContrast" && WindowsTheme.HighContrast)
        {
            Color window = SystemColors.WindowColor, text = SystemColors.WindowTextColor;
            Color highlight = SystemColors.HighlightColor, link = SystemColors.HotTrackColor;
            Color face = SystemColors.ControlColor, gray = SystemColors.GrayTextColor;
            foreach (var n in new[] { "AppBg", "PanelBg", "SidebarBg", "ContentBg", "StatusBarBg", "InputBg", "SearchBg", "ToastBg" }) Put(n, window);
            foreach (var n in new[] { "Foreground", "InputForeground", "ToastForeground", "AppBorder", "InputBorder", "StatusBarForeground" }) Put(n, text);
            Put("DimForeground", WindowsTheme.Luminance(gray) is var lg && Math.Abs(lg - WindowsTheme.Luminance(window)) > 0.3 ? gray : text);
            Put("Accent", highlight);
            Put("AccentHover", link);
            Put("AccentMuted", highlight);
            Put("ActiveBg", highlight);
            Put("HoverBg", face);
            d["ButtonBgBrush"] = new SolidColorBrush(face);
            d["ButtonBorderBrush"] = new SolidColorBrush(text);
            return d;
        }

        if (AppSettings.Current.UseWindowsAccent && resolved != "HighContrast" && WindowsTheme.Accent is { } accent)
        {
            // Dark theme: the lighter shade reads better on dark backgrounds.
            var main = resolved == "Light" ? accent.Accent : accent.Light;
            Put("Accent", main);
            Put("AccentHover", resolved == "Light" ? accent.Dark : accent.Accent);
            var muted = Color.FromArgb(resolved == "Light" ? (byte)0x33 : (byte)0x55, main.R, main.G, main.B);
            Put("AccentMuted", muted);
            Put("ActiveBg", muted);
            return d;
        }
        return null;
    }

    /// <summary>
    /// Switches the Fluent.Ribbon (ControlzEx) theme so the ribbon matches the
    /// selected app theme. Runs best-effort: if the theme can't be applied the
    /// ribbon simply keeps its previous appearance.
    /// </summary>
    private static void ApplyRibbonTheme(string themeName, Color? accent = null)
    {
        try
        {
            string baseColor = IsDarkTheme ? "Dark" : "Light";
            if (accent is { } a)
            {
                // A ribbon theme made from the Windows (or contrast) colour.
                var generated = ControlzEx.Theming.RuntimeThemeGenerator.Current.GenerateRuntimeTheme(baseColor, a);
                if (generated != null)
                {
                    ControlzEx.Theming.ThemeManager.Current.ChangeTheme(Current, generated);
                    return;
                }
            }
            string fluentTheme = themeName switch
            {
                "Light" => "Light.Blue",
                "HighContrast" => IsDarkTheme ? "Dark.Yellow" : "Light.Blue",
                _ => "Dark.Blue"
            };
            ControlzEx.Theming.ThemeManager.Current.ChangeTheme(Current, fluentTheme);
        }
        catch
        {
            // Non-fatal: ribbon keeps its default theme.
        }
    }
}
