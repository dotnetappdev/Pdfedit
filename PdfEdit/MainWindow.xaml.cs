using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Fluent;
using PdfEdit.Models;
using PdfEdit.Services;
using PdfEdit.ViewModels;

namespace PdfEdit;

public partial class MainWindow : RibbonWindow
{
    public MainWindow()
    {
        // Value converters are registered application-wide in App.xaml so that
        // every control (including stand-alone UserControls) can resolve them.
        InitializeComponent();
        ApplyDockTheme(AppSettings.Current.Theme);
        Loaded += OnWindowLoaded;
    }

    /// <summary>Matches the AvalonDock docking chrome to the current app theme.</summary>
    public void ApplyDockTheme(string themeName)
    {
        DockManager.Theme = themeName switch
        {
            "Light" => new AvalonDock.Themes.Vs2013LightTheme(),
            "HighContrast" => new AvalonDock.Themes.Vs2013DarkTheme(),
            _ => new AvalonDock.Themes.Vs2013DarkTheme()
        };
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

    // ── Design ribbon handlers ────────────────────────────────────────────────

    private MainViewModel? VM => DataContext as MainViewModel;

    private void DesignTool_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.DesignCanvas == null) return;
        var tag = (sender as FrameworkElement)?.Tag?.ToString();
        VM.DesignCanvas.ActiveTool = tag switch
        {
            "Text"      => DesignTool.Text,
            "Rectangle" => DesignTool.Rectangle,
            "Ellipse"   => DesignTool.Ellipse,
            "Line"      => DesignTool.Line,
            "Arrow"     => DesignTool.Arrow,
            "Pen"       => DesignTool.Pen,
            "Image"     => DesignTool.Image,
            _           => DesignTool.Select
        };
        DesignCanvasControl?.Focus();
    }

    private void TextAlign_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.DesignCanvas == null) return;
        var tag = (sender as FrameworkElement)?.Tag?.ToString();
        VM.DesignCanvas.TextAlignment = tag switch
        {
            "Center"  => System.Windows.TextAlignment.Center,
            "Right"   => System.Windows.TextAlignment.Right,
            "Justify" => System.Windows.TextAlignment.Justify,
            _         => System.Windows.TextAlignment.Left
        };
    }

    private void BringForward_Click(object sender, RoutedEventArgs e)  => VM?.DesignCanvas.BringForward();
    private void SendBackward_Click(object sender, RoutedEventArgs e)  => VM?.DesignCanvas.SendBackward();
    private void DeleteDesignElement_Click(object sender, RoutedEventArgs e) => VM?.DesignCanvas.DeleteSelected();
    private void DesignUndo_Click(object sender, RoutedEventArgs e)    => VM?.DesignCanvas.Undo();
    private void DesignRedo_Click(object sender, RoutedEventArgs e)    => VM?.DesignCanvas.Redo();
    private void DesignZoomIn_Click(object sender, RoutedEventArgs e)  => VM?.DesignCanvas.ZoomIn();
    private void DesignZoomOut_Click(object sender, RoutedEventArgs e) => VM?.DesignCanvas.ZoomOut();

    private void DesignPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VM?.DesignCanvas == null) return;
        var idx = (sender as ComboBox)?.SelectedIndex ?? 0;
        VM.DesignCanvas.PageSize = idx switch
        {
            1 => DesignPageSize.Letter,
            2 => DesignPageSize.A3,
            _ => DesignPageSize.A4
        };
    }
}
