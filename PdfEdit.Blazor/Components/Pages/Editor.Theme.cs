using Microsoft.JSInterop;

namespace PdfEdit.Blazor.Components.Pages;

// The Windows app's themes: Light, Dark and High contrast, or System (follows the device's
// dark-mode and high-contrast settings), and its other palettes (Office, Dracula, Nord …).
// The choice is remembered in this browser.
public partial class Editor
{
    public static readonly string[] Themes =
    [
        "System", "Light", "Dark", "HighContrast",
        "Office", "OfficeBlack", "Dracula", "Nord", "OneDark", "Monokai", "SolarizedLight", "SolarizedDark", "GitHubLight",
    ];

    /// <summary>The themes with light backgrounds.</summary>
    private static readonly HashSet<string> LightThemes = ["Light", "Office", "SolarizedLight", "GitHubLight"];

    private string _theme = "System";
    private bool _systemDark, _systemContrast;

    private string EffectiveTheme => _theme != "System" ? _theme : _systemContrast ? "HighContrast" : _systemDark ? "Dark" : "Light";
    private string ThemeClass => EffectiveTheme switch
    {
        "Dark" => "pe-theme-dark", "HighContrast" => "pe-theme-hc", "Light" => "pe-theme-light",
        var other => "pe-theme-" + other.ToLowerInvariant() + (LightThemes.Contains(other) ? " pe-light" : " pe-dark"),
    };
    private bool ThemeIsLight => LightThemes.Contains(EffectiveTheme);

    public static string ThemeName(string id) => id switch
    {
        "HighContrast" => "High Contrast", "OfficeBlack" => "Office Black", "OneDark" => "One Dark",
        "SolarizedLight" => "Solarized Light", "SolarizedDark" => "Solarized Dark", "GitHubLight" => "GitHub Light", _ => id,
    };
    public static string ThemeIcon(string id) => id switch
    {
        "Light" => "bi-sun", "Dark" => "bi-moon", "HighContrast" => "bi-circle-half", "System" => "bi-display",
        _ when LightThemes.Contains(id) => "bi-palette", _ => "bi-palette-fill",
    };
    private static string ThemeHint(string id) => id switch
    {
        "System" => "Follow the device's light, dark or high-contrast setting",
        "HighContrast" => "Black, white and bright colours with strong outlines",
        "Office" => "Microsoft Office's blue and white", "OfficeBlack" => "Office's Black theme",
        "Dracula" => "Purple on charcoal (draculatheme.com)", "Nord" => "Arctic blue-greys (nordtheme.com)",
        "OneDark" => "Atom's dark theme", "Monokai" => "Sublime Text's Monokai", "GitHubLight" => "GitHub's light theme",
        "SolarizedLight" or "SolarizedDark" => "Ethan Schoonover's Solarized",
        _ => $"{id} theme",
    };

    private async Task LoadThemeAsync()
    {
        try
        {
            var t = await JS.InvokeAsync<ThemeState>("pdfedit.theme.init", _self);
            if (Themes.Contains(t.Saved)) _theme = t.Saved;
            _systemDark = t.Dark;
            _systemContrast = t.Contrast;
            StateHasChanged();
        }
        catch { /* storage blocked: stays on System / light */ }
    }

    public async Task SetThemeAsync(string id)
    {
        _theme = id;
        try { await JS.InvokeVoidAsync("pdfedit.theme.save", id); } catch { }
    }

    private Task NextThemeAsync() => SetThemeAsync(Themes[(Array.IndexOf(Themes, _theme) + 1) % Themes.Length]);

    /// <summary>The device's dark-mode or high-contrast setting changed.</summary>
    [JSInvokable]
    public Task OnSystemTheme(bool dark, bool contrast)
    {
        _systemDark = dark;
        _systemContrast = contrast;
        return InvokeAsync(StateHasChanged);
    }

    private sealed record ThemeState(string Saved, bool Dark, bool Contrast);
}
