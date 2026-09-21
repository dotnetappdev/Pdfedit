namespace PdfEdit.Models;

/// <summary>
/// Describes one themable interface area whose font size the user can adjust,
/// modelled after Visual Studio's "Fonts and Colors" settings list.
/// </summary>
public sealed class InterfaceFontArea
{
    public InterfaceFontArea(string key, string displayName, string description,
        string resourceKey, double defaultSize)
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
        ResourceKey = resourceKey;
        DefaultSize = defaultSize;
    }

    /// <summary>Stable identifier persisted in settings.</summary>
    public string Key { get; }

    /// <summary>Friendly name shown in the settings list.</summary>
    public string DisplayName { get; }

    /// <summary>Short explanation of what the area covers.</summary>
    public string Description { get; }

    /// <summary>The application resource key (a Double) that styles bind to via DynamicResource.</summary>
    public string ResourceKey { get; }

    /// <summary>The out-of-the-box font size for this area.</summary>
    public double DefaultSize { get; }
}

/// <summary>Registry of the adjustable interface font areas.</summary>
public static class InterfaceFonts
{
    public const double MinSize = 8;
    public const double MaxSize = 32;

    public static readonly IReadOnlyList<InterfaceFontArea> Areas = new List<InterfaceFontArea>
    {
        new("Ribbon",     "Ribbon & Toolbar",  "The main ribbon tabs, buttons and menus.",       "RibbonFontSize",    13),
        new("Panels",     "Side Panels",       "Toolbox, properties and page thumbnail panels.", "PanelFontSize",     13),
        new("Chat",       "AI Assistant",      "The AI chat assistant panel.",                   "ChatFontSize",      13),
        new("StatusBar",  "Status Bar",        "The status bar along the bottom of the window.",  "StatusBarFontSize", 12),
        new("Dialogs",    "Dialogs & Settings","Settings and other pop-up dialog windows.",      "DialogFontSize",    13),
    };

    public static InterfaceFontArea? Find(string key) =>
        Areas.FirstOrDefault(a => a.Key == key);
}
