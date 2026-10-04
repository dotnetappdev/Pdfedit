using System.IO;
using System.Windows;
using System.Windows.Controls;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>Visual comparison of two PDFs, page by page, with the differences coloured.</summary>
public partial class VisualCompareDialog : Window
{
    private List<PageDiff> _diffs = new();

    public VisualCompareDialog(string? newPath)
    {
        InitializeComponent();
        NewBox.Text = newPath ?? "";
    }

    private static string? Pick(string title)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF files|*.pdf", Title = title };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private void BrowseOld_Click(object sender, RoutedEventArgs e) { if (Pick("Old version") is { } p) OldBox.Text = p; }
    private void BrowseNew_Click(object sender, RoutedEventArgs e) { if (Pick("New version") is { } p) NewBox.Text = p; }

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(OldBox.Text) || !File.Exists(NewBox.Text)) { AppDialog.ShowInfo("Choose both PDFs to compare.", "Compare"); return; }
        CompareBtn.IsEnabled = false;
        PageList.Items.Clear();
        try
        {
            var progress = new Progress<(int Done, int Total)>(p => Title = p.Done < p.Total ? $"Comparing page {p.Done + 1} of {p.Total}…" : "Compare PDFs (visual)");
            _diffs = await VisualCompareService.CompareAsync(OldBox.Text, NewBox.Text, progress, CancellationToken.None);
            foreach (var d in _diffs)
                PageList.Items.Add(d.Old == null ? $"Page {d.Page} — added" : d.New == null ? $"Page {d.Page} — removed"
                                   : d.ChangedPercent < 0.05 ? $"Page {d.Page} — no change" : $"Page {d.Page} — {d.ChangedPercent:0.#}% changed");
            int changedPages = _diffs.Count(d => d.Old == null || d.New == null || d.ChangedPercent >= 0.05);
            Title = $"Compare PDFs (visual) — {changedPages} of {_diffs.Count} page(s) differ";
            int first = _diffs.FindIndex(d => d.Old == null || d.New == null || d.ChangedPercent >= 0.05);
            PageList.SelectedIndex = Math.Max(0, first);
        }
        catch (Exception ex) { AppDialog.ShowError("Couldn't compare the PDFs.", ex); }
        finally { CompareBtn.IsEnabled = true; }
    }

    private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e) => Show();
    private void View_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) Show(); }

    private void Show()
    {
        int i = PageList.SelectedIndex;
        if (i < 0 || i >= _diffs.Count) return;
        var d = _diffs[i];
        bool side = ViewSide.IsChecked == true;
        SideView.Visibility = side ? Visibility.Visible : Visibility.Collapsed;
        SingleView.Visibility = side ? Visibility.Collapsed : Visibility.Visible;
        OldImage.Source = d.Old;
        NewImage.Source = d.New;
        MainImage.Source = ViewOld.IsChecked == true ? d.Old : ViewNew.IsChecked == true ? d.New : d.Diff;
    }
}
