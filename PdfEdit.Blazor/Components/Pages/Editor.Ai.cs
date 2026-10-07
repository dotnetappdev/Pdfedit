using System.Text;
using Markdig;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// The AI Assistant: chat about the open PDF, the document tasks (summarise, extract, contract
/// review, personal information, translate, smart fill) and the changes the AI proposes, which the
/// user applies one by one. Uses PdfEdit.Core's AiProviderService and AiReplyParser, like the
/// Windows app.
/// </summary>
public partial class Editor
{
    public List<AiChatMessage> AiHistory { get; } = new();
    public bool AiRunning { get; private set; }
    public string AiProvider { get; set; } = "Claude";
    public string AiModel { get; set; } = "";
    private CancellationTokenSource? _aiCts;
    private string? _docText;
    private int _docTextVersion = -1;

    // API keys typed in the AI settings dialog. Kept in memory for this browser session only,
    // never written anywhere; keys in the server's configuration are used when none is typed.
    private readonly Dictionary<string, string> _sessionKeys = new();

    public static readonly (string Type, string Label, string Icon)[] AiPresets =
    [
        ("summarize", "Summarize", "bi-card-text"),
        ("extract", "Extract data", "bi-table"),
        ("contract", "Review contract", "bi-briefcase"),
        ("pii", "Find personal info", "bi-person-badge"),
        ("translate", "Translate", "bi-translate"),
        ("smartfill", "Fill form with AI", "bi-magic"),
    ];

    private void InitAi()
    {
        var ai = Config.GetSection("PdfEdit:Ai");
        // A local AI server (Ollama, LM Studio …) reachable from the web server.
        if (ai["LocalEndpoint"] is { Length: > 0 } ep) AppSettings.Current.LocalAiEndpoint = ep;
        if (ai["LocalModel"] is { Length: > 0 } lm) AppSettings.Current.LocalAiModel = lm;
        if (ai["LocalApiKey"] is { Length: > 0 } lk) AppSettings.Current.LocalAiApiKey = lk;

        AiProvider = ai["Provider"] is { Length: > 0 } p && AiProviderService.Providers.ContainsKey(p)
            ? p
            : AiProviderService.Providers.Keys.FirstOrDefault(k => ServerKey(k).Length > 0) ?? "Claude";
        AiModel = ai["Model"] is { Length: > 0 } m ? m : DefaultModel(AiProvider);
    }

    public static string DefaultModel(string provider) =>
        provider == AiProviderService.LocalProvider ? AppSettings.Current.LocalAiModel
        : AiProviderService.GetModels(provider).FirstOrDefault() ?? "";

    public string[] AiModels =>
        AiProvider == AiProviderService.LocalProvider ? [AppSettings.Current.LocalAiModel] : AiProviderService.GetModels(AiProvider);

    private string ServerKey(string provider) => provider switch
    {
        "Claude" => Config["PdfEdit:Ai:ClaudeApiKey"] ?? "",
        "OpenAI" => Config["PdfEdit:Ai:OpenAiApiKey"] ?? "",
        AiProviderService.CopilotProvider => Config["PdfEdit:Ai:GitHubToken"] ?? "",
        AiProviderService.LocalProvider => Config["PdfEdit:Ai:LocalEndpoint"] is { Length: > 0 } ? "local" : "",
        _ => "",
    };

    public string AiKey => _sessionKeys.TryGetValue(AiProvider, out var k) && k.Length > 0 ? k : ServerKey(AiProvider);
    public bool AiReady => AiKey.Length > 0;
    public bool HasServerKey(string provider) => ServerKey(provider).Length > 0;
    public string SessionKey(string provider) => _sessionKeys.GetValueOrDefault(provider, "");
    public void SetSessionKey(string provider, string key) => _sessionKeys[provider] = key.Trim();

    private void ProviderChanged(Microsoft.AspNetCore.Components.ChangeEventArgs e)
    {
        AiProvider = e.Value?.ToString() ?? AiProvider;
        AiModel = DefaultModel(AiProvider);
    }

    public string ModelName(string model) => AiProviderService.ModelDisplayNames.TryGetValue(model, out var n) ? n : model;

