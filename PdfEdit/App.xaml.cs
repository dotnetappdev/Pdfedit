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

        try
        {
            FontService.ApplyAll();
        }
        catch (Exception ex)
        {
            ErrorDialog.Show("Failed to apply interface font sizes. Defaults will be used.", ex);
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

        // Insert right after the Fluent generic dictionary (index 0).
        int insertAt = merged.Count > 0 ? 1 : 0;
        merged.Insert(insertAt, newDict);
        _themeDict = newDict;

        // Keep the Fluent ribbon in sync with the app theme.
        ApplyRibbonTheme(themeName);
    }

    /// <summary>
    /// Switches the Fluent.Ribbon (ControlzEx) theme so the ribbon matches the
    /// selected app theme. Runs best-effort: if the theme can't be applied the
    /// ribbon simply keeps its previous appearance.
    /// </summary>
    private static void ApplyRibbonTheme(string themeName)
    {
        try
        {
            string fluentTheme = themeName switch
            {
                "Light" => "Light.Blue",
                "HighContrast" => "Dark.Yellow",
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
