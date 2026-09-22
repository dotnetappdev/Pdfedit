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
            "Table"     => DesignTool.Table,
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
    private void BringToFront_Click(object sender, RoutedEventArgs e)  => VM?.DesignCanvas.BringToFront();
    private void SendToBack_Click(object sender, RoutedEventArgs e)    => VM?.DesignCanvas.SendToBack();
    private void DesignDuplicate_Click(object sender, RoutedEventArgs e) => VM?.DesignCanvas.DuplicateSelected();
    private void DeleteDesignElement_Click(object sender, RoutedEventArgs e) => VM?.DesignCanvas.DeleteSelected();
    private void DesignUndo_Click(object sender, RoutedEventArgs e)    => VM?.DesignCanvas.Undo();
    private void DesignRedo_Click(object sender, RoutedEventArgs e)    => VM?.DesignCanvas.Redo();
    private void DesignZoomIn_Click(object sender, RoutedEventArgs e)  => VM?.DesignCanvas.ZoomIn();
    private void DesignZoomOut_Click(object sender, RoutedEventArgs e) => VM?.DesignCanvas.ZoomOut();
    private void DesignZoomFit_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.DesignCanvas == null) return;
        var ctrl = DesignCanvasControl;
        VM.DesignCanvas.ZoomFit(ctrl.ActualWidth - 80, ctrl.ActualHeight - 80);
    }

    private void SaveDesign_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.DesignCanvas == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title  = "Save Design",
            Filter = "PdfEdit Design|*.pdfdesign|All files|*.*",
            DefaultExt = "pdfdesign"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            VM.DesignCanvas.SaveDesign(dlg.FileName);
            Dialogs.AppDialog.ShowInfo($"Design saved to {System.IO.Path.GetFileName(dlg.FileName)}.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Could not save design.", ex); }
    }

    private void LoadDesign_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Load Design",
            Filter = "PdfEdit Design|*.pdfdesign|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (VM == null) VM_EnsureDesign();
            VM!.DesignCanvas.LoadDesign(dlg.FileName);
            VM.IsDesignMode = true;
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Could not load design.", ex); }
    }

    private void VM_EnsureDesign()
    {
        // Ensures design mode is active so DesignCanvas VM is initialized
        VM?.NewDesignCommand.Execute(null);
    }

    private void DesignBgColor_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.DesignCanvas == null) return;
        var current = VM.DesignCanvas.PageBackground;
        // Use a simple color-picker dialog built from WPF
        var win = new Window
        {
            Title = "Page Background Color",
            Width = 320, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)FindResource("AppBgBrush")
        };
        var stack = new StackPanel { Margin = new Thickness(16) };
        var label = new TextBlock { Text = "Enter color (hex, e.g. #FFFFFF or #FFE8D5):", Margin = new Thickness(0,0,0,8) };
        var box   = new TextBox   { Text = $"#{current.R:X2}{current.G:X2}{current.B:X2}", Margin = new Thickness(0,0,0,12), Padding = new Thickness(4) };
        var btns  = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok    = new Button { Content = "OK",     Width = 70, Margin = new Thickness(0,0,8,0), IsDefault = true };
        var cancel= new Button { Content = "Cancel", Width = 70, IsCancel = true };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        stack.Children.Add(label); stack.Children.Add(box); stack.Children.Add(btns);
        win.Content = stack;
        ok.Click += (_, _) =>
        {
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(box.Text);
                VM.DesignCanvas.PageBackground = c;
                win.DialogResult = true;
            }
            catch { }
        };
        win.ShowDialog();
    }

    private void DesignAlign_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.DesignCanvas == null) return;
        var tag = (sender as FrameworkElement)?.Tag?.ToString();
        switch (tag)
        {
            case "AlignLeft":    VM.DesignCanvas.AlignLeft();    break;
            case "AlignRight":   VM.DesignCanvas.AlignRight();   break;
            case "AlignCenterH": VM.DesignCanvas.AlignCenterH(); break;
            case "AlignTop":     VM.DesignCanvas.AlignTop();     break;
            case "AlignBottom":  VM.DesignCanvas.AlignBottom();  break;
            case "AlignCenterV": VM.DesignCanvas.AlignCenterV(); break;
        }
    }

    private void DesignTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.DesignCanvas == null) return;
        var tag = (sender as FrameworkElement)?.Tag?.ToString();
        if (!string.IsNullOrEmpty(tag))
        {
            VM.DesignCanvas.LoadTemplate(tag);
            VM.IsDesignMode = true;
        }
    }

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
