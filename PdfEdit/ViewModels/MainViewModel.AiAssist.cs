using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>
/// AI helpers around the document: questions about selected text or an area of the page, the
/// generative summary panel, writing presets (email, study notes, quiz …), app commands the
/// assistant can run, and asking by voice.
/// </summary>
public partial class MainViewModel
{
    // ── Selected text / area ────────────────────────────────────────────────

    /// <summary>The language replies are translated into: the setting, else Windows' language.</summary>
    public static string TranslateLanguage =>
        !string.IsNullOrWhiteSpace(AppSettings.Current.AiTranslateLanguage) ? AppSettings.Current.AiTranslateLanguage
        : (CultureInfo.CurrentUICulture.IsNeutralCulture ? CultureInfo.CurrentUICulture : CultureInfo.CurrentUICulture.Parent).EnglishName;

    public async Task AskAboutSelectionAsync(string kind, string text, byte[]? png, int page)
    {
        string quote = text.Length > 6000 ? text[..6000] + "…" : text;
        string prompt = kind switch
        {
            "explain" => $"Explain this passage from page {page} in plain language. Say what it means and anything important to watch out for.\n\n> {Quote(quote)}",
            "summarise" => $"Summarise this passage from page {page} in a few short bullet points.\n\n> {Quote(quote)}",
            "rewrite" => $"Rewrite this passage from page {page} so it's clear and easy to read, keeping the meaning. Give the rewritten text, then one line on what you changed.\n\n> {Quote(quote)}",
            "translate" => $"Translate this passage from page {page} into {TranslateLanguage}. Give only the translation.\n\n> {Quote(quote)}",
            "image" => $"This is an area of page {page} of the open PDF. Describe what it shows and explain it: read any text, and explain any chart, table, figures or numbers.",
            _ => quote,
        };
        await SendChatMessageAsync(prompt, png);
    }

    private static string Quote(string s) => s.Replace("\n", "\n> ");

    /// <summary>Puts the selected text in the chat box for the user to finish the question.</summary>
    public void StartQuestionAboutSelection(string text, int page)
    {
        ShowAiPanel = true;
        AiChatInput = $"About this passage on page {page}:\n> {Quote(text.Length > 3000 ? text[..3000] + "…" : text)}\n\n";
        AiInputFocusRequested?.Invoke();
    }

    /// <summary>Asks the chat panel to focus its input box (caret at the end).</summary>
    public event Action? AiInputFocusRequested;

