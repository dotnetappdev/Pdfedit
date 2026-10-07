using System.IO;
using System.Windows;
using System.Windows.Input;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>Translate PDF, Ask across PDFs, Mind map and Scan clean-up.</summary>
public partial class MainViewModel
{
    private ICommand? _translatePdfCommand, _askAcrossCommand, _mindMapCommand, _scanCleanupCommand;

    private bool RequireAi()
    {
        if (IsAiConfigured) return true;
        ToastService.Instance.Warning("Set up an AI provider first: Settings → AI Helper (Claude, OpenAI or a free local model).");
        return false;
    }

    // ── Translate the whole PDF, keeping the layout ─────────────────────────
    public ICommand TranslatePdfCommand => _translatePdfCommand ??= new AsyncRelayCommand(async () =>
    {
        if (_currentFilePath == null || !RequireAi()) return;
        string provider = _aiProvider, model = _aiModel, key = CurrentAiKey;
        var dlg = new Dialogs.TranslateDialog(_currentFilePath, _currentPageIndex + 1, TranslateLanguage,
            (prompt, ct) => AiProviderService.CompleteAsync(prompt, provider, model, key, ct,
                systemPrompt: "You are a professional translator. You reply with JSON only."))
        { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || dlg.OutputPath == null) return;
        SaveDocumentState();
        await LoadDocumentAsync(dlg.OutputPath);
        ToastService.Instance.Success($"Translated copy saved as {Path.GetFileName(dlg.OutputPath)}.");
    }, () => HasDocument);

    // ── Ask across several PDFs ─────────────────────────────────────────────
    public ICommand AskAcrossPdfsCommand => _askAcrossCommand ??= new RelayCommand(() =>
    {
        if (!RequireAi()) return;
        var start = OpenTabs.Select(t => t.Path).Where(File.Exists).ToList();
        var dlg = new Dialogs.AskAcrossDialog(start,
            (system, prompt, onChunk, ct) => AiProviderService.SendStreamingAsync(
                new[] { new AiChatMessage { Role = "user", Content = prompt } }, _aiProvider, _aiModel, CurrentAiKey, onChunk, ct, systemPrompt: system),
            async (path, page) => { await OpenAtPageAsync(path, page); Application.Current.MainWindow?.Activate(); })
        { Owner = Application.Current.MainWindow };
        dlg.Show();   // modeless: keep it open while reading the sources
    });

    // ── Mind map ────────────────────────────────────────────────────────────
    public ICommand MindMapCommand => _mindMapCommand ??= new AsyncRelayCommand(async () =>
    {
        if (_currentFilePath == null || !RequireAi()) return;
        if (string.IsNullOrEmpty(_documentText)) { ToastService.Instance.Info("The document's text is still loading — try again in a moment."); return; }
        string name = Path.GetFileNameWithoutExtension(_currentFilePath), path = _currentFilePath;
        StatusText = "Making a mind map…";
        IsAiRunning = true;
        try
        {
            string reply = await AiProviderService.CompleteAsync(
                "Make a mind map of this document. Reply with JSON only, in this shape: " +
                "{\"title\": \"short title of the document\", \"children\": [{\"title\": \"main topic (max 6 words)\", \"page\": 1, " +
                "\"children\": [{\"title\": \"key point (max 9 words)\", \"page\": 2, \"children\": []}]}]}. " +
                "Use 4 to 8 main topics with 2 to 5 key points each, and a third level only where it really helps. " +
                "\"page\" is the page number where the topic is found (pages are marked [Page N]).\n\nDocument:\n" + _documentText,
                _aiProvider, _aiModel, CurrentAiKey, systemPrompt: "You turn documents into clear, well-organised mind maps. You reply with JSON only.");
            var root = Dialogs.MindNode.Parse(reply);
            if (root == null || root.Children.Count == 0) { StatusText = "Ready"; Dialogs.AppDialog.ShowInfo("The AI didn't return a mind map. Try again, or try another model.", "Mind map"); return; }
            if (string.IsNullOrWhiteSpace(root.Title)) root.Title = name;
            StatusText = "Mind map ready.";
            new Dialogs.MindMapWindow(root, name, page =>
            {
                if (string.Equals(path, _currentFilePath, StringComparison.OrdinalIgnoreCase)) GoToCitedPage(page);
                else _ = OpenAtPageAsync(path, page);
            }) { Owner = Application.Current.MainWindow }.Show();
        }
        catch (Exception ex) { StatusText = "Mind map failed."; Dialogs.AppDialog.ShowError("Couldn't make the mind map.", ex); }
        finally { IsAiRunning = false; }
    }, () => HasDocument);

    // ── Scan clean-up ───────────────────────────────────────────────────────
    public ICommand ScanCleanupCommand => _scanCleanupCommand ??= new AsyncRelayCommand(async () =>
    {
        if (_currentFilePath == null || _document == null) return;
        var dlg = new Dialogs.ScanCleanupDialog(_document.PageCount, i => _renderService.RenderPageAsync(i, 1.0, 1.0))
        { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;
        var plans = dlg.Plans;
        string what = "";
        if (await ModifyCurrentFileAsync((i, o) => what = ScanCleanupService.Apply(i, o, plans), "Scan clean-up"))
            ToastService.Instance.Success(what + ". Ctrl+Z undoes it.");
    }, () => HasDocument);
}
