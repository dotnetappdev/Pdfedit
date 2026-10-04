using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>Ask one question across several PDFs; the answer cites file and page, and the sources open there.</summary>
public partial class AskAcrossDialog : Window
{
    private sealed record FileEntry(string Path) { public string Name => System.IO.Path.GetFileName(Path); }

    private readonly Func<string, string, Action<string>, CancellationToken, Task> _ask;
    private readonly Func<string, int, Task> _open;
    private List<DocPage>? _pages;
    private CancellationTokenSource? _cts;

    /// <param name="ask">(systemPrompt, question with documents, onChunk, ct) → streams the answer.</param>
    /// <param name="open">Opens a file at a page.</param>
    public AskAcrossDialog(IEnumerable<string> start, Func<string, string, Action<string>, CancellationToken, Task> ask, Func<string, int, Task> open)
    {
        InitializeComponent();
        _ask = ask;
        _open = open;
        foreach (var f in start) Add(f);
        Closed += (_, _) => _cts?.Cancel();
        Loaded += (_, _) => QuestionBox.Focus();
    }

    private void Add(string path)
    {
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return;
        if (FileList.Items.Cast<FileEntry>().Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        FileList.Items.Add(new FileEntry(path));
        _pages = null;
        FileInfo.Text = $"{FileList.Items.Count} PDF(s)";
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF files|*.pdf", Multiselect = true, Title = "Add PDFs" };
        if (dlg.ShowDialog(this) == true) foreach (var f in dlg.FileNames) Add(f);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Add every PDF in a folder" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dlg.FolderName, "*.pdf", SearchOption.AllDirectories).Take(500)) Add(f);
        }
        catch (Exception ex) { AppDialog.ShowError("Couldn't read that folder.", ex); }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in FileList.SelectedItems.Cast<FileEntry>().ToList()) FileList.Items.Remove(f);
        _pages = null;
        FileInfo.Text = $"{FileList.Items.Count} PDF(s)";
    }

    private void Question_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; Ask_Click(sender, e); }
    }

    private async void Ask_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        string question = QuestionBox.Text.Trim();
        var files = FileList.Items.Cast<FileEntry>().Select(f => f.Path).ToList();
        if (files.Count == 0) { StatusLine.Text = "Add some PDFs first."; return; }
        if (question.Length == 0) { QuestionBox.Focus(); return; }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        AskBtn.Content = "Stop";
        Hint.Visibility = Visibility.Collapsed;
        Answer.Markdown = "";
        Sources.Children.Clear();
        try
        {
            if (_pages == null)
            {
                var progress = new Progress<string>(name => StatusLine.Text = $"Reading {name}…");
                _pages = await Task.Run(() => MultiDocService.Read(files, progress, ct), ct);
            }
            if (_pages.Count == 0) { StatusLine.Text = "None of these PDFs has text to read (scans need text recognition first)."; return; }
            int docs = _pages.Select(p => p.Path).Distinct().Count();
            StatusLine.Text = $"Asking about {docs} document(s), {_pages.Count} pages…";
            string context = await Task.Run(() => MultiDocService.Context(question, _pages), ct);
            string answer = "";
            await _ask(MultiDocService.Instructions, $"Question: {question}\n\nDocuments:\n{context}", chunk =>
                Dispatcher.Invoke(() => { answer += chunk; Answer.Markdown = answer; AnswerScroll.ScrollToEnd(); }), ct);
            StatusLine.Text = "Sources:";
            ShowSources(answer);
        }
        catch (OperationCanceledException) { StatusLine.Text = "Stopped."; }
        catch (Exception ex) { StatusLine.Text = "Couldn't answer: " + ex.Message; }
        finally
        {
            _cts = null;
            AskBtn.Content = "Ask";
        }
    }

    private void ShowSources(string answer)
    {
        var byName = FileList.Items.Cast<FileEntry>().GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Path, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, page) in MultiDocService.Citations(answer))
        {
            if (!byName.TryGetValue(name, out var path)) continue;
            var b = new Button { Content = $"{name} · p.{page}", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 6), ToolTip = path, Cursor = Cursors.Hand };
            b.Click += async (_, _) => await _open(path, page);
            Sources.Children.Add(b);
        }
        if (Sources.Children.Count == 0) StatusLine.Text = "";
    }
}