    /// <summary>A PNG of part of a page (area in PDF points, origin bottom-left).</summary>
    public async Task<byte[]?> CapturePageAreaAsync(int pageIndex, Rect area, double zoom = 2.0, int maxSide = 1568)
    {
        if (_document == null) return null;
        try
        {
            var bmp = await _renderService.RenderPageAsync(pageIndex, zoom, 1.0);
            var size = _document.PageSizes[pageIndex];
            double sx = bmp.PixelWidth / size.Width, sy = bmp.PixelHeight / size.Height;
            var px = new Int32Rect(
                (int)Math.Clamp(area.Left * sx, 0, bmp.PixelWidth - 1),
                (int)Math.Clamp((size.Height - area.Bottom) * sy, 0, bmp.PixelHeight - 1), 0, 0);
            px.Width = (int)Math.Clamp(area.Width * sx, 1, bmp.PixelWidth - px.X);
            px.Height = (int)Math.Clamp(area.Height * sy, 1, bmp.PixelHeight - px.Y);
            BitmapSource crop = new CroppedBitmap(bmp, px);
            // Keep the picture a sensible size (for the AI, 1568 px).
            double fit = Math.Min(1.0, (double)maxSide / Math.Max(crop.PixelWidth, crop.PixelHeight));
            if (fit < 1.0) crop = new TransformedBitmap(crop, new System.Windows.Media.ScaleTransform(fit, fit));
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(crop));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    // ── Generative summary panel ────────────────────────────────────────────

    private readonly Dictionary<string, string> _summaries = new(StringComparer.OrdinalIgnoreCase);
    private string _summaryText = "";
    private bool _isSummarising;
    private CancellationTokenSource? _summaryCts;
    private ICommand? _generateSummaryCommand;

    public string SummaryText { get => _summaryText; private set { _summaryText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSummary)); } }
    public bool HasSummary => _summaryText.Length > 0;
    public bool IsSummarising { get => _isSummarising; private set { _isSummarising = value; OnPropertyChanged(); } }

    public ICommand GenerateSummaryCommand => _generateSummaryCommand ??= new AsyncRelayCommand(GenerateSummaryAsync, () => HasDocument);

    /// <summary>Shows the summary already made for this file (called when a document loads).</summary>
    private void ShowCachedSummary()
    {
        _summaryCts?.Cancel();
        IsSummarising = false;
        SummaryText = _currentFilePath != null && _summaries.TryGetValue(_currentFilePath, out var s) ? s : "";
    }

    private async Task GenerateSummaryAsync()
    {
        if (_currentFilePath == null) return;
        if (!IsAiConfigured) { ToastService.Instance.Warning("Set up an AI provider first (Settings → AI Assistant)."); return; }
        if (string.IsNullOrEmpty(_documentText)) { ToastService.Instance.Info("The document's text is still loading — try again in a moment."); return; }
        SummaryPanelRequested?.Invoke();

        _summaryCts?.Cancel();
        _summaryCts = new CancellationTokenSource();
        var ct = _summaryCts.Token;
        string path = _currentFilePath, raw = "";
        IsSummarising = true;
        SummaryText = "";
        try
        {
            var msg = new AiChatMessage
            {
                Role = "user",
                Content = "Write a generative summary of this document as an outline:\n" +
                          "- Start with a two-sentence overview in bold.\n" +
                          "- Then one '##' heading per section of the document, each followed by (p. N) for the page it starts on, " +
                          "and 2–4 bullet points with the key facts (names, dates, amounts, obligations).\n" +
                          "- Finish with a '## Key dates and actions' section if the document has any.\n" +
                          "Use only the document. Cite pages as (p. N).\n\nDocument (pages marked [Page N]):\n" + _documentText,
            };
            await AiProviderService.SendStreamingAsync(new[] { msg }, _aiProvider, _aiModel, CurrentAiKey,
                chunk => Application.Current.Dispatcher.Invoke(() =>
                {
                    if (ct.IsCancellationRequested || !string.Equals(path, _currentFilePath, StringComparison.OrdinalIgnoreCase)) return;
                    raw += chunk;
                    SummaryText = raw;
                }), ct,
                systemPrompt: "You summarise documents accurately and concisely in Markdown.");
            if (!ct.IsCancellationRequested) _summaries[path] = raw;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SummaryText = $"Couldn't make the summary: {ex.Message}"; }
        finally { if (!ct.IsCancellationRequested) IsSummarising = false; }
    }

    /// <summary>Asks the window to show the Summary panel.</summary>
    public event Action? SummaryPanelRequested;

    // ── Writing presets ─────────────────────────────────────────────────────

    public static readonly (string Key, string Label, string Prompt)[] WritingPresets =
    {
        ("email", "Email about this document", "Draft a short, friendly email that explains what this document is and the main things the reader needs to know or do. Include a subject line."),
        ("notes", "Study notes", "Turn this document into clear study notes: headings, short bullet points, key terms in bold with one-line definitions. Cite pages as (p. N)."),
        ("flashcards", "Flashcards", "Make 12 flashcards from this document as a table with columns Question and Answer. Keep answers short. Cite pages as (p. N)."),
        ("quiz", "Quiz", "Write a 10-question multiple-choice quiz on this document, with four options each. Put the answers with page references (p. N) at the end."),
        ("faq", "FAQ", "Write an FAQ for this document: the 8 questions a reader is most likely to ask, each with a short answer and a page reference (p. N)."),
        ("actions", "Action items and deadlines", "List every action, obligation and deadline in this document as a table: What, Who, When, Page."),
        ("plain", "Plain-English version", "Rewrite the whole document in plain English for a non-expert, keeping every important point. Use short sections and cite pages (p. N)."),
        ("post", "Social post", "Write a short LinkedIn post (under 120 words) sharing the key insight from this document."),
    };

    public async Task RunWritingPresetAsync(string key)
    {
        var preset = WritingPresets.FirstOrDefault(p => p.Key == key);
        if (preset.Key == null) return;
        if (string.IsNullOrEmpty(_documentText)) { ToastService.Instance.Info("Open a PDF with text first."); return; }
        await SendChatMessageAsync(preset.Prompt, null);
    }

    // ── Asking by voice ─────────────────────────────────────────────────────

    private bool _isListening;
    public bool IsListening { get => _isListening; private set { _isListening = value; OnPropertyChanged(); } }

    /// <summary>Listens for one spoken question, then sends it.</summary>
    public async Task AskByVoiceAsync()
    {
        if (IsListening) { VoiceInputService.Cancel(); return; }
        IsListening = true;
        StatusText = "Listening… ask your question.";
        try
        {
            string? heard = await VoiceInputService.ListenOnceAsync();
            if (string.IsNullOrWhiteSpace(heard)) { StatusText = "Didn't catch that."; return; }
            StatusText = $"Heard: “{heard}”";
            await SendChatMessageAsync(heard!, null);
        }
        catch (Exception ex)
        {
            StatusText = "Voice input isn't available.";
            Dialogs.AppDialog.ShowInfo(ex.Message, "Ask by voice");
        }
        finally { IsListening = false; }
    }
}
