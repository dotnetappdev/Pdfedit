using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// The AI Assistant tab's Outline, Write, Ask by Voice, Ask Across PDFs, Mind Map, Design Form
/// with AI and Fill Fields — the Windows app's prompts, sent with the provider chosen here.
/// </summary>
public partial class Editor
{
    private bool NeedAiKey()
    {
        if (AiReady) return false;
        _dialog = DialogKind.AiSettings;
        Toast($"Add a {AiProvider} API key first.", "error");
        return true;
    }

    // ── Outline and Write ────────────────────────────────────────────────────

    private async Task AiOutlineAsync()
    {
        if (Doc == null || NeedAiKey()) return;
        await SendAiAsync(
            "Write a generative summary of this document as an outline:\n" +
            "- Start with a two-sentence overview in bold.\n" +
            "- Then one '##' heading per section of the document, each followed by (p. N) for the page it starts on, " +
            "and 2–4 bullet points with the key facts (names, dates, amounts, obligations).\n" +
            "- Finish with a '## Key dates and actions' section if the document has any.\n" +
            "Use only the document. Cite pages as (p. N).");
    }

    public static readonly (string Key, string Label)[] WritePresets =
    [
        ("email", "Email about this document"), ("notes", "Study notes"), ("flashcards", "Flashcards"), ("quiz", "Quiz"),
        ("faq", "FAQ"), ("actions", "Action items and deadlines"), ("plain", "Plain-English version"), ("post", "Social post"),
    ];

    private static string WritePrompt(string key) => key switch
    {
        "email" => "Draft a short, friendly email that explains what this document is and the main things the reader needs to know or do. Include a subject line.",
        "notes" => "Turn this document into clear study notes: headings, short bullet points, key terms in bold with one-line definitions. Cite pages as (p. N).",
        "flashcards" => "Make 12 flashcards from this document as a table with columns Question and Answer. Keep answers short. Cite pages as (p. N).",
        "quiz" => "Write a 10-question multiple-choice quiz on this document, with four options each. Put the answers with page references (p. N) at the end.",
        "faq" => "Write an FAQ for this document: the 8 questions a reader is most likely to ask, each with a short answer and a page reference (p. N).",
        "actions" => "List every action, obligation and deadline in this document as a table: What, Who, When, Page.",
        "plain" => "Rewrite the whole document in plain English for a non-expert, keeping every important point. Use short sections and cite pages (p. N).",
        "post" => "Write a short LinkedIn post (under 120 words) sharing the key insight from this document.",
        _ => "",
    };

    private async Task AiWriteAsync(string key)
    {
        if (Doc == null || NeedAiKey()) return;
        await SendAiAsync(WritePrompt(key));
    }

    // ── Ask by Voice (the browser's speech recognition) ──────────────────────

    public bool Listening { get; private set; }

    private async Task AskByVoiceAsync()
    {
        if (Listening) { await JS.InvokeVoidAsync("pdfedit.voice.stop"); return; }
        if (NeedAiKey()) return;
        bool started = await JS.InvokeAsync<bool>("pdfedit.voice.listen", _self);
        if (!started)
        {
            Toast("Your browser can't listen for speech. Try Chrome or Edge, or type the question in the AI chat.", "error");
            return;
        }
        Listening = true;
        Status("Listening… ask your question");
    }

    [JSInvokable]
    public async Task OnVoiceResult(string? heard, string? error)
    {
        Listening = false;
        if (!string.IsNullOrWhiteSpace(error)) Status(error == "not-allowed" ? "The browser wasn't allowed to use the microphone" : "Didn't catch that");
        else if (string.IsNullOrWhiteSpace(heard)) Status("Didn't catch that");
        else
        {
            Status($"Heard: “{heard}”");
            await InvokeAsync(() => SendAiAsync(heard!));
        }
        await InvokeAsync(StateHasChanged);
    }

    // ── Fill Fields (with what's typed in the chat box) ──────────────────────

    /// <summary>The text in the AI chat box (the side panel keeps it here).</summary>
    public string AiDraft { get; set; } = "";

