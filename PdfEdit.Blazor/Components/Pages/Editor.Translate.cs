using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

// Translate PDF: every paragraph translated by the AI and written back in place, keeping the
// layout (PdfEdit.Core's TranslateService, as in the Windows app). It's a change like any other,
// so Undo brings the original back.
public partial class Editor
{
    private CancellationTokenSource? _translateCts;

    public static readonly string[] TranslateLanguages =
    [
        "English", "Spanish", "French", "German", "Italian", "Portuguese", "Dutch", "Polish", "Swedish", "Danish",
        "Norwegian", "Finnish", "Greek", "Turkish", "Russian", "Ukrainian", "Czech", "Romanian", "Hungarian",
        "Chinese (Simplified)", "Chinese (Traditional)", "Japanese", "Korean", "Hindi", "Thai", "Vietnamese",
        "Indonesian", "Malay", "Filipino", "Welsh", "Irish",
    ];

    public string TranslateLanguage { get; set; } = "French";
    public bool TranslateThisPage { get; set; }
    public string? TranslateStatus { get; private set; }
    public bool Translating { get; private set; }

    public void ShowTranslate()
    {
        if (Doc == null) return;
        TranslateStatus = AiReady ? null : $"Translation uses the AI Assistant: add a {AiProvider} key in AI Assistant → API Keys first.";
        _dialog = DialogKind.Translate;
    }

    public async Task TranslatePdfAsync()
    {
        if (Doc == null || Translating) return;
        string language = TranslateLanguage.Trim();
        if (language.Length == 0) return;
        if (!AiReady) { _dialog = DialogKind.AiSettings; Toast($"Add a {AiProvider} API key first.", "error"); return; }
        var doc = Doc;
        int only = TranslateThisPage ? _page + 1 : 0;
        Translating = true;
        TranslateStatus = "Reading the text…";
        _translateCts = new CancellationTokenSource();
        var ct = _translateCts.Token;
        StateHasChanged();
        try
        {
            await CommitPendingAsync();
            var blocks = await Task.Run(() => TranslateService.GetBlocks(doc.CurrentPath, only), ct);
            if (blocks.Count == 0)
            {
                TranslateStatus = "There's no text to translate. If the pages are scans, run Tools → Recognise Text first.";
                return;
            }
            TranslateStatus = $"Translating {blocks.Count} paragraphs into {language}…";
            StateHasChanged();
            string provider = AiProvider, model = AiModel, key = AiKey;
            var progress = new UiProgress<(int Done, int Total)>(p => InvokeAsync(() =>
            {
                TranslateStatus = $"Translating into {language}: {p.Done} of {p.Total} paragraphs…";
                StateHasChanged();
            }));
            await TranslateService.TranslateAsync(blocks, language,
                (prompt, token) => AiProviderService.CompleteAsync(prompt, provider, model, key, token), progress, ct);
            int done = blocks.Count(b => b.Translation != null);
            if (done == 0) { TranslateStatus = "The AI didn't send back any translations. Try again, or another model."; return; }

            TranslateStatus = "Writing the translated PDF…";
            StateHasChanged();
            await Store.ApplyAsync(doc, (src, dest) => TranslateService.Write(src, dest, blocks, language));
            doc.FileName = $"{Path.GetFileNameWithoutExtension(doc.FileName)} ({language}).pdf";
            LoadValues();
            _dialog = DialogKind.None;
            int missing = blocks.Count - done;
            Status($"Translated into {language} — Undo brings back the original");
            Toast($"Translated {done} paragraph{(done == 1 ? "" : "s")} into {language}." +
                  (missing > 0 ? $" {missing} came back without a translation and were left as they were." : "") +
                  " Download keeps the translation; Undo brings back the original.", missing > 0 ? "" : "success");
        }
        catch (OperationCanceledException) { TranslateStatus = "Stopped."; }
        catch (Exception ex) { TranslateStatus = "Translation failed: " + ex.Message; }
        finally
        {
            Translating = false;
            _translateCts?.Dispose();
            _translateCts = null;
            StateHasChanged();
        }
    }

    public void StopTranslate() => _translateCts?.Cancel();
}
