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

    private void SetStrokeWidth_Click(object sender, RoutedEventArgs e)
    {
        if (VM == null) return;
        var tag = (sender as FrameworkElement)?.Tag?.ToString();
        if (double.TryParse(tag, out double w))
            VM.CurrentStrokeWidth = w;
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

    private void DesignPenColor_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.DesignCanvas == null) return;
        var newColor = ShowColorPickerDialog("Set Pen Color", VM.DesignCanvas.PenColor);
        if (newColor != null) VM.DesignCanvas.PenColor = newColor.Value;
    }

    private void DesignColor_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.DesignCanvas == null) return;
        var tag = (sender as FrameworkElement)?.Tag?.ToString();

        System.Windows.Media.Color current = tag switch
        {
            "FillColor"   => VM.DesignCanvas.FillColor,
            "StrokeColor" => VM.DesignCanvas.StrokeColor,
            "TextColor"   => VM.DesignCanvas.TextColor,
            "TextBgColor" => VM.DesignCanvas.TextBgColor,
            _             => System.Windows.Media.Colors.Black
        };

        var newColor = ShowColorPickerDialog($"Set {tag?.Replace("Color", " Color")}", current);
        if (newColor == null) return;

        switch (tag)
        {
            case "FillColor":   VM.DesignCanvas.FillColor   = newColor.Value; break;
            case "StrokeColor": VM.DesignCanvas.StrokeColor = newColor.Value; break;
            case "TextColor":   VM.DesignCanvas.TextColor   = newColor.Value; break;
            case "TextBgColor": VM.DesignCanvas.TextBgColor = newColor.Value; break;
        }
    }

    private System.Windows.Media.Color? ShowColorPickerDialog(string title, System.Windows.Media.Color current)
    {
        var win = new Window
        {
            Title = title,
            Width = 360, Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)FindResource("AppBgBrush")
        };
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Hex color (#AARRGGBB or #RRGGBB):", Margin = new Thickness(0,0,0,6) });
        var box   = new TextBox { Text = $"#{current.A:X2}{current.R:X2}{current.G:X2}{current.B:X2}", Padding = new Thickness(4), Margin = new Thickness(0,0,0,10) };
        stack.Children.Add(box);

        // Quick swatches
        var swatchPanel = new WrapPanel { Margin = new Thickness(0,0,0,10) };
        var swatches = new[] { "#FF000000","#FFFFFFFF","#FF1F3A8A","#FF8B0000","#FF006400","#FF555555","#FF800080","#FFFF8C00","#FF0A84FF","#FFFFE000","#FF40C4FF","#FFAAAAAA" };
        foreach (var hex in swatches)
        {
            var btn = new Button
            {
                Width = 24, Height = 24, Margin = new Thickness(2),
                Tag = hex,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex))
            };
            btn.Click += (_, _) => box.Text = hex;
            swatchPanel.Children.Add(btn);
        }
        stack.Children.Add(swatchPanel);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok     = new Button { Content = "OK",     Width = 70, Margin = new Thickness(0,0,8,0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        stack.Children.Add(btns);
        win.Content = stack;

        System.Windows.Media.Color? result = null;
        ok.Click += (_, _) =>
        {
            try
            {
                result = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(box.Text);
                win.DialogResult = true;
            }
            catch { }
        };
        win.ShowDialog();
        return result;
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

    private void DesignExportPng_Click(object sender, RoutedEventArgs e) => ExportDesignImage("png");
    private void DesignExportJpeg_Click(object sender, RoutedEventArgs e) => ExportDesignImage("jpeg");

    private void ExportDesignImage(string format)
    {
        if (VM?.DesignCanvas == null) return;
        var ext = format == "jpeg" ? "jpg" : "png";
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Export Design as {format.ToUpperInvariant()}",
            Filter = format == "jpeg" ? "JPEG Image|*.jpg;*.jpeg|All files|*.*" : "PNG Image|*.png|All files|*.*",
            DefaultExt = ext
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var canvas = DesignCanvasControl;
            canvas.Measure(new Size(canvas.ActualWidth, canvas.ActualHeight));
            canvas.Arrange(new Rect(new Size(canvas.ActualWidth, canvas.ActualHeight)));
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)canvas.ActualWidth, (int)canvas.ActualHeight,
                96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(canvas);
            System.Windows.Media.Imaging.BitmapEncoder encoder = format == "jpeg"
                ? new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 92 }
                : new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var stream = System.IO.File.Create(dlg.FileName);
            encoder.Save(stream);
            Dialogs.AppDialog.ShowInfo($"Image saved to {System.IO.Path.GetFileName(dlg.FileName)}.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Could not export image.", ex); }
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
