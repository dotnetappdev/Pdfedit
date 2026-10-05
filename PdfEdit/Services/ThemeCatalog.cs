using System.Windows;
using System.Windows.Media;

namespace PdfEdit.Services;

/// <summary>
/// A colour theme: the same named colours the built-in Light / Dark themes define, so every
/// window, panel and dialog picks it up. Built in code, so adding a theme is one entry below.
/// </summary>
public sealed record ThemePalette(
    string Id, string Name, bool IsDark,
    string Accent, string AccentHover, string AccentMuted,
    string AppBg, string PanelBg, string SidebarBg, string ContentBg,
    string StatusBarBg, string StatusBarFg,
    string Foreground, string DimForeground, string Border,
    string InputBg, string InputBorder, string Hover, string Active,
    string ButtonBg, string ButtonBorder)
{
    public Color AccentColor => ToColor(Accent);

    public static Color ToColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public ResourceDictionary Build()
    {
        var d = new ResourceDictionary();
        void Put(string key, string hex)
        {
            var c = ToColor(hex);
            d[key + "Color"] = c;
            var b = new SolidColorBrush(c);
            b.Freeze();
            d[key + "Brush"] = b;
        }
        Put("Accent", Accent); Put("AccentHover", AccentHover); Put("AccentMuted", AccentMuted);
        Put("AppBg", AppBg); Put("PanelBg", PanelBg); Put("SidebarBg", SidebarBg); Put("ContentBg", ContentBg);
        Put("StatusBarBg", StatusBarBg); Put("StatusBarForeground", StatusBarFg);
        Put("Foreground", Foreground); Put("DimForeground", DimForeground); Put("AppBorder", Border);
        Put("InputBg", InputBg); Put("InputForeground", Foreground); Put("InputBorder", InputBorder);
        Put("ToastBg", IsDark ? Hover : "#2D2D2D"); Put("ToastForeground", IsDark ? Foreground : "#FFFFFF");
        Put("SearchBg", InputBg); Put("HoverBg", Hover); Put("ActiveBg", Active);
        Put("ButtonBg", ButtonBg); Put("ButtonBorder", ButtonBorder);
        d["PanelFontSize"] = 12.0;
        d["RibbonFontSize"] = 12.0;
        d["StatusBarFontSize"] = 11.0;
        return d;
    }
}

public static class ThemeCatalog
{
    /// <summary>Every theme for the menus: id and name. "System", "Light", "Dark" and "HighContrast" are the originals.</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> Choices = new List<(string, string)>
    {
        ("System", "Use Windows setting"), ("Light", "Light"), ("Dark", "Dark"), ("HighContrast", "High contrast"),
    }.Concat(Palettes().Select(p => (p.Id, p.Name))).ToList();

