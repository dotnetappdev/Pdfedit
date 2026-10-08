using Microsoft.JSInterop;

namespace PdfEdit.Blazor.Components.Pages;

// The Windows app's themes: Light, Dark and High contrast, or System (follows the device's
// dark-mode and high-contrast settings). The choice is remembered in this browser.
public partial class Editor
{
    public static readonly string[] Themes = ["System", "Light", "Dark", "HighContrast"];

    private string _theme = "System";
    private bool _systemDark, _systemContrast;

    private string EffectiveTheme => _theme != "System" ? _theme : _systemContrast ? "HighContrast" : _systemDark ? "Dark" : "Light";
    private string ThemeClass => EffectiveTheme switch { "Dark" => "pe-theme-dark", "HighContrast" => "pe-theme-hc", _ => "pe-theme-light" };
    private bool ThemeIsLight => EffectiveTheme == "Light";

    public static string ThemeName(string id) => id switch { "System" => "System", "HighContrast" => "High Contrast", _ => id };
    public static string ThemeIcon(string id) => id switch
    {
        "Light" => "bi-sun", "Dark" => "bi-moon", "HighContrast" => "bi-circle-half", _ => "bi-display",
    };
    private static string ThemeHint(string id) => id switch
    {
        "System" => "Follow the device's light, dark or high-contrast setting",
        "HighContrast" => "Black, white and bright colours with strong outlines",
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
