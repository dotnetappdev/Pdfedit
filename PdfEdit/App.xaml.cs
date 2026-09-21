using System.Windows;
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
            ApplyTheme(AppSettings.Current?.Theme ?? "Dark");
        }
        catch (Exception ex)
        {
            ErrorDialog.Show("Failed to apply the selected theme. The default appearance will be used.", ex);
        }

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
