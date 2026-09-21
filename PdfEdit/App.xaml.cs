using System.Windows;

namespace PdfEdit;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && System.IO.File.Exists(e.Args[0]))
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
            _ = mainWindow.OpenFileAsync(e.Args[0]);
        }
    }
}