    private async Task AiFillFromChatAsync()
    {
        if (Doc == null || NeedAiKey()) return;
        var names = FieldNames().ToList();
        if (names.Count == 0) { Toast("This PDF has no form fields to fill.", "error"); return; }
        string instructions = AiDraft.Trim();
        if (instructions.Length == 0)
        {
            ShowRight(RightTab.AI);
            Toast("Type what to fill in the AI chat box first (for example: “my name is Jo Bloggs, today's date, …”), then Fill Fields.");
            return;
        }
        ShowRight(RightTab.AI);
        AiHistory.Add(new AiChatMessage { Role = "user", Content = instructions });
        AiDraft = "";
        var reply = NewReply();
        reply.Content = "Filling the form…";
        AiRunning = true;
        _aiCts = new CancellationTokenSource();
        StateHasChanged();
        try
        {
            var values = await AiProviderService.FillFormFieldsAsync(instructions, names, AiProvider, AiModel, AiKey, _aiCts.Token);
            int n = values.Count(kv => FillField(kv.Key, kv.Value));
            reply.Content = n == 0 ? "I couldn't match anything you said to this form's fields." : $"Filled **{n}** field{(n == 1 ? "" : "s")}.";
        }
        catch (OperationCanceledException) { reply.Content = "(Stopped)"; }
        catch (Exception ex) { reply.Content = "Error: " + ex.Message; }
        finally
        {
            reply.IsStreaming = false;
            AiRunning = false;
            StateHasChanged();
        }
    }

    // ── Ask Across PDFs ──────────────────────────────────────────────────────

    public string AskAcrossAnswer { get; private set; } = "";
    public string? AskAcrossStatus { get; private set; }
    public List<(string File, int Page)> AskAcrossSources { get; private set; } = new();
    public bool AskAcrossIncludeOpen { get; set; } = true;

