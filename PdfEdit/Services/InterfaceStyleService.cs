using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace PdfEdit.Services;

/// <summary>
/// Icon sizes and the menu text size for parts of the window PdfEdit doesn't template itself
/// (Fluent Ribbon's icons and drop-down menus).
/// </summary>
public static class InterfaceStyleService
{
    public static readonly (string Key, string Label, double Factor)[] IconSizes =
    {
        ("Small", "Small (75%)", 0.75),
        ("Normal", "Normal (100%)", 1.0),
        ("Large", "Large (125%)", 1.25),
        ("ExtraLarge", "Extra large (150%)", 1.5),
    };

    public static double IconFactor(string? key)
    {
        foreach (var size in IconSizes)
            if (size.Key == key) return size.Factor;
        return 1.0;
    }

    /// <summary>Raised after the icon size changes, with the new factor (the ribbon grows to fit).</summary>
    public static event Action<double>? IconSizeChanged;

    private static readonly Type? IconPresenterType = Type.GetType("Fluent.IconPresenter, Fluent");
    private static Style? _fluentIconBase, _fluentMenuBase;
    private static bool _captured;

    /// <summary>Call once at startup, before the main window is built.</summary>
    public static void Initialize()
    {
        var app = Application.Current;
        if (app == null) return;
        if (!_captured)
        {
            _captured = true;
            if (IconPresenterType != null) _fluentIconBase = app.TryFindResource(IconPresenterType) as Style;
            _fluentMenuBase = app.TryFindResource(typeof(Fluent.MenuItem)) as Style
                              ?? app.TryFindResource("Fluent.Ribbon.Styles.MenuItem") as Style;
        }
        InstallFluentMenuStyle(app);
        ApplyIconSize(AppSettings.Current.IconSize);
    }

    /// <summary>Ribbon drop-down menus use the Menus font size too.</summary>
    private static void InstallFluentMenuStyle(Application app)
    {
        if (_fluentMenuBase == null) return;   // keep Fluent's own look rather than lose its template
        var style = new Style(typeof(Fluent.MenuItem), _fluentMenuBase);
        style.Setters.Add(new Setter(Control.FontSizeProperty, new DynamicResourceExtension("MenuFontSize")));
        var text = new Style(typeof(TextBlock), app.TryFindResource(typeof(TextBlock)) as Style);
        text.Setters.Add(new Setter(TextBlock.FontSizeProperty, new DynamicResourceExtension("MenuFontSize")));
        style.Resources.Add(typeof(TextBlock), text);
        app.Resources[typeof(Fluent.MenuItem)] = style;
    }

    /// <summary>Sets the icon size everywhere: PdfEdit's own icons and Fluent Ribbon's icon slots.</summary>
    public static void ApplyIconSize(string? key)
    {
        var app = Application.Current;
        if (app == null) return;
        double f = IconFactor(key);
        app.Resources["SmallIconSize"] = 16 * f;
        app.Resources["MediumIconSize"] = 24 * f;
        app.Resources["LargeIconSize"] = 32 * f;

        if (IconPresenterType != null)
        {
            var style = new Style(IconPresenterType, _fluentIconBase);
            foreach (var (field, size) in new[] { ("SmallSizeProperty", 16 * f), ("MediumSizeProperty", 24 * f), ("LargeSizeProperty", 32 * f) })
            {
                if (IconPresenterType.GetField(field, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null) is DependencyProperty dp
                    && dp.PropertyType == typeof(Size))
                    style.Setters.Add(new Setter(dp, new Size(size, size)));
            }
            app.Resources[IconPresenterType] = style;
        }
        IconSizeChanged?.Invoke(f);
    }
}