    private static readonly Dictionary<string, ThemePalette> ById = Palettes().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string id, out ThemePalette palette) => ById.TryGetValue(id, out palette!);

    /// <summary>Three colours to preview a theme in the menus (background, panel, accent).</summary>
    public static (Color Bg, Color Panel, Color Accent) Swatch(string id) => id switch
    {
        "Light" => (ThemePalette.ToColor("#F5F5F5"), ThemePalette.ToColor("#FFFFFF"), ThemePalette.ToColor("#0A84FF")),
        "Dark" or "System" => (ThemePalette.ToColor("#121212"), ThemePalette.ToColor("#1D1D1D"), ThemePalette.ToColor("#0A84FF")),
        "HighContrast" => (Colors.Black, Colors.Black, ThemePalette.ToColor("#1AEBFF")),
        _ when ById.TryGetValue(id, out var p) => (ThemePalette.ToColor(p.AppBg), ThemePalette.ToColor(p.PanelBg), p.AccentColor),
        _ => (Colors.Gray, Colors.Gray, Colors.Gray),
    };

    private static IEnumerable<ThemePalette> Palettes() => new[]
    {
        // Microsoft Office (Word's blue, white panels, grey canvas) and Office's Black theme.
        new ThemePalette("Office", "Office", false,
            Accent: "#185ABD", AccentHover: "#103F91", AccentMuted: "#D0E0F7",
            AppBg: "#F3F2F1", PanelBg: "#FFFFFF", SidebarBg: "#F3F2F1", ContentBg: "#E1DFDD",
            StatusBarBg: "#185ABD", StatusBarFg: "#FFFFFF",
            Foreground: "#323130", DimForeground: "#605E5C", Border: "#E1DFDD",
            InputBg: "#FFFFFF", InputBorder: "#8A8886", Hover: "#EDEBE9", Active: "#DEECF9",
            ButtonBg: "#FFFFFF", ButtonBorder: "#8A8886"),
        new ThemePalette("OfficeBlack", "Office Black", true,
            Accent: "#4FA3F7", AccentHover: "#78B9FA", AccentMuted: "#1E3A5C",
            AppBg: "#1F1F1F", PanelBg: "#292929", SidebarBg: "#262626", ContentBg: "#3B3B3B",
            StatusBarBg: "#1F1F1F", StatusBarFg: "#C8C8C8",
            Foreground: "#F3F3F3", DimForeground: "#ABABAB", Border: "#3F3F3F",
            InputBg: "#333333", InputBorder: "#5C5C5C", Hover: "#3D3D3D", Active: "#2E4B6F",
            ButtonBg: "#333333", ButtonBorder: "#5C5C5C"),

        // Dracula (draculatheme.com): the purple-on-charcoal theme from Visual Studio and VS Code.
        new ThemePalette("Dracula", "Dracula", true,
            Accent: "#BD93F9", AccentHover: "#FF79C6", AccentMuted: "#44475A",
            AppBg: "#21222C", PanelBg: "#282A36", SidebarBg: "#21222C", ContentBg: "#191A21",
            StatusBarBg: "#191A21", StatusBarFg: "#F8F8F2",
            Foreground: "#F8F8F2", DimForeground: "#9AA5CE", Border: "#44475A",
            InputBg: "#21222C", InputBorder: "#6272A4", Hover: "#343746", Active: "#44475A",
            ButtonBg: "#343746", ButtonBorder: "#6272A4"),

        // Nord (nordtheme.com): arctic blue-greys.
        new ThemePalette("Nord", "Nord", true,
            Accent: "#5E81AC", AccentHover: "#81A1C1", AccentMuted: "#3B4F66",
            AppBg: "#2E3440", PanelBg: "#3B4252", SidebarBg: "#2E3440", ContentBg: "#434C5E",
            StatusBarBg: "#3B4252", StatusBarFg: "#D8DEE9",
            Foreground: "#ECEFF4", DimForeground: "#A3ABB9", Border: "#4C566A",
            InputBg: "#2E3440", InputBorder: "#4C566A", Hover: "#434C5E", Active: "#4C566A",
            ButtonBg: "#434C5E", ButtonBorder: "#4C566A"),

        // One Dark (Atom's default dark theme).
        new ThemePalette("OneDark", "One Dark", true,
            Accent: "#4D8FE0", AccentHover: "#61AFEF", AccentMuted: "#2C3E52",
            AppBg: "#21252B", PanelBg: "#282C34", SidebarBg: "#21252B", ContentBg: "#3A3F4B",
            StatusBarBg: "#21252B", StatusBarFg: "#9DA5B4",
            Foreground: "#D7DAE0", DimForeground: "#7F848E", Border: "#181A1F",
            InputBg: "#1D1F23", InputBorder: "#3E4451", Hover: "#2C313A", Active: "#3E4451",
            ButtonBg: "#2C313A", ButtonBorder: "#3E4451"),

        // Monokai (Sublime Text, VS Code).
        new ThemePalette("Monokai", "Monokai", true,
            Accent: "#F92672", AccentHover: "#FD971F", AccentMuted: "#49483E",
            AppBg: "#1E1F1C", PanelBg: "#272822", SidebarBg: "#1E1F1C", ContentBg: "#3E3D32",
            StatusBarBg: "#1E1F1C", StatusBarFg: "#CFCFC2",
            Foreground: "#F8F8F2", DimForeground: "#A59F85", Border: "#3E3D32",
            InputBg: "#1E1F1C", InputBorder: "#75715E", Hover: "#3E3D32", Active: "#49483E",
            ButtonBg: "#3E3D32", ButtonBorder: "#75715E"),

        // Solarized (ethanschoonover.com/solarized), light and dark.
        new ThemePalette("SolarizedLight", "Solarized Light", false,
            Accent: "#268BD2", AccentHover: "#1F6FA8", AccentMuted: "#D5E6F2",
            AppBg: "#EEE8D5", PanelBg: "#FDF6E3", SidebarBg: "#EEE8D5", ContentBg: "#E4DCC5",
            StatusBarBg: "#EEE8D5", StatusBarFg: "#586E75",
            Foreground: "#073642", DimForeground: "#657B83", Border: "#DDD6C1",
            InputBg: "#FDF6E3", InputBorder: "#C9C1AA", Hover: "#E6DFC9", Active: "#D9E8F0",
            ButtonBg: "#FDF6E3", ButtonBorder: "#C9C1AA"),
        new ThemePalette("SolarizedDark", "Solarized Dark", true,
            Accent: "#268BD2", AccentHover: "#2AA198", AccentMuted: "#0F4A63",
            AppBg: "#002B36", PanelBg: "#073642", SidebarBg: "#002B36", ContentBg: "#0A4252",
            StatusBarBg: "#00212B", StatusBarFg: "#93A1A1",
            Foreground: "#EEE8D5", DimForeground: "#93A1A1", Border: "#0E4553",
            InputBg: "#002B36", InputBorder: "#586E75", Hover: "#0B4452", Active: "#164C5A",
            ButtonBg: "#0B4452", ButtonBorder: "#586E75"),

        // GitHub's light theme.
        new ThemePalette("GitHubLight", "GitHub Light", false,
            Accent: "#0969DA", AccentHover: "#0550AE", AccentMuted: "#DDF4FF",
            AppBg: "#F6F8FA", PanelBg: "#FFFFFF", SidebarBg: "#F6F8FA", ContentBg: "#E7EBEF",
            StatusBarBg: "#F6F8FA", StatusBarFg: "#57606A",
            Foreground: "#1F2328", DimForeground: "#656D76", Border: "#D0D7DE",
            InputBg: "#FFFFFF", InputBorder: "#D0D7DE", Hover: "#EAEEF2", Active: "#DDF4FF",
            ButtonBg: "#F6F8FA", ButtonBorder: "#D0D7DE"),
    };
}
