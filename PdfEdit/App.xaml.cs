using System.Windows;
using PdfEdit.Services;

namespace PdfEdit;

public partial class App : Application
{
    private static ResourceDictionary? _themeDict;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppSettings.Initialize();
        ApplyTheme(AppSettings.Current.Theme);

        base.OnStartup(e);

        if (e.Args.Length > 0 && System.IO.File.Exists(e.Args[0]))
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
            _ = mainWindow.OpenFileAsync(e.Args[0]);
        }
    }

    public static void SwitchTheme(string themeName)
    {
        AppSettings.Current.Theme = themeName;
        AppSettings.Current.Save();
        ApplyTheme(themeName);
    }

    private static void ApplyTheme(string themeName)
    {
        string uri = themeName switch
        {
            "Light" => "Themes/LightTheme.xaml",
            "HighContrast" => "Themes/HighContrastTheme.xaml",
            _ => "Themes/DarkTheme.xaml"
        };

        var newDict = new ResourceDictionary
        {
            Source = new Uri(uri, UriKind.Relative)
        };

        var merged = Current.Resources.MergedDictionaries;

        // Replace existing theme dictionary (index 1, after Fluent)
        if (_themeDict != null && merged.Contains(_themeDict))
            merged.Remove(_themeDict);

        // Insert after Fluent (index 0)
        merged.Insert(1, newDict);
        _themeDict = newDict;
    }
}
