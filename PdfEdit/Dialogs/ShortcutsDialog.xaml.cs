using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>F1: every shortcut in use now (including your own), plus the Design canvas and form keys.</summary>
public partial class ShortcutsDialog : Window
{
    /// <summary>The user asked to change shortcuts (open Settings → Keyboard).</summary>
    public bool ChangeRequested { get; private set; }

    public ShortcutsDialog()
    {
        InitializeComponent();
        foreach (var group in ShortcutService.Actions.GroupBy(a => a.Category))
        {
            var rows = group.Select(a => (a, keys: ShortcutService.Effective(a))).Where(x => x.keys.Count > 0).ToList();
            if (rows.Count == 0) continue;
            Header(group.Key == "Tools" ? "TOOLS (WHEN YOU'RE NOT TYPING)" : group.Key.ToUpperInvariant());
            foreach (var (a, keys) in rows)
                Line(ShortcutService.Display(keys), a.Label, bold: AppSettings.Current.Shortcuts.ContainsKey(a.Id));
        }
        Header("DESIGN CANVAS");
        foreach (var (k, d) in new[]
        {
            ("V / E", "Select / edit fields"), ("T / R / E / L", "Text / rectangle / ellipse / line"), ("A / P / I", "Arrow / pen / image"),
            ("S", "Signature"), ("Ctrl+A", "Select all"), ("Ctrl+C / Ctrl+V / Ctrl+D", "Copy / paste / duplicate"),
            ("Delete", "Delete selected"), ("Arrow keys", "Nudge (Shift = 10 px)"), ("Ctrl+Arrow", "Resize (Shift = 10 px)"),
            ("Escape", "Deselect / cancel tool"),
        }) Line(k, d);
        Header("FORMS");
        Line("Tab / Shift+Tab", "Next / previous field");
        Line("Space / Enter", "Tick a check box or option");
    }

    private void Header(string text)
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 11, Margin = new Thickness(0, 14, 0, 4) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        Body.Children.Add(t);
    }

    private void Line(string keys, string what, bool bold = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        var k = new TextBlock { Text = keys, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Width = 200, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
        k.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundBrush");
        var d = new TextBlock { Text = what, FontSize = 11.5 };
        d.SetResourceReference(TextBlock.ForegroundProperty, "DimForegroundBrush");
        row.Children.Add(k);
        row.Children.Add(d);
        Body.Children.Add(row);
    }

    private void Change_Click(object sender, RoutedEventArgs e)
    {
        ChangeRequested = true;
        Close();
    }
}
