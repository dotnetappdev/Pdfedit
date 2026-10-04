using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>Search every PDF in a folder for some text; open a result at its page.</summary>
public partial class FolderSearchDialog : Window
{
    private readonly ObservableCollection<SearchHit> _hits = new();
    private readonly Func<string, int, Task> _open;
    private CancellationTokenSource? _cts;

    public FolderSearchDialog(string? startFolder, Func<string, int, Task> open)
    {
        InitializeComponent();
        _open = open;
        Results.ItemsSource = _hits;
        FolderBox.Text = startFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Loaded += (_, _) => QueryBox.Focus();
        Closed += (_, _) => _cts?.Cancel();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Search PDFs in" };
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        string q = QueryBox.Text, folder = FolderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(q)) { QueryBox.Focus(); return; }
        if (!Directory.Exists(folder)) { StatusText.Text = "That folder doesn't exist."; return; }

        _hits.Clear();
        _cts = new CancellationTokenSource();
        SearchBtn.Content = "Stop";
        bool sub = SubBox.IsChecked == true, mc = CaseBox.IsChecked == true, ww = WordBox.IsChecked == true;
        var progress = new Progress<(int Done, int Total, string File)>(p =>
            StatusText.Text = p.Done < p.Total ? $"Searching {p.Done + 1} of {p.Total}: {Path.GetFileName(p.File)}…" : $"{_hits.Count} match(es) in {p.Total} PDF(s).");
        try
        {
            var token = _cts.Token;
            await Task.Run(() => FolderSearchService.Search(folder, sub, q, mc, ww,
                hit => Dispatcher.Invoke(() => _hits.Add(hit)), progress, token));
        }
        catch (OperationCanceledException) { StatusText.Text = $"Stopped — {_hits.Count} match(es) so far."; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally
        {
            _cts = null;
            SearchBtn.Content = "Search";
        }
    }

    private async void OpenSelected()
    {
        if (Results.SelectedItem is SearchHit h) await _open(h.File, h.Page);
    }

    private void Results_DoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();
    private void Results_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) OpenSelected(); }
}
