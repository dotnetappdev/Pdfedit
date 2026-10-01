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
        PageToolsPill.Visibility = vm.IsDesignMode ? Visibility.Collapsed : Visibility.Visible;
        foreach (var popup in RailPopups()) popup.IsOpen = false;
        SigPopup.IsOpen = MorePopup.IsOpen = false;
    }

    // ── Acrobat quick-tools rail ──────────────────────────────────────────────
    // Each rail button stands for a family of tools; its corner arrow picks one, and the button
    // then shows (and re-selects) the last tool picked, as in Acrobat.

    private static readonly (string Rail, ActiveTool[] Tools)[] RailFamilies =
    {
        ("Select",    new[] { ActiveTool.Select, ActiveTool.Hand, ActiveTool.Zoom, ActiveTool.TextFill, ActiveTool.CheckboxToggle }),
        ("Comment",   new[] { ActiveTool.StickyNote, ActiveTool.DrawCallout, ActiveTool.Stamp,
                              ActiveTool.InsertText, ActiveTool.ReplaceText }),
        ("Highlight", new[] { ActiveTool.Highlight, ActiveTool.Underline, ActiveTool.Strikethrough }),
        ("Draw",      new[] { ActiveTool.DrawFreehand, ActiveTool.DrawRectangle, ActiveTool.DrawEllipse, ActiveTool.DrawArrow,
                              ActiveTool.DrawLine, ActiveTool.DrawCloud, ActiveTool.DrawPolygon, ActiveTool.DrawPolyline, ActiveTool.Eraser }),
        ("AddText",   new[] { ActiveTool.AddText, ActiveTool.VerticalText, ActiveTool.Checkmark, ActiveTool.XMark,
                              ActiveTool.Dot, ActiveTool.Circle, ActiveTool.Line, ActiveTool.DateStamp }),
    };

    private static readonly Dictionary<ActiveTool, string> ToolGlyphs = new()
    {
        [ActiveTool.Select] = "\uE8B0", [ActiveTool.Hand] = "\uE7C9", [ActiveTool.Zoom] = "\uE71E",
        [ActiveTool.TextFill] = "\uE8B0", [ActiveTool.CheckboxToggle] = "\uE8B0",
        [ActiveTool.InsertText] = "\uE710", [ActiveTool.ReplaceText] = "\uE8AC",
        [ActiveTool.StickyNote] = "\uE90A", [ActiveTool.DrawCallout] = "\uE8F2", [ActiveTool.Stamp] = "\uE8F4",
        [ActiveTool.Highlight] = "\uE7C1", [ActiveTool.Underline] = "\uE8DC", [ActiveTool.Strikethrough] = "\uEDE0",
        [ActiveTool.DrawFreehand] = "\uEDC6", [ActiveTool.DrawRectangle] = "\uE7C2", [ActiveTool.DrawEllipse] = "\uEA3A",
        [ActiveTool.DrawArrow] = "\uEBD4", [ActiveTool.Eraser] = "\uED60",
        [ActiveTool.DrawLine] = "\uE738", [ActiveTool.DrawCloud] = "\uE753",
        [ActiveTool.DrawPolygon] = "\uE7C2", [ActiveTool.DrawPolyline] = "\uE8A9",
        // Add text shows the mark itself inside its box
        [ActiveTool.AddText] = "A", [ActiveTool.VerticalText] = "A", [ActiveTool.Checkmark] = "✓", [ActiveTool.XMark] = "✕",
        [ActiveTool.Dot] = "●", [ActiveTool.Circle] = "○", [ActiveTool.Line] = "—", [ActiveTool.DateStamp] = "31",
    };

    private RadioButton RailButton(string rail) => (RadioButton)FindName(rail + "Btn");
    private TextBlock RailGlyph(string rail) => (TextBlock)FindName(rail + "Glyph");
    private IEnumerable<System.Windows.Controls.Primitives.Popup> RailPopups() =>
        RailFamilies.Select(f => (System.Windows.Controls.Primitives.Popup)FindName(f.Rail + "Popup"));

    /// <summary>Makes <paramref name="tool"/> the one its rail button shows and re-selects.</summary>
    private void ShowOnRail(string rail, ActiveTool tool)
    {
        RailButton(rail).Tag = tool.ToString();
        if (ToolGlyphs.TryGetValue(tool, out var g)) RailGlyph(rail).Text = g;
    }

    private void SyncLiveChecked(ActiveTool tool)
    {
        _syncing = true;
        try
        {
            bool found = false;
            foreach (var (rail, tools) in RailFamilies)
            {
                bool match = tools.Contains(tool);
                if (match) ShowOnRail(rail, tool);
                RailButton(rail).IsChecked = match;
                found |= match;
            }
            // Signature has its own button; anything else lives in the More popup.
            found |= tool == ActiveTool.Signature;
            MoreBtn.Background = found ? Brushes.Transparent : (Brush)FindResource("AccentBrush");
        }
        finally { _syncing = false; }
    }

    private void RailTool_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Tag: string name } || VM is not MainViewModel vm) return;
        if (!Enum.TryParse<ActiveTool>(name, out var tool)) return;
        vm.IsDesignMode = false;
        vm.ActiveTool = tool;
    }

    private void Corner_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string popupName } && FindName(popupName) is System.Windows.Controls.Primitives.Popup popup)
            popup.IsOpen = !popup.IsOpen;
    }

    /// <summary>A tool picked from a rail button's corner menu.</summary>
    private void SubTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } || !Enum.TryParse<ActiveTool>(name, out var tool)) return;
        foreach (var popup in RailPopups()) popup.IsOpen = false;
        if (VM is not MainViewModel vm) return;
        vm.IsDesignMode = false;
        vm.ActiveTool = tool;           // SyncLiveChecked updates the rail button
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

    // ── Colour (in the Draw and Add text corner menus) ─────────────────────────

    private void InkColor_Click(object sender, RoutedEventArgs e)
    {
        foreach (var popup in RailPopups()) popup.IsOpen = false;
        if (sender is not Button { Tag: string hex } || VM is not MainViewModel vm) return;
        if (hex.Length == 0)
        {
            vm.MarkColor = null;       // "Default": green ✓, black ✕
            return;
        }
        vm.CurrentFontColor = hex;     // text (also recolours the selected text)
        vm.CurrentDrawingColor = hex;  // freehand pen and shapes
        vm.MarkColor = hex;            // ✓ ✕ ● ○ — marks
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

    private void ShowComments_Click(object sender, RoutedEventArgs e)
    {
        foreach (var popup in RailPopups()) popup.IsOpen = false;
        VM?.ShowCommentsPanel();
    }

    private void MeasureUnit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string unit } && VM is MainViewModel vm)
        {
            vm.MeasureUnit = unit;
            vm.StatusText = $"Measurements in {unit}.";
        }
    }

    // ── Tool selection ────────────────────────────────────────────────────────

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

    private void HlColor_Click(object sender, RoutedEventArgs e)
    {
        HighlightPopup.IsOpen = false;
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
