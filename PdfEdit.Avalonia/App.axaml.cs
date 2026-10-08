using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace PdfEdit.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        // macOS hands over PDFs opened from Finder (double-click, Open With, dropped on the Dock icon)
        // as an activation, not on the command line.
        if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
            activatable.Activated += (_, e) =>
            {
                if (e is not FileActivatedEventArgs files) return;
                foreach (var item in files.Files)
                    if (item.TryGetLocalPath() is { } path && File.Exists(path))
                        MainWindow.OpenFile(path);
            };
        base.OnFrameworkInitializationCompleted();
    }
}
