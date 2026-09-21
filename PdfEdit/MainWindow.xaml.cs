using System.ComponentModel;
using System.Windows;
using Fluent;
using PdfEdit.Converters;
using PdfEdit.Services;
using PdfEdit.ViewModels;

namespace PdfEdit;

public partial class MainWindow : RibbonWindow
{
    public MainWindow()
    {
        // Register converters before InitializeComponent so XAML can resolve them
        Resources.Add("ZeroToOneConverter", new ZeroToOneConverter());
        Resources.Add("ZoomPercentConverter", new ZoomPercentConverter());
        Resources.Add("EnumToBoolConverter", new EnumToBoolConverter());
        Resources.Add("NullToBoolConverter", new NullToBoolConverter());
        Resources.Add("BoolToVisibilityConverter", new BoolToVisibilityConverter());
        Resources.Add("InverseBoolConverter", new InverseBoolConverter());

        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;
        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop))
        {
            Left = s.WindowLeft;
            Top = s.WindowTop;
        }
        Width = s.WindowWidth;
        Height = s.WindowHeight;
        if (s.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        var s = AppSettings.Current;
        if (WindowState == WindowState.Normal)
        {
            s.WindowLeft = Left;
            s.WindowTop = Top;
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }
        s.WindowMaximized = WindowState == WindowState.Maximized;

        if (DataContext is MainViewModel vm)
            vm.SaveDocumentState();

        s.Save();
    }

    public async Task OpenFileAsync(string path)
    {
        if (DataContext is MainViewModel vm)
            await vm.OpenFileAsync(path);
    }

    protected override void OnDrop(System.Windows.DragEventArgs e)
    {
        base.OnDrop(e);
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var pdf = System.Array.Find(files, f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            if (pdf != null) _ = OpenFileAsync(pdf);
        }
    }
}