    public async Task AskAcrossAsync(IReadOnlyList<IBrowserFile> files, string question)
    {
        question = question.Trim();
        if (question.Length == 0) { AskAcrossStatus = "Type your question."; return; }
        if (NeedAiKey()) return;
        var folder = Directory.CreateTempSubdirectory("pdfedit-across-").FullName;
        AiRunning = true;
        _aiCts = new CancellationTokenSource();
        var ct = _aiCts.Token;
        AskAcrossAnswer = "";
        AskAcrossSources = new();
        try
        {
            var pdfs = files.Where(f => f.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
            var paths = await SaveUploadsAsync(pdfs, folder);
            // Name the copies after the uploads, so the answer's citations read naturally.
            var named = new List<string>();
            for (int i = 0; i < paths.Count; i++)
            {
                var dest = Path.Combine(folder, PdfDocumentStore.SafeName(pdfs[i].Name));
                if (named.Contains(dest)) dest = Path.Combine(folder, $"{i + 1} {PdfDocumentStore.SafeName(pdfs[i].Name)}");
                File.Move(paths[i], dest);
                named.Add(dest);
            }
            if (AskAcrossIncludeOpen && Doc != null)
            {
                var open = Path.Combine(folder, PdfDocumentStore.SafeName(Doc.FileName));
                if (!named.Contains(open)) { File.Copy(Doc.CurrentPath, open); named.Insert(0, open); }
            }
            if (named.Count == 0) { AskAcrossStatus = "Choose some PDFs (or keep “the open document” ticked)."; return; }
            AskAcrossStatus = $"Reading {named.Count} PDF{(named.Count == 1 ? "" : "s")}…";
            StateHasChanged();
            var pages = await Task.Run(() => MultiDocService.Read(named, null, ct), ct);
            if (pages.Count == 0) { AskAcrossStatus = "None of these PDFs has text to read (scans need Recognise Text first)."; return; }
            AskAcrossStatus = $"Asking about {pages.Select(p => p.Path).Distinct().Count()} documents, {pages.Count} pages…";
            StateHasChanged();
            string context = await Task.Run(() => MultiDocService.Context(question, pages), ct);
            string provider = AiProvider, model = AiModel, key = AiKey;
            await AiProviderService.SendStreamingAsync([new AiChatMessage { Role = "user", Content = $"Question: {question}\n\nDocuments:\n{context}" }],
                provider, model, key, chunk => InvokeAsync(() => { AskAcrossAnswer += chunk; StateHasChanged(); }), ct, MultiDocService.Instructions);
            AskAcrossSources = MultiDocService.Citations(AskAcrossAnswer).Distinct().ToList();
            AskAcrossStatus = AskAcrossSources.Count > 0 ? "Sources:" : null;
        }
        catch (OperationCanceledException) { AskAcrossStatus = "Stopped."; }
        catch (Exception ex) { AskAcrossStatus = "Couldn't answer: " + ex.Message; }
        finally
        {
            AiRunning = false;
            try { Directory.Delete(folder, true); } catch { }
            StateHasChanged();
        }
    }

    // ── Mind Map ─────────────────────────────────────────────────────────────

    public string? MindMapSvg { get; private set; }

    private async Task MindMapAsync()
    {
        if (Doc == null || NeedAiKey()) return;
        var text = await DocumentTextAsync();
        if (string.IsNullOrWhiteSpace(text)) { Toast("This PDF has no text to read (it may be scanned). Run Recognise Text first.", "error"); return; }
        AiRunning = true;
        _aiCts = new CancellationTokenSource();
        var ct = _aiCts.Token;
        await RunAsync("Making the mind map…", async () =>
        {
            try
            {
                var reply = await AiProviderService.CompleteAsync(MindMap.Prompt(text), AiProvider, AiModel, AiKey, ct, MindMap.SystemPrompt);
                var root = MindMap.Parse(reply, Path.GetFileNameWithoutExtension(Doc.FileName));
                if (root == null) { Toast(MindMap.NoMapMessage, "error"); return; }
                MindMapSvg = MindMap.ToSvg(root, !ThemeIsLight);
                _dialog = DialogKind.MindMap;
                Status("Mind map ready — click a topic to go to its page");
            }
            finally { AiRunning = false; }
        });
    }

    /// <summary>A topic in the mind map was clicked.</summary>
    public async Task MindMapGoAsync(int page)
    {
        _dialog = DialogKind.None;
        await GoToPageAsync(page - 1);
    }

    public async Task SaveMindMapAsync()
    {
        if (MindMapSvg == null) return;
        var svg = MindMapSvg;
        var url = await Store.StageDownloadAsync($"{BaseName} mind map.svg", path => File.WriteAllText(path, svg));
        await JS.InvokeVoidAsync("pdfedit.download", url);
    }

    // ── Design Form with AI ──────────────────────────────────────────────────

    public string DesignAiDescription { get; set; } = "";
    public string DesignAiPaper { get; set; } = "A4";
    public string DesignAiAccent { get; set; } = FormDesignAi.Accents[0].Hex;
    public string? DesignAiStatus { get; private set; }

    public async Task DesignFormWithAiAsync()
    {
        string description = DesignAiDescription.Trim();
        if (description.Length < 4) { DesignAiStatus = "Describe the form you need, e.g. “a car damage report with the driver's details, the damage and a signature”."; return; }
        if (NeedAiKey()) return;
        AiRunning = true;
        _aiCts = new CancellationTokenSource();
        DesignAiStatus = "The AI is designing the form…";
        StateHasChanged();
        try
        {
            var reply = await AiProviderService.CompleteAsync(FormDesignAi.BuildPrompt(description), AiProvider, AiModel, AiKey, _aiCts.Token, FormDesignAi.SystemPrompt);
            var spec = FormDesignAi.Parse(reply);
            if (spec == null || spec.FieldCount == 0) { DesignAiStatus = "The AI's reply wasn't a form. Try again, or describe it differently."; return; }
            var (w, h) = DesignAiPaper == "Letter" ? (612.0, 792.0) : (595.0, 842.0);
            var (items, pageHeight, grew) = FormDesignAi.Layout(spec, w, h, DesignAiAccent);
            DesignCheckpoint();
            Design = new DesignDocument
            {
                PageSize = grew ? "Custom" : DesignAiPaper, CustomWidth = w, CustomHeight = grew ? pageHeight : h,
                BgColor = "#FFFFFFFF", Elements = items,
            };
            _designName = string.IsNullOrWhiteSpace(spec.Title) ? "AI form" : spec.Title.Trim();
            SelectedDesignId = null;
            SelectedDesignIds.Clear();
            SetDesignMode(true);
            _dialog = DialogKind.None;
            DesignAiStatus = null;
            Toast($"Designed “{_designName}” with {spec.FieldCount} field{(spec.FieldCount == 1 ? "" : "s")}." +
                  (grew ? " The page was made taller to fit." : "") + " Change anything, then Export PDF or Open as PDF.", "success");
        }
        catch (OperationCanceledException) { DesignAiStatus = "Stopped."; }
        catch (Exception ex) { DesignAiStatus = "Couldn't design the form: " + ex.Message; }
        finally
        {
            AiRunning = false;
            StateHasChanged();
        }
    }
}
