using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

public partial class SettingsWindow : Window
{
    // Snapshot of interface font sizes taken when the dialog opens, so Cancel
    // can revert any live changes the user previewed.
    private readonly Dictionary<string, double> _fontSnapshot;
    private bool _fontsInitialized;

    private readonly List<string> _fonts = new()
    {
        "Arial", "Times New Roman", "Courier New", "Georgia", "Verdana",
        "Tahoma", "Calibri", "Segoe UI", "Helvetica Neue", "Palatino Linotype"
    };

    // (label, format string) pairs shown with a live preview
    private static readonly (string Label, string Format)[] DateFormats =
    {
        ("Long date",        "MMMM d, yyyy"),
        ("US date",          "MM/dd/yyyy"),
        ("EU date",          "dd/MM/yyyy"),
        ("Dashed (EU)",      "dd-MM-yyyy"),
        ("ISO 8601",         "yyyy-MM-dd"),
        ("Short date",       "d MMM yyyy"),
        ("Full weekday",     "dddd, MMMM d, yyyy"),
        ("Short US",         "M/d/yy"),
    };

    public SettingsWindow()
    {
        InitializeComponent();
        _fontSnapshot = new Dictionary<string, double>(AppSettings.Current.InterfaceFontSizes);
        LoadCurrentSettings();
        InitFontSettings();
    }

    // ── Fonts tab (Visual Studio "Fonts and Colors" style) ───────────────────
    private void InitFontSettings()
    {
        for (double s = InterfaceFonts.MinSize; s <= InterfaceFonts.MaxSize; s++)
            FontSizeCombo.Items.Add(s.ToString("0"));

        // Apply custom sizes typed directly into the editable combo box.
        FontSizeCombo.LostFocus += (_, _) => ApplySelectedFontSize();
        FontSizeCombo.KeyUp += (_, ev) =>
        {
            if (ev.Key == System.Windows.Input.Key.Enter) ApplySelectedFontSize();
        };

        FontAreaList.ItemsSource = InterfaceFonts.Areas;
        _fontsInitialized = true;
        FontAreaList.SelectedIndex = 0; // triggers SelectionChanged → loads editor
    }

    private void FontAreaList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_fontsInitialized || FontAreaList.SelectedItem is not InterfaceFontArea area)
            return;

        FontAreaName.Text = area.DisplayName;
        FontAreaDesc.Text = area.Description;

        double size = FontService.GetSize(area);
        FontSizeCombo.Text = size.ToString("0");
        FontPreview.FontSize = size;
    }

    private void FontSizeCombo_Changed(object sender, SelectionChangedEventArgs e)
        => ApplySelectedFontSize();

    private void ApplySelectedFontSize()
    {
        if (!_fontsInitialized || FontAreaList.SelectedItem is not InterfaceFontArea area)
            return;
        if (!double.TryParse(FontSizeCombo.Text, out var size))
            return;

        size = Math.Clamp(size, InterfaceFonts.MinSize, InterfaceFonts.MaxSize);
        FontService.SetSize(area, size);   // applies live to the whole app
        FontPreview.FontSize = size;
    }

    private void ResetFonts_Click(object sender, RoutedEventArgs e)
    {
        FontService.ResetAll();
        // Refresh the editor for the currently selected area.
        FontAreaList_SelectionChanged(FontAreaList, null!);
    }

    private void LoadCurrentSettings()
    {
        var s = AppSettings.Current;

        // Theme
        RbDark.IsChecked = s.Theme == "Dark";
        RbLight.IsChecked = s.Theme == "Light";
        RbHighContrast.IsChecked = s.Theme == "HighContrast";

        // UI Scale
        UiScaleSlider.Value = s.UiScale;
        UiScaleLabel.Text = $"{(int)(s.UiScale * 100)}%";

        // Editor
        DefaultFontCombo.ItemsSource = _fonts;
        DefaultFontCombo.SelectedItem = _fonts.Contains(s.DefaultFontFamily)
            ? s.DefaultFontFamily : "Arial";
        DefaultFontSizeTb.Text = s.DefaultFontSize.ToString("0");
        DefaultColorTb.Text = s.DefaultFontColor;
        ForceUpperCaseCb.IsChecked = s.ForceUpperCaseDefault;

        // Date formats
        BuildDateFormatPanel(s.DateFormat);

        // AI
        ApiKeyBox.Password = s.ClaudeApiKey;
        OpenAiKeyBox.Password = s.OpenAiApiKey;

        // Accessibility
        HighContrastFocusCb.IsChecked = s.HighContrastFocusIndicators;
    }

    private void BuildDateFormatPanel(string currentFormat)
    {
        DateFormatPanel.Children.Clear();
        var today = DateTime.Today;
        var fg = (Brush)(TryFindResource("ForegroundBrush") ?? Brushes.White);
        var dim = (Brush)(TryFindResource("DimForegroundBrush") ?? Brushes.Gray);

        foreach (var (label, fmt) in DateFormats)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var rb = new RadioButton
            {
                GroupName = "DateFormat",
                Tag = fmt,
                IsChecked = fmt == currentFormat,
                Foreground = fg,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(rb, $"Date format: {label}");

            var labelSp = new StackPanel { Orientation = Orientation.Horizontal };
            labelSp.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = fg,
                FontSize = 13,
                Width = 120,
                VerticalAlignment = VerticalAlignment.Center
            });
            labelSp.Children.Add(new TextBlock
            {
                Text = $"({fmt})",
                Foreground = dim,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            });
            rb.Content = labelSp;
            Grid.SetColumn(rb, 0);

            var preview = new TextBlock
            {
                Text = today.ToString(fmt),
                Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 120)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
                MinWidth = 180
            };
            System.Windows.Automation.AutomationProperties.SetHelpText(preview, $"Preview: {today.ToString(fmt)}");
            Grid.SetColumn(preview, 1);

            row.Children.Add(rb);
            row.Children.Add(preview);
            DateFormatPanel.Children.Add(row);
        }
    }

    private string GetSelectedDateFormat()
    {
        foreach (Grid row in DateFormatPanel.Children)
        {
            if (row.Children[0] is RadioButton rb && rb.IsChecked == true && rb.Tag is string fmt)
                return fmt;
        }
        return "MMMM d, yyyy";
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
            App.SwitchTheme(tag);
    }

    private void UiScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (UiScaleLabel != null)
            UiScaleLabel.Text = $"{(int)(e.NewValue * 100)}%";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;

        // Theme already applied by radio button handler
        s.UiScale = UiScaleSlider.Value;

        s.DefaultFontFamily = DefaultFontCombo.SelectedItem as string ?? "Arial";
        if (double.TryParse(DefaultFontSizeTb.Text, out var fs))
            s.DefaultFontSize = Math.Clamp(fs, 6, 144);
        s.DefaultFontColor = DefaultColorTb.Text;
        s.ForceUpperCaseDefault = ForceUpperCaseCb.IsChecked == true;

        s.ClaudeApiKey = ApiKeyBox.Password;
        s.OpenAiApiKey = OpenAiKeyBox.Password;
        s.HighContrastFocusIndicators = HighContrastFocusCb.IsChecked == true;
        s.DateFormat = GetSelectedDateFormat();

        s.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Restore original theme if user changed it without saving
        App.SwitchTheme(AppSettings.Current.Theme);

        // Revert any live font-size previews to the snapshot taken on open.
        AppSettings.Current.InterfaceFontSizes = new Dictionary<string, double>(_fontSnapshot);
        FontService.ApplyAll();

        DialogResult = false;
        Close();
    }
}
