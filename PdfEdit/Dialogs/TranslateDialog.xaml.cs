using System.IO;
using System.Windows;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>Translate the whole PDF (or one page) into a new, translated copy that keeps the layout.</summary>
public partial class TranslateDialog : Window
{
    private readonly string _source;
    private readonly int _page;
    private readonly Func<string, CancellationToken, Task<string>> _complete;
    private CancellationTokenSource? _cts;

    /// <summary>The translated PDF, when it's done.</summary>
    public string? OutputPath { get; private set; }

    public TranslateDialog(string source, int page, string defaultLanguage, Func<string, CancellationToken, Task<string>> complete)
    {
        InitializeComponent();
        _source = source;
        _page = page;
        _complete = complete;
        foreach (var l in new[] { defaultLanguage, "English", "Spanish", "French", "German", "Italian", "Portuguese", "Dutch", "Polish",
                                  "Swedish", "Danish", "Norwegian", "Finnish", "Greek", "Turkish", "Russian", "Ukrainian", "Czech",
                                  "Romanian", "Hungarian", "Chinese (Simplified)", "Chinese (Traditional)", "Japanese", "Korean",
                                  "Hindi", "Thai", "Vietnamese", "Indonesian", "Malay", "Filipino", "Welsh", "Irish" }.Distinct())
            LanguageBox.Items.Add(l);
        LanguageBox.Text = defaultLanguage;
        Closing += (_, e) => { if (_cts != null) { _cts.Cancel(); } };
    }

    private async void Go_Click(object sender, RoutedEventArgs e)
    {
        string language = LanguageBox.Text.Trim();
        if (language.Length == 0) { LanguageBox.Focus(); return; }
        GoBtn.IsEnabled = LanguageBox.IsEnabled = AllPages.IsEnabled = ThisPage.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.IsIndeterminate = true;
        StatusLine.Text = "Reading the text…";
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            int only = ThisPage.IsChecked == true ? _page : 0;
            var blocks = await Task.Run(() => TranslateService.GetBlocks(_source, only), ct);
            if (blocks.Count == 0)
            {
                StatusLine.Text = "There's no text to translate. If the pages are scans, run text recognition first (Toolkit → Scan & text recognition).";
                Reset();
                return;
            }
            Progress.IsIndeterminate = false;
            Progress.Maximum = blocks.Count;
            StatusLine.Text = $"Translating {blocks.Count} paragraphs into {language}…";
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                Progress.Value = p.Done;
                StatusLine.Text = $"Translating into {language}: {p.Done} of {p.Total} paragraphs…";
            });
            await TranslateService.TranslateAsync(blocks, language, _complete, progress, ct);
            int missing = blocks.Count(b => b.Translation == null);

            string dir = Path.GetDirectoryName(_source)!, name = Path.GetFileNameWithoutExtension(_source);
            string dest = Path.Combine(dir, $"{name} ({language}).pdf");
            for (int i = 2; File.Exists(dest); i++) dest = Path.Combine(dir, $"{name} ({language}) {i}.pdf");
            if (!IsWritable(dir)) dest = Path.Combine(Path.GetTempPath(), Path.GetFileName(dest));
            StatusLine.Text = "Writing the translated PDF…";
            await Task.Run(() => TranslateService.Write(_source, dest, blocks, language), ct);
            OutputPath = dest;
            if (missing > 0)
                AppDialog.ShowInfo($"{missing} of {blocks.Count} paragraphs came back without a translation and were left as they were.", "Translate PDF");
            _cts = null;
            DialogResult = true;
        }
        catch (OperationCanceledException) { _cts = null; if (IsLoaded) { StatusLine.Text = "Stopped."; Reset(); } }
        catch (Exception ex)
        {
            _cts = null;
            StatusLine.Text = "Translation failed: " + ex.Message;
            Reset();
        }
    }

    private static bool IsWritable(string dir)
    {
        try { string t = Path.Combine(dir, ".pdfedit-write-test"); File.WriteAllText(t, ""); File.Delete(t); return true; }
        catch { return false; }
    }

    private void Reset()
    {
        GoBtn.IsEnabled = LanguageBox.IsEnabled = AllPages.IsEnabled = ThisPage.IsEnabled = true;
        Progress.Visibility = Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        DialogResult = false;
    }
}
