using System.Windows;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>
/// The old-school Tip of the Day: one tip at a time with Previous / Next, and a "Show tips at
/// startup" box. It remembers where you got to, so each start shows a new tip.
/// </summary>
public partial class TipOfTheDayDialog : Window
{
    private int _index;

    public TipOfTheDayDialog()
    {
        InitializeComponent();
        var s = AppSettings.Current;
        _index = Wrap(s.NextTipIndex);
        ShowAtStartup.IsChecked = s.ShowTipsAtStartup;
        ShowTip();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } };
        Closed += (_, _) =>
        {
            s.ShowTipsAtStartup = ShowAtStartup.IsChecked == true;
            s.NextTipIndex = Wrap(_index + 1);   // next time, start with the one after
            s.Save();
        };
    }

    private static int Wrap(int i) => ((i % TipsCatalog.All.Count) + TipsCatalog.All.Count) % TipsCatalog.All.Count;

    private void ShowTip()
    {
        var tip = TipsCatalog.All[_index];
        TipTitle.Text = tip.Title;
        TipText.Text = tip.Text;
        TipCount.Text = $"Tip {_index + 1} of {TipsCatalog.All.Count}";
    }

    private void Next_Click(object sender, RoutedEventArgs e) { _index = Wrap(_index + 1); ShowTip(); }

    private void Previous_Click(object sender, RoutedEventArgs e) { _index = Wrap(_index - 1); ShowTip(); }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
