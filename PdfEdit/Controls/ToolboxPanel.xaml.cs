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
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    // True while the buttons are being updated to mirror a tool chosen elsewhere
    // (ribbon, keyboard shortcut, Edit Fields after adding a field …).
    private bool _syncing;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnVmPropertyChanged;
            oldVm.DesignCanvas.PropertyChanged -= OnDesignVmPropertyChanged;
        }
        if (e.NewValue is MainViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.DesignCanvas.PropertyChanged += OnDesignVmPropertyChanged;
            ApplyMode(vm);
            SyncLiveChecked(vm.ActiveTool);
            SyncChecked(DesignToolsPanel, vm.DesignCanvas.ActiveTool.ToString());
            UpdateInkDot(vm.CurrentFontColor);
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (VM is not { } vm) return;
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ActiveTool):
                SyncLiveChecked(vm.ActiveTool);
                break;
            case nameof(MainViewModel.IsDesignMode):
            case nameof(MainViewModel.IsPdfMode):
                ApplyMode(vm);
                break;
            case nameof(MainViewModel.CurrentFontColor):
                UpdateInkDot(vm.CurrentFontColor);
                break;
        }
    }

    private void OnDesignVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignCanvasViewModel.ActiveTool) && VM is { } vm)
            SyncChecked(DesignToolsPanel, vm.DesignCanvas.ActiveTool.ToString());
    }

    /// <summary>Shows the Live (Fill &amp; Sign) or the Design tool set to match the active view.</summary>
    private void ApplyMode(MainViewModel vm)
    {
        PdfToolsPanel.Visibility    = vm.IsDesignMode ? Visibility.Collapsed : Visibility.Visible;
        DesignToolsPanel.Visibility = vm.IsDesignMode ? Visibility.Visible : Visibility.Collapsed;
        AnnotPopup.IsOpen = SigPopup.IsOpen = HighlightColorPopup.IsOpen = InkColorPopup.IsOpen = MorePopup.IsOpen = false;
    }

    // Text-mark tools share the "Add text" button (its corner arrow picks the mark).
    private static readonly HashSet<ActiveTool> TextMarkTools = new()
    {
        ActiveTool.AddText, ActiveTool.VerticalText, ActiveTool.Checkmark, ActiveTool.XMark,
        ActiveTool.Dot, ActiveTool.Circle, ActiveTool.Line,
    };

    private void SyncLiveChecked(ActiveTool tool)
    {
        string name = TextMarkTools.Contains(tool) ? "AddText" : tool.ToString();
        bool found = SyncChecked(PdfToolsPanel, name);
        // Tools that live in the More popup light up the "…" button instead.
        MoreBtn.Background = found ? Brushes.Transparent : (Brush)FindResource("AccentBrush");
    }

    /// <summary>Checks the button whose Tag matches the active tool; returns false if none does.</summary>
    private bool SyncChecked(Panel panel, string toolName)
    {
        bool found = false;
        _syncing = true;
        try
        {
            foreach (var rb in Descendants<RadioButton>(panel))
            {
                bool match = rb.Tag as string == toolName;
                rb.IsChecked = match;
                found |= match;
            }
        }
        finally { _syncing = false; }
        return found;
    }

    private static IEnumerable<T> Descendants<T>(Panel panel) where T : DependencyObject
    {
        foreach (var child in panel.Children.OfType<DependencyObject>())
        {
            if (child is T t) yield return t;
            if (child is Panel p)
                foreach (var d in Descendants<T>(p)) yield return d;
        }
    }

    private void UpdateInkDot(string? hex)
    {
        try { InkColorDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex ?? "#000000")); }
        catch { InkColorDot.Fill = Brushes.Black; }
    }

    // ── Add text / marks ──────────────────────────────────────────────────────

    private void AddTextTool_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing || VM is not MainViewModel vm) return;
        vm.IsDesignMode = false;
        vm.ActiveTool = _activeAnnotTool;
    }

    // ── Colour ────────────────────────────────────────────────────────────────

    private void InkColorBtn_Click(object sender, RoutedEventArgs e) => InkColorPopup.IsOpen = !InkColorPopup.IsOpen;

    private void InkColor_Click(object sender, RoutedEventArgs e)
    {
        InkColorPopup.IsOpen = false;
        if (sender is not Button { Tag: string hex } || VM is not MainViewModel vm) return;
        vm.CurrentFontColor = hex;     // text and marks (also recolours the selected text)
        vm.CurrentDrawingColor = hex;  // freehand pen and shapes
        UpdateInkDot(hex);
    }

    // ── More tools ────────────────────────────────────────────────────────────

    private void MoreBtn_Click(object sender, RoutedEventArgs e) => MorePopup.IsOpen = !MorePopup.IsOpen;

    private void MoreTool_Click(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        if (sender is Button { Tag: string toolName } && Enum.TryParse<ActiveTool>(toolName, out var tool)
            && VM is MainViewModel vm)
        {
            vm.IsDesignMode = false;
            vm.ActiveTool = tool;
        }
    }

    // ── Tool selection ────────────────────────────────────────────────────────

    // Live-View tools: set ActiveTool and switch to Live View tab
    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
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
        if (_syncing) return;
        if (sender is RadioButton rb && rb.Tag is string toolName
            && Enum.TryParse<DesignTool>(toolName, out var tool)
            && VM is MainViewModel vm)
        {
            vm.IsDesignMode = true;
            vm.DesignCanvas.ActiveTool = tool;
        }
    }

    // Design-Canvas Sign: same signature popup as Live View, anchored to the Design button
    // (Click, not Checked: clicking the already-selected Sign button reopens the popup.)
    private void DesignSign_Click(object sender, RoutedEventArgs e)
    {
        if (VM is not MainViewModel vm) return;
        vm.IsDesignMode = true;
        vm.DesignCanvas.ActiveTool = DesignTool.Sign;
        SigPopup.PlacementTarget = DesignSignBtn;
        SigPopup.IsOpen = true;
    }

    /// <summary>Arms the signature for placement in whichever view is showing.</summary>
    private void UseSignature(MainViewModel vm, byte[] png)
    {
        if (vm.IsDesignMode)
        {
            vm.DesignCanvas.PendingSignature = png;
            vm.DesignCanvas.ActiveTool = DesignTool.Sign;
        }
        else
        {
            vm.PendingLibrarySignature = png;
            vm.ActiveTool = ActiveTool.Signature;
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
            vm.IsDesignMode = false;
            vm.ActiveTool = tool;
            AddTextGlyph.Text = tool switch
            {
                ActiveTool.Checkmark => "✓",
                ActiveTool.XMark     => "✕",
                ActiveTool.Dot       => "●",
                ActiveTool.Circle    => "○",
                ActiveTool.Line      => "—",
                _                    => "A",
            };
        }
    }

    // ── Signature popup (image 1 style) ──────────────────────────────────────

    private void SignBtn_Click(object sender, RoutedEventArgs e)
    {
        SigPopup.PlacementTarget = SignBtn;
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
                UseSignature(vm, sig.ImageBytes);
                ToastService.Instance.Info(vm.IsDesignMode
                    ? $"'{sig.Name}' selected — click a signature field or the page to place."
                    : $"'{sig.Name}' selected — click the page to place.");
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
            UseSignature(vm, dlg.Result.ImageBytes);
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
            UseSignature(vm, dlg.Result.ImageBytes);
            ToastService.Instance.Info("Initials created — click the page to place.");
        }
    }

    // ── Highlight tool ───────────────────────────────────────────────────────

    private void HighlightTool_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
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
            UseSignature(vm, dlg.Result.ImageBytes);
    }
}