    // ── Chat ─────────────────────────────────────────────────────────────────

    public async Task SendAiAsync(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || AiRunning) return;
        if (!AiReady) { _dialog = DialogKind.AiSettings; Toast($"Add a {AiProvider} API key first.", "error"); return; }
        ShowRight(RightTab.AI);
        AiHistory.Add(new AiChatMessage { Role = "user", Content = text });
        await StreamReplyAsync(AiHistory.Where(m => !m.IsStreaming).ToList(), await SystemPromptAsync(), maxTokens: null);
    }

    public async Task RunAiPresetAsync(string type)
    {
        if (Doc == null || AiRunning) return;
        if (!AiReady) { _dialog = DialogKind.AiSettings; Toast($"Add a {AiProvider} API key first.", "error"); return; }
        ShowRight(RightTab.AI);
        var docText = await DocumentTextAsync();
        if (string.IsNullOrWhiteSpace(docText) && type != "smartfill")
        {
            Toast("This PDF has no text to read (it may be scanned). OCR isn't in the web version yet.", "error");
            return;
        }
        if (type == "smartfill") { await SmartFillAsync(docText); return; }

        var label = AiPresets.First(p => p.Type == type).Label;
        AiHistory.Add(new AiChatMessage { Role = "user", Content = label });
        var reply = NewReply();
        await RunStreamAsync(reply, (chunk, ct) => AiProviderService.AnalyzeDocumentAsync(
            docText, type, AiProvider, AiModel, AiKey, chunk, ct, FieldNames()));
    }

    private async Task SmartFillAsync(string docText)
    {
        var names = FieldNames().ToList();
        if (names.Count == 0) { Toast("This PDF has no form fields to fill.", "error"); return; }
        AiHistory.Add(new AiChatMessage { Role = "user", Content = "Fill this form with AI" });
        var reply = NewReply();
        reply.Content = "Reading the form…";
        AiRunning = true;
        _aiCts = new CancellationTokenSource();
        StateHasChanged();
        try
        {
            var values = await AiProviderService.FillFormFieldsAsync(
                "Fill in this PDF form using the information in the document and sensible values. Leave a field empty if you don't know it.",
                names, AiProvider, AiModel, AiKey, _aiCts.Token, documentText: docText);
            int n = 0;
            foreach (var (name, value) in values)
                if (FillField(name, value)) n++;
            reply.Content = n == 0
                ? "I couldn't work out any values for this form from the document."
                : $"Filled **{n}** field{(n == 1 ? "" : "s")}. Check them, then **Apply Changes** or **Download**.";
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

    public void StopAi() => _aiCts?.Cancel();

    public void ClearAiChat()
    {
        StopAi();
        AiHistory.Clear();
    }

    private AiChatMessage NewReply()
    {
        var reply = new AiChatMessage { Role = "assistant", Content = "", IsStreaming = true };
        AiHistory.Add(reply);
        return reply;
    }

    private Task StreamReplyAsync(List<AiChatMessage> history, string systemPrompt, int? maxTokens)
    {
        var reply = NewReply();
        return RunStreamAsync(reply, (chunk, ct) =>
            AiProviderService.SendStreamingAsync(history, AiProvider, AiModel, AiKey, chunk, ct, systemPrompt));
    }

    /// <summary>Streams a reply into <paramref name="reply"/>, redrawing about ten times a second.</summary>
    private async Task RunStreamAsync(AiChatMessage reply, Func<Action<string>, CancellationToken, Task> send)
    {
        AiRunning = true;
        _aiCts = new CancellationTokenSource();
        StateHasChanged();
        var last = DateTime.UtcNow;
        try
        {
            await send(chunk =>
            {
                lock (reply)
                {
                    reply.Raw += chunk;
                    reply.Content = AiReplyParser.VisibleWhileStreaming(reply.Raw);
                }
                if ((DateTime.UtcNow - last).TotalMilliseconds > 100)
                {
                    last = DateTime.UtcNow;
                    _ = InvokeAsync(StateHasChanged);
                }
            }, _aiCts.Token);
            FinishReply(reply);
            if (string.IsNullOrEmpty(reply.Content) && reply.Actions.Count == 0)
                reply.Content = "(No response — check the API key and model.)";
        }
        catch (OperationCanceledException)
        {
            FinishReply(reply);
            reply.Content = reply.Content.Length > 0 ? reply.Content + "\n\n*(Stopped)*" : "(Stopped)";
        }
        catch (Exception ex)
        {
            reply.Content = "Error: " + ex.Message;
        }
        finally
        {
            reply.IsStreaming = false;
            AiRunning = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private static void FinishReply(AiChatMessage reply)
    {
        reply.IsStreaming = false;
        if (string.IsNullOrEmpty(reply.Raw)) return;
        var (text, actions, follow) = AiReplyParser.Parse(reply.Raw);
        reply.Content = text;
        reply.Actions.Clear();
        foreach (var a in actions) reply.Actions.Add(a);
        reply.FollowUps.Clear();
        foreach (var f in follow) reply.FollowUps.Add(f);
    }

    private IEnumerable<string> FieldNames() => Doc?.Info.FormFields.Select(f => f.Name).Distinct() ?? [];

    private async Task<string> DocumentTextAsync()
    {
        if (Doc == null) return "";
        if (_docTextVersion != Doc.Version || _docText == null)
        {
            var path = Doc.CurrentPath;
            _docText = await Task.Run(() => PdfTextExtractorService.GetDocumentText(path));
            _docTextVersion = Doc.Version;
        }
        return _docText;
    }

    private async Task<string> SystemPromptAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the assistant built into PdfEdit, a PDF editor (web version). Answer in Markdown (headings, lists, **bold**, tables where useful) and keep answers focused.");
        if (Doc != null)
        {
            sb.AppendLine($"The open PDF is \"{Doc.FileName}\" with {PageCount} page(s); the user is on page {_page + 1}.");
            sb.AppendLine("The document text marks each page with [Page N]. When you refer to something in the document, cite it as (p. N) so the user can click to jump there.");

            var fields = Doc.Info.FormFields.GroupBy(f => f.Name).Select(g => g.First()).Take(150).ToList();
            if (fields.Count > 0)
            {
                sb.AppendLine().AppendLine("Form fields (name | type | page | current value | options):");
                foreach (var f in fields)
                {
                    string opts = f.Options.Count > 0 ? string.Join(" / ", f.Options.Take(12)) : "";
                    sb.AppendLine($"- {f.Name} | {f.FieldType} | {f.PageNumber} | {Values.GetValueOrDefault(f.Name)} | {opts}");
                }
            }
            sb.AppendLine().AppendLine("""
                You can propose changes to the document. Put them at the end of your reply in a fenced block
                tagged actions holding a JSON array; the user reviews each one and clicks Apply. Only propose
                actions when the user asks for a change or one clearly helps. Available actions:
                {"action":"fill_field","field":"<exact field name>","value":"<text; Yes/Off for checkboxes>"}
                {"action":"go_to_page","page":3}
                {"action":"highlight","text":"<exact words from the document>","page":2}   (page optional)
                {"action":"redact","text":"<exact words to black out>","page":2}            (page optional)
                {"action":"add_note","page":1,"text":"<comment>"}
                {"action":"add_stamp","page":1,"stamp":"APPROVED"}   (APPROVED, DRAFT, CONFIDENTIAL, SIGN HERE, PAID, RECEIVED …)
                {"action":"rotate_page","page":2,"degrees":90}
                {"action":"delete_page","page":4}
                {"action":"add_watermark","text":"DRAFT"}
                {"action":"add_bookmark","page":5,"title":"<title>"}
                {"action":"zoom","percent":150}   or {"action":"zoom","mode":"fit_width"}
                {"action":"command","name":"<command>"}
                Commands: ocr, compress, export_word, export_excel, export_text, export_images, export_pdfa, flatten, greyscale, page_numbers, split, save, print.
                Example:
                ```actions
                [{"action":"fill_field","field":"FirstName","value":"Jane"}]
                ```
                """);
            sb.AppendLine("At the very end, add one line: FOLLOW-UPS: <question> | <question> | <question> — short questions the user might ask next.");
            var text = await DocumentTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine().Append("The user has the following PDF document open:\n\n").Append(text);
        }
        return sb.ToString();
    }

    // ── Applying the AI's proposed changes ───────────────────────────────────

    public async Task ApplyAiActionAsync(AiActionItem item)
    {
        if (Doc == null) return;
        try
        {
            string done = "";
            int page = int.TryParse(item.Arg("page"), out var p) ? Math.Clamp(p, 1, PageCount) - 1 : _page;
            switch (item.Action)
            {
                case "fill_field":
                    if (!FillField(item.Arg("field"), item.Arg("value"))) throw new InvalidOperationException($"There's no field called “{item.Arg("field")}”.");
                    break;
                case "go_to_page":
                    await GoToPageAsync(page);
                    break;
                case "highlight":
                case "redact":
                    int n = await MarkTextAsync(item.Arg("text"), int.TryParse(item.Arg("page"), out var only) ? only : 0,
                                                item.Action == "redact" ? ItemKind.Redact : ItemKind.Highlight);
                    if (n == 0) throw new InvalidOperationException("That text wasn't found on the page.");
                    done = $"{n} place{(n == 1 ? "" : "s")}" + (item.Action == "redact" ? " — Apply Redactions to remove them" : "");
                    break;
                case "add_note":
                    var (w, _) = ViewSize(page);
                    _items.Add(new PageItem { Kind = ItemKind.Note, Page = page, Left = w - 40, Top = 30 + 26 * _items.Count(i => i.Page == page && i.Kind == ItemKind.Note), Width = 20, Height = 20, Text = item.Arg("text") });
                    break;
                case "add_stamp":
                    var stampTitle = item.Arg("stamp").Trim().ToUpperInvariant();
                    if (stampTitle.Length == 0) throw new InvalidOperationException("No stamp was named.");
                    var def = StampCatalog.BuiltIn.FirstOrDefault(d => d.Title == stampTitle && !d.Dynamic);
                    var (sw2, sh2) = StampCatalog.SizeFor(stampTitle, null);
                    var (pw2, _) = ViewSize(page);
                    _items.Add(new PageItem
                    {
                        Kind = ItemKind.Stamp, Page = page, Left = Math.Max(0, pw2 - sw2 - 36), Top = 36 + 52 * _items.Count(i => i.Page == page && i.Kind == ItemKind.Stamp),
                        Width = sw2, Height = sh2, Text = stampTitle, Color = def?.Color ?? StampCatalog.ColorFor(stampTitle),
                    });
                    break;
                case "rotate_page":
                    int deg = int.TryParse(item.Arg("degrees"), out var d) ? d : 90;
                    await ChangeAsync("Rotating…", $"Rotated page {page + 1}", (src, dest) =>
                        Store.Forms.SaveFull(src, dest, new Dictionary<string, string>(), new Dictionary<int, int> { [page] = ((deg % 360) + 360) % 360 }, Array.Empty<FreeTextAnnotation>()));
                    break;
                case "delete_page":
                    if (PageCount < 2) throw new InvalidOperationException("The last page can't be deleted.");
                    await ChangeAsync("Deleting the page…", $"Deleted page {page + 1}", (src, dest) => Store.Forms.DeletePages(src, dest, [page]));
                    break;
                case "add_watermark":
                    await WatermarkAsync(new WatermarkOptions { Text = item.Arg("text") is { Length: > 0 } t ? t : "DRAFT" });
                    break;
                case "add_bookmark":
                    var title = item.Arg("title");
                    await ChangeAsync("Adding the bookmark…", "Bookmark added", (src, dest) => Store.Forms.AddBookmark(src, dest, title, page + 1));
                    break;
                case "zoom":
                    if (item.Arg("mode") == "fit_width") await FitWidthAsync();
                    else if (item.Arg("mode") == "fit_page") await FitPageAsync();
                    else if (double.TryParse(item.Arg("percent"), out var pct)) SetZoom(pct / 100);
                    break;
                case "command":
                    await RunAiCommandAsync(item.Arg("name"));
                    break;
                case "design_form":
                    throw new InvalidOperationException("Designing forms is in the Windows app; it isn't in the web version yet.");
                default:
                    throw new InvalidOperationException($"“{item.Action}” isn't available in the web version.");
            }
            item.Message = done;
            item.State = AiActionState.Applied;
        }
        catch (Exception ex)
        {
            item.Message = ex.Message;
            item.State = AiActionState.Failed;
        }
    }

    private Task RunAiCommandAsync(string name) => name switch
    {
        "compress" => CompressAsync(),
        "export_word" => ExportAsync(ExportFormat.Word),
        "export_excel" => ExportAsync(ExportFormat.Excel),
        "export_text" => ExportAsync(ExportFormat.Text),
        "export_images" => ExportAsync(ExportFormat.Images),
        "export_pdfa" => PdfAAsync(),
        "flatten" => SaveFlattenedAsync(),
        "greyscale" => GreyscaleAsync(),
        "page_numbers" => PageNumbersAsync("Page {n} of {total}", "BottomCenter", 9),
        "split" => SplitAsync(),
        "save" => SaveAsync(),
        "print" => PrintAsync(),
        "ocr" => RunOcrAsync("eng", skipPagesWithText: true),
        _ => throw new InvalidOperationException($"“{name}” isn't available in the web version."),
    };

    /// <summary>Sets a field from an AI value ("true"/"Yes" tick a checkbox, a radio takes its option).</summary>
    private bool FillField(string name, string value)
    {
        if (Doc == null) return false;
        var widgets = Doc.Info.FormFields.Where(f => f.Name == name).ToList();
        if (widgets.Count == 0)
        {
            widgets = Doc.Info.FormFields.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (widgets.Count == 0) return false;
            name = widgets[0].Name;
        }
        var f = widgets[0];
        Values[name] = f.FieldType switch
        {
            FieldType.Checkbox => value.Trim().ToLowerInvariant() is "true" or "yes" or "on" or "1" or "x" || value == f.ExportValue ? f.ExportValue : "Off",
            FieldType.RadioButton => widgets.FirstOrDefault(w => string.Equals(w.ExportValue, value, StringComparison.OrdinalIgnoreCase))?.ExportValue ?? value,
            _ => value,
        };
        return true;
    }

    /// <summary>Adds highlight (or redaction) boxes over every place <paramref name="text"/> appears.</summary>
    private async Task<int> MarkTextAsync(string text, int onlyPage, ItemKind kind)
    {
        if (Doc == null || string.IsNullOrWhiteSpace(text)) return 0;
        var path = Doc.CurrentPath;
        var hits = await Task.Run(() => PdfTextExtractorService.FindTextPositions(path, text.Trim(), onlyPage, ignoreCase: true));
        if (hits.Count == 0 && onlyPage > 0)
            hits = await Task.Run(() => PdfTextExtractorService.FindTextPositions(path, text.Trim(), 0, ignoreCase: true));
        foreach (var h in hits)
        {
            var v = ToView(h.PageNumber - 1, h.Left, h.Bottom, h.Width, h.Height);
            _items.Add(new PageItem { Kind = kind, Page = h.PageNumber - 1, Left = v.Left, Top = v.Top, Width = v.Width, Height = v.Height });
        }
        if (hits.Count > 0) await GoToPageAsync(hits[0].PageNumber - 1);
        return hits.Count;
    }

    private static readonly Regex PageCite = new(@"\(p(?:age|\.)?\s?(\d{1,4})\)", RegexOptions.IgnoreCase);
    private static readonly Markdig.MarkdownPipeline Md = new Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    /// <summary>The reply as HTML (raw HTML in it is escaped), with (p. N) turned into links to the page.</summary>
    public string ReplyHtml(string markdown)
    {
        var html = Markdig.Markdown.ToHtml(markdown ?? "", Md);
        return PageCite.Replace(html, m =>
        {
            int n = int.Parse(m.Groups[1].Value);
            if (n < 1 || n > PageCount) return m.Value;
            return $"<a href=\"#page-{n - 1}\" onclick=\"pdfedit.scrollToPage({n - 1}, true); return false;\">{m.Value}</a>";
        });
    }
}
