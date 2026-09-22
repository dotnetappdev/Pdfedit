using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfEdit.Dialogs;
using PdfEdit.Models;
using PdfEdit.Services;
using PdfEdit.ViewModels;
using DesignTool = PdfEdit.Models.DesignTool;

namespace PdfEdit.Controls;

public partial class ToolboxPanel : UserControl
{
    // Currently selected annotation sub-tool
    private ActiveTool _activeAnnotTool = ActiveTool.AddText;

    // Active highlight color (ARGB hex, 50% opacity)
    private string _highlightColor = "#80FFFF00";

    public ToolboxPanel()
    {
        InitializeComponent();
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    // ── Tool selection ────────────────────────────────────────────────────────

    // Live-View tools: set ActiveTool and switch to Live View tab
    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string toolName
            && Enum.TryParse<ActiveTool>(toolName, out var tool)
            && VM is MainViewModel vm)
        {
            vm.IsDesignMode = false;
            vm.ActiveTool = tool;
        }
    }

    // Design-Canvas tools: set DesignCanvas.ActiveTool and switch to Design tab
    private void DesignTool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string toolName
            && Enum.TryParse<DesignTool>(toolName, out var tool)
            && VM is MainViewModel vm)
        {
            vm.IsDesignMode = true;
            vm.DesignCanvas.ActiveTool = tool;
        }
    }

    // ── Annotation sub-type popup (image 2 style) ─────────────────────────────

    private void AnnotBtn_Click(object sender, RoutedEventArgs e)
    {
        AnnotPopup.IsOpen = !AnnotPopup.IsOpen;
    }

    private void AnnotType_Click(object sender, RoutedEventArgs e)
    {
        AnnotPopup.IsOpen = false;
        if (sender is Button btn && btn.Tag is string toolName
            && Enum.TryParse<ActiveTool>(toolName, out var tool)
            && VM is MainViewModel vm)
        {
            _activeAnnotTool = tool;
            vm.ActiveTool = tool;
            AnnotBtn.Background = (Brush)FindResource("AccentBrush");
        }
    }

    // ── Signature popup (image 1 style) ──────────────────────────────────────

    private void SignBtn_Click(object sender, RoutedEventArgs e)
    {
        SigPopup.IsOpen = !SigPopup.IsOpen;
    }

    private void SigPopup_Opened(object sender, EventArgs e)
    {
        RefreshSignaturePopup();
    }

    private void RefreshSignaturePopup()
    {
        SavedSigPanel.Children.Clear();
        var sigs = SignatureStore.Load();

        SigDivider.Visibility = sigs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var sig in sigs)
            SavedSigPanel.Children.Add(BuildSignatureCard(sig));
    }

    private UIElement BuildSignatureCard(SavedSignature sig)
    {
        BitmapImage? bmp = null;
        try
        {
            using var ms = new MemoryStream(sig.ImageBytes);
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 200;
            bmp.EndInit();
            bmp.Freeze();
        }
        catch { }

        // Signature preview image
        var preview = new Image
        {
            Source = bmp,
            Height = 40,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // Name label
        var name = new TextBlock
        {
            Text = sig.Name,
            FontSize = 12,
            FontFamily = new FontFamily("Segoe Script, Script MT Bold, Segoe UI"),
            Foreground = (Brush)FindResource("ForegroundBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 0, 0),
        };

        // Show name if no image
        UIElement content = bmp != null ? preview : name;

        // Delete (×) button
        var delBtn = new Button
        {
            Content = new TextBlock
            {
                Text = "×",
                FontSize = 16,
                Foreground = (Brush)FindResource("DimForegroundBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"Remove '{sig.Name}'",
        };
        delBtn.Click += (_, e) =>
        {
            e.Handled = true;
            SignatureStore.Remove(sig.Id);
            ToastService.Instance.Info($"Signature '{sig.Name}' removed.");
            RefreshSignaturePopup();
        };

        // Row: [sig image/name] [×]
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(content, 0);
        Grid.SetColumn(delBtn, 1);
        row.Children.Add(content);
        row.Children.Add(delBtn);

        // Card border
        var card = new Border
        {
            BorderBrush = (Brush)FindResource("AppBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)FindResource("InputBgBrush"),
            Padding = new Thickness(10, 7, 7, 7),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
            Child = row,
        };
        System.Windows.Automation.AutomationProperties.SetName(card, $"Signature: {sig.Name}");

        card.MouseLeftButtonDown += (_, _) =>
        {
            SigPopup.IsOpen = false;
            if (VM is MainViewModel vm)
            {
                vm.IsDesignMode = false;
                vm.PendingLibrarySignature = sig.ImageBytes;
                vm.ActiveTool = ActiveTool.Signature;
                ToastService.Instance.Info($"'{sig.Name}' selected — click the page to place.");
            }
        };

        card.MouseEnter += (_, _) =>
            card.BorderBrush = (Brush)FindResource("AccentBrush");
        card.MouseLeave += (_, _) =>
            card.BorderBrush = (Brush)FindResource("AppBorderBrush");

        return card;
    }

    private void AddSignature_Click(object sender, RoutedEventArgs e)
    {
        SigPopup.IsOpen = false;
        var dlg = new SignatureDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && dlg.Result?.ImageBytes != null && VM is MainViewModel vm)
        {
            vm.PendingLibrarySignature = dlg.Result.ImageBytes;
            vm.ActiveTool = ActiveTool.Signature;
            ToastService.Instance.Info("Signature created — click the page to place.");
        }
    }

    private void AddInitials_Click(object sender, RoutedEventArgs e)
    {
        SigPopup.IsOpen = false;
        var dlg = new SignatureDialog
        {
            Owner = Window.GetWindow(this),
            InitialsMode = true,
        };
        if (dlg.ShowDialog() == true && dlg.Result?.ImageBytes != null && VM is MainViewModel vm)
        {
            vm.PendingLibrarySignature = dlg.Result.ImageBytes;
            vm.ActiveTool = ActiveTool.Signature;
            ToastService.Instance.Info("Initials created — click the page to place.");
        }
    }

    // ── Highlight tool ───────────────────────────────────────────────────────

    private void HighlightTool_Checked(object sender, RoutedEventArgs e)
    {
        Tool_Checked(sender, e); // activate the tool
        HighlightColorPopup.IsOpen = true;
    }

    private void HlColor_Click(object sender, RoutedEventArgs e)
    {
        HighlightColorPopup.IsOpen = false;
        if (sender is Button btn && btn.Tag is string color)
        {
            _highlightColor = color;
            if (VM is MainViewModel vm)
                vm.ActiveHighlightColor = color;
        }
    }

    // Public method so MainWindow can open "Add Signature" from the ribbon
    public void OpenAddSignatureDialog()
    {
        var dlg = new SignatureDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && dlg.Result?.ImageBytes != null && VM is MainViewModel vm)
        {
            vm.PendingLibrarySignature = dlg.Result.ImageBytes;
            vm.ActiveTool = ActiveTool.Signature;
        }
    }
}
