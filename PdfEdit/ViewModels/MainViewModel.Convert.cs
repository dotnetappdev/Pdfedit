using System.IO;
using System.Windows.Input;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>Export to HTML, Markdown and ePub (text with headings, lists and paragraphs).</summary>
public partial class MainViewModel
{
    private ICommand? _exportHtmlCommand, _exportMarkdownCommand, _exportEpubCommand;

    public ICommand ExportHtmlCommand => _exportHtmlCommand ??= new AsyncRelayCommand(
        () => ExportTextFormatAsync("HTML web page", "html", DocumentConvertService.ToHtml), () => HasDocument);
    public ICommand ExportMarkdownCommand => _exportMarkdownCommand ??= new AsyncRelayCommand(
        () => ExportTextFormatAsync("Markdown", "md", DocumentConvertService.ToMarkdown), () => HasDocument);
    public ICommand ExportEpubCommand => _exportEpubCommand ??= new AsyncRelayCommand(
        () => ExportTextFormatAsync("ePub e-book", "epub", DocumentConvertService.ToEpub), () => HasDocument);

    private async Task ExportTextFormatAsync(string kind, string ext, Func<string, string, int> convert)
    {
        if (_currentFilePath == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Export as {kind}",
            Filter = $"{kind} (*.{ext})|*.{ext}",
            FileName = Path.GetFileNameWithoutExtension(_currentFilePath) + "." + ext,
            InitialDirectory = Path.GetDirectoryName(_currentFilePath),
        };
        if (dlg.ShowDialog() != true) return;
        string src = _currentFilePath, dest = dlg.FileName;
        try
        {
            StatusText = $"Exporting as {kind}…";
            int pages = await Task.Run(() => convert(src, dest));
            StatusText = $"Exported {pages} page(s) to {Path.GetFileName(dest)}.";
            ToastService.Instance.Success($"Saved as {kind}. Scanned pages need text recognition (OCR) first to have text.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError($"Couldn't export as {kind}.", ex);
        }
    }
}
