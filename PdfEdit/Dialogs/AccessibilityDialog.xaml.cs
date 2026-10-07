using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>
/// the usual Accessibility Checker: runs <see cref="AccessibilityService.Check"/> on the open PDF,
/// lists every rule as passed / failed / warning / check-by-hand, and fixes the document-level
/// problems (title, language, tab order, field and link descriptions) in one go.
/// </summary>
public partial class AccessibilityDialog : Window
{
    private readonly Func<string?> _path;
    private readonly Func<AccessibilityFixOptions, Task<bool>> _fix;
    private List<AccessibilityCheck> _checks = new();

    public AccessibilityDialog(Func<string?> path, Func<AccessibilityFixOptions, Task<bool>> fix)
    {
        InitializeComponent();
        _path = path;
        _fix = fix;

        foreach (var c in new[] { CultureInfo.CurrentUICulture.Name, "en-GB", "en-US", "fr-FR", "de-DE", "es-ES", "it-IT", "nl-NL", "pt-PT", "pt-BR", "pl-PL", "sv-SE", "da-DK", "nb-NO", "fi-FI", "ja-JP", "zh-CN", "ko-KR", "ar-SA" }.Distinct())
            if (c.Length > 0) LangBox.Items.Add(c);
        LangBox.Text = CultureInfo.CurrentUICulture.Name.Length > 0 ? CultureInfo.CurrentUICulture.Name : "en-GB";

        Loaded += async (_, _) => await RunCheckAsync();
    }

    private async Task RunCheckAsync()
    {
        string? path = _path();
        if (path == null) { Close(); return; }
        Summary.Text = "Checking…";
        Results.Children.Clear();
        try
        {
            _checks = await Task.Run(() => AccessibilityService.Check(path));
        }
        catch (Exception ex)
        {
            Summary.Text = "The check couldn't finish.";
            Results.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap });
            return;
        }
        ShowResults(path);
    }

    private void ShowResults(string path)
    {
        int failed = _checks.Count(c => c.Status == CheckStatus.Failed);
        int warn = _checks.Count(c => c.Status == CheckStatus.Warning);
        int passed = _checks.Count(c => c.Status == CheckStatus.Passed);
        int manual = _checks.Count(c => c.Status == CheckStatus.Manual);
        Summary.Text = failed == 0 && warn == 0
            ? $"No problems found · {passed} passed · {manual} to check by hand"
            : $"{failed} failed · {warn} warning{(warn == 1 ? "" : "s")} · {passed} passed · {manual} to check by hand";

        foreach (var group in _checks.GroupBy(c => c.Category))
        {
            var header = new TextBlock { Text = group.Key.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
            header.SetResourceReference(TextBlock.ForegroundProperty, "DimForegroundBrush");
            Results.Children.Add(header);
            foreach (var c in group.OrderBy(c => c.Status switch { CheckStatus.Failed => 0, CheckStatus.Warning => 1, CheckStatus.Manual => 3, _ => 2 }))
                Results.Children.Add(Row(c));
        }

        // Fix panel: only the fixes that are needed.
        bool Needs(string key) => _checks.Any(c => c.FixKey == key && c.CanFix);
        TitleBox.Text = Needs("title") ? Path.GetFileNameWithoutExtension(path) : "";
        TitleBox.IsEnabled = Needs("title");
        LangBox.IsEnabled = Needs("language");
        DisplayTitleBox.IsChecked = DisplayTitleBox.IsEnabled = Needs("displaytitle");
        TabOrderBox.IsChecked = TabOrderBox.IsEnabled = Needs("taborder");
        TooltipsBox.IsChecked = TooltipsBox.IsEnabled = Needs("tooltips");
        LinksBox.IsChecked = LinksBox.IsEnabled = Needs("links");
        bool any = _checks.Any(c => c.CanFix);
        FixPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        FixBtn.IsEnabled = any;
    }

    private static UIElement Row(AccessibilityCheck c)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 5) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new TextBlock
        {
            Text = c.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Margin = new Thickness(0, 2, 0, 0),
            Foreground = new SolidColorBrush(c.Status switch
            {
                CheckStatus.Passed => Color.FromRgb(0x34, 0xA8, 0x53),
                CheckStatus.Failed => Color.FromRgb(0xE0, 0x5A, 0x4F),
                CheckStatus.Warning => Color.FromRgb(0xE8, 0xA3, 0x17),
                _ => Color.FromRgb(0x5B, 0x8D, 0xEF),
            }),
            ToolTip = c.StatusText,
        };
        var text = new StackPanel();
        var rule = new TextBlock { Text = c.Rule, FontWeight = FontWeights.SemiBold, FontSize = 13 };
        var detail = new TextBlock { Text = c.Detail, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 1, 0, 0) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "DimForegroundBrush");
        text.Children.Add(rule);
        text.Children.Add(detail);
        Grid.SetColumn(text, 1);
        g.Children.Add(icon);
        g.Children.Add(text);
        System.Windows.Automation.AutomationProperties.SetName(g, $"{c.Rule}: {c.StatusText}. {c.Detail}");
        return g;
    }

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        var opt = new AccessibilityFixOptions
        {
            Title = TitleBox.IsEnabled ? TitleBox.Text : null,
            Language = LangBox.IsEnabled ? LangBox.Text : null,
            DisplayTitle = DisplayTitleBox.IsChecked == true,
            TabOrder = TabOrderBox.IsChecked == true,
            FieldTooltips = TooltipsBox.IsChecked == true,
            LinkDescriptions = LinksBox.IsChecked == true,
            MarkTagged = _checks.Any(c => c.FixKey == "marked" && c.CanFix),
        };
        FixBtn.IsEnabled = false;
        if (await _fix(opt)) await RunCheckAsync();
        else FixBtn.IsEnabled = true;
    }

    private async void Recheck_Click(object sender, RoutedEventArgs e) => await RunCheckAsync();
}
