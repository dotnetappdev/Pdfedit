using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>
/// AI chat extras: the instructions that let the assistant cite pages, suggest follow-up
/// questions and propose changes (fill a field, highlight text, rotate a page …), and the code
/// that applies or undoes each proposed change when the user clicks Apply / Undo.
/// The changes travel as a small JSON block in the reply, so they work the same with Claude,
/// OpenAI and local models.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Instructions added to every chat so replies can carry page links, actions and follow-ups.</summary>
    private string AiAssistantInstructions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the assistant built into PdfEdit, a PDF editor. Answer in Markdown (headings, lists, **bold**, tables where useful) and keep answers focused.");
        if (HasDocument)
        {
            sb.AppendLine($"The open PDF is \"{Path.GetFileName(_currentFilePath)}\" with {PageCount} page(s); the user is on page {_currentPageIndex + 1}.");
            sb.AppendLine("The document text marks each page with [Page N]. When you refer to something in the document, cite it as (p. N) so the user can click to jump there.");
        }

        var fields = AllFields.GroupBy(f => f.Name).Select(g => g.First()).Take(150).ToList();
        if (fields.Count > 0)
        {
            sb.AppendLine().AppendLine("Form fields (name | type | page | current value | options):");
            foreach (var f in fields)
            {
                string val = FieldValues.TryGetValue(f.Name, out var v) ? v : f.Value;
                string opts = f.Options.Count > 0 ? string.Join(" / ", f.Options.Take(12)) : "";
                sb.AppendLine($"- {f.Name} | {f.FieldType} | {f.PageNumber} | {val} | {opts}");
            }
        }

        if (HasDocument)
        {
            sb.AppendLine().AppendLine("""
                You can propose changes to the document. Put them at the end of your reply in a fenced block
                tagged actions holding a JSON array; the user reviews each one and clicks Apply. Only propose
                actions when the user asks for a change or one clearly helps. Available actions:
                {"action":"fill_field","field":"<exact field name>","value":"<text; Yes/Off for checkboxes>"}
                {"action":"go_to_page","page":3}
                {"action":"highlight","text":"<exact words from the document>","page":2}   (page optional)
                {"action":"redact","text":"<exact words to black out>","page":2}            (page optional)
                {"action":"add_note","page":1,"text":"<comment>"}
                {"action":"add_stamp","page":1,"stamp":"APPROVED"}   (e.g. APPROVED, DRAFT, CONFIDENTIAL, FINAL, VOID, SIGN HERE)
                {"action":"rotate_page","page":2,"degrees":90}
                {"action":"delete_page","page":4}
                {"action":"add_watermark","text":"DRAFT"}
                {"action":"add_bookmark","page":5,"title":"<title>"}
                Example:
                ```actions
                [{"action":"fill_field","field":"FirstName","value":"Jane"}]
                ```
                """);
        }

        if (HasDocument)
        {
            sb.AppendLine("""
                Two more actions control the app itself (use them when the user asks PdfEdit to do something, e.g. "compress this", "export to Word", "read it to me"):
                {"action":"zoom","percent":150}   or {"action":"zoom","mode":"fit_width"} / "fit_page"
                {"action":"command","name":"<command>"}
                """);
            sb.AppendLine("Commands: " + string.Join(", ", AiCommands.Keys) + ".");
        }

        sb.AppendLine().AppendLine("""
            When the user asks you to create or design a new form (e.g. "make me a job application form",
            "I need a car damage report"), don't describe it in text: put this at the end of your reply in a
            fenced block tagged actions holding a JSON array, and say in one sentence what the form will cover.
            {"action":"design_form","description":"<what the form is for, who fills it in, and every field or section the user asked for>"}
            PdfEdit then designs the form with fillable fields on its Design canvas.
            """);

        sb.AppendLine("Finish every reply with one line of two or three short follow-up questions the user might ask next, in this exact form:");
        sb.AppendLine("FOLLOW-UPS: question one | question two | question three");
        return sb.ToString();
    }

    /// <summary>Streaming callback: keep the raw reply, show it without the action block or follow-up line.</summary>
    private static void AppendReplyChunk(AiChatMessage reply, string chunk)
    {
        reply.Raw += chunk;
        reply.Content = AiReplyParser.VisibleWhileStreaming(reply.Raw);
    }

    /// <summary>Once the reply is complete: split out the actions and follow-ups.</summary>
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

    /// <summary>Asks a suggested follow-up question.</summary>
    public void AskFollowUp(string question)
    {
        if (_isAiRunning || string.IsNullOrWhiteSpace(question)) return;
        AiChatInput = question;
        if (SendAiChatCommand.CanExecute(null)) SendAiChatCommand.Execute(null);
    }

    /// <summary>Jumps to a page cited in a reply (1-based).</summary>
    public void GoToCitedPage(int page)
    {
        if (!HasDocument || page < 1 || page > PageCount) return;
        CurrentPageIndex = page - 1;
    }

    /// <summary>Adds an AI reply to the current page as a sticky note.</summary>
    public void AddReplyAsNote(AiChatMessage msg)
    {
        if (!HasDocument || string.IsNullOrWhiteSpace(msg.Content)) return;
        var size = _document!.PageSizes[Math.Clamp(_currentPageIndex, 0, _document.PageSizes.Count - 1)];
        AddStickyNote(new StickyNoteAnnotation
        {
            Left = 24, Bottom = size.Height - 48,
            Text = PlainText(msg.Content),
            Author = "AI Assistant",
        });
        PageChanged?.Invoke();
        ToastService.Instance.Success($"Added as a note on page {_currentPageIndex + 1}.");
    }

    /// <summary>Markdown reduced to readable plain text (for notes and copying).</summary>
    public static string PlainText(string markdown)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(markdown, @"\*\*(.+?)\*\*|__(.+?)__", "$1$2");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?m)^#{1,6}\s*", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"`{1,3}", "");
        return s.Trim();
    }

    // ── Applying actions ────────────────────────────────────────────────────

    public async Task ApplyAiActionAsync(AiActionItem item)
    {
        if (!item.IsPending) return;
        if (item.Action == "design_form")
        {
            // Works without an open PDF: the form is built on the Design canvas.
            if (DesignFormWithAi(item.Arg("description"))) Applied(item, "Designed on the Design canvas", () => DesignCanvas.Undo());
            return;
        }
        if (!HasDocument || _currentFilePath == null) { Fail("No PDF is open."); return; }
        try
        {
            switch (item.Action)
            {
                case "fill_field": FillField(item); break;
                case "go_to_page": GoToPage(item); break;
                case "highlight": await HighlightAsync(item); break;
                case "redact": await RedactAsync(item); break;
                case "add_note": AddNote(item); break;
                case "add_stamp": AddStamp(item); break;
                case "rotate_page": RotateFromAi(item); break;
                case "delete_page":
                {
                    int p = Page(item);
                    if (PageCount <= 1) { Fail("A PDF needs at least one page."); return; }
                    if (!Dialogs.AppDialog.ShowConfirm($"Delete page {p} of {PageCount}?", "Delete page", "Delete", isDanger: true)) return;
                    await FileAction(item, (i, o) => _formService.DeletePages(i, o, new[] { p - 1 }), $"Page {p} deleted");
                    break;
                }
                case "add_watermark":
                {
                    var opt = new WatermarkOptions { Text = Arg(item, "text", "DRAFT") };
                    int current = _currentPageIndex + 1;
                    await FileAction(item, (i, o) => WatermarkService.Apply(i, o, opt, current), $"'{opt.Text}' watermark added");
                    break;
                }
                case "zoom": ZoomFromAi(item); break;
                case "command": RunCommandFromAi(item); break;
                case "add_bookmark":
                {
                    int p = Page(item);
                    string title = Arg(item, "title", $"Page {p}");
                    await FileAction(item, (i, o) => _formService.AddBookmark(i, o, title, p), $"Bookmark '{title}' added");
                    break;
                }
                default: Fail($"PdfEdit doesn't know how to do “{item.Action}”."); return;
            }
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }

        void Fail(string message) { item.Message = message; item.State = AiActionState.Failed; }
    }

    public void UndoAiAction(AiActionItem item)
    {
        if (!item.CanUndo) return;
        try { item.UndoAction!(); item.State = AiActionState.Undone; }
        catch (Exception ex) { item.Message = ex.Message; item.State = AiActionState.Failed; }
    }

    /// <summary>Applies every pending action in a reply.</summary>
    public async Task ApplyAllAiActionsAsync(AiChatMessage msg)
    {
        foreach (var a in msg.Actions.Where(a => a.IsPending && a.Action != "delete_page").ToList())
            await ApplyAiActionAsync(a);
    }

    private static string Arg(AiActionItem item, string key, string fallback = "")
    {
        var v = item.Arg(key).Trim();
        return v.Length > 0 ? v : fallback;
    }

    private int Page(AiActionItem item, bool required = true)
    {
        if (int.TryParse(item.Arg("page"), out int p) && p >= 1 && p <= PageCount) return p;
        if (!required) return 0;
        return _currentPageIndex + 1;
    }

    private static void Applied(AiActionItem item, string message, Action? undo)
    {
        item.UndoAction = undo;
        item.Message = message;
        item.State = AiActionState.Applied;
    }

    private void FillField(AiActionItem item)
    {
        string want = item.Arg("field");
        string? name = FieldValues.Keys.FirstOrDefault(k => k == want)
                    ?? FieldValues.Keys.FirstOrDefault(k => string.Equals(k, want, StringComparison.OrdinalIgnoreCase))
                    ?? AllFields.Select(f => f.Name).FirstOrDefault(k => string.Equals(k, want, StringComparison.OrdinalIgnoreCase));
        if (name == null) throw new InvalidOperationException($"There's no field called “{want}”.");

        string value = item.Arg("value");
        var field = AllFields.First(f => f.Name == name);
        if (field.FieldType == FieldType.Checkbox)
        {
            bool on = value.Trim().ToLowerInvariant() is "yes" or "true" or "on" or "1" or "x" or "checked" || value == field.ExportValue;
            value = on ? field.ExportValue : "Off";
        }
        string old = FieldValues.TryGetValue(name, out var o) ? o : field.Value;
        SetField(name, value);
        Applied(item, "", () => SetField(name, old));

        void SetField(string n, string v)
        {
            UpdateFieldValue(n, v);
            FieldValueChangedExternally?.Invoke(n, v);
            PageChanged?.Invoke();
        }
    }

    private void GoToPage(AiActionItem item)
    {
        int before = _currentPageIndex;
        int p = Page(item);
        CurrentPageIndex = p - 1;
        Applied(item, "", () => CurrentPageIndex = before);
    }

    private async Task<List<TextMatch>> FindAsync(AiActionItem item)
    {
        string text = item.Arg("text").Trim();
        if (text.Length == 0) throw new InvalidOperationException("No text was given.");
        string path = _currentFilePath!;
        int page = Page(item, required: false);
        var matches = await Task.Run(() => PdfTextExtractorService.FindTextPositions(path, text, page));
        if (matches.Count == 0 && page > 0)   // the model may have got the page wrong
            matches = await Task.Run(() => PdfTextExtractorService.FindTextPositions(path, text));
        if (matches.Count == 0) throw new InvalidOperationException($"Couldn't find “{text}” in the document.");
        return matches;
    }

    private async Task HighlightAsync(AiActionItem item)
    {
        var matches = await FindAsync(item);
        var added = matches.Select(m => new HighlightAnnotation
        {
            PageNumber = m.PageNumber, Left = m.Left, Bottom = m.Bottom, Width = m.Width, Height = Math.Max(m.Height, 6),
            Color = CurrentHighlightColor, Opacity = CurrentHighlightOpacity, Kind = HighlightKind.Highlight,
        }).ToList();
        foreach (var h in added) HighlightAnnotations.Add(h);
        _undoService.Push(new AnnotationAction
        {
            Description = $"AI highlight ({added.Count})",
            Execute = () => { foreach (var h in added) HighlightAnnotations.Add(h); },
            Undo = () => { foreach (var h in added) HighlightAnnotations.Remove(h); },
        });
        RefreshUndoCanExecute();
        CurrentPageIndex = added[0].PageNumber - 1;
        PageChanged?.Invoke();
        Applied(item, $"{added.Count} found", () => { foreach (var h in added) HighlightAnnotations.Remove(h); PageChanged?.Invoke(); });
    }

    private async Task RedactAsync(AiActionItem item)
    {
        var matches = await FindAsync(item);
        var added = matches.Select(m => new RedactRegion
        {
            PageNumber = m.PageNumber, Left = m.Left - 1, Bottom = m.Bottom - 1, Width = m.Width + 2, Height = Math.Max(m.Height, 6) + 2,
        }).ToList();
        foreach (var r in added) RedactionRegions.Add(r);
        OnPropertyChanged(nameof(ApplyRedactionsCommand));
        CurrentPageIndex = added[0].PageNumber - 1;
        PageChanged?.Invoke();
        Applied(item, $"{added.Count} marked — use Apply Redactions to remove the text", () =>
        {
            foreach (var r in added) RedactionRegions.Remove(r);
            OnPropertyChanged(nameof(ApplyRedactionsCommand));
            PageChanged?.Invoke();
        });
    }

    private void AddNote(AiActionItem item)
    {
        int p = Page(item);
        var size = _document!.PageSizes[p - 1];
        int existing = StickyNotes.Count(n => n.PageNumber == p);
        var note = new StickyNoteAnnotation
        {
            Left = 24 + existing * 28, Bottom = size.Height - 48 - existing * 28,
            Text = Arg(item, "text", "Note"), Author = "AI Assistant",
        };
        CurrentPageIndex = p - 1;
        AddStickyNote(note);   // puts it on the current page and makes it undoable
        PageChanged?.Invoke();
        Applied(item, "", () => { StickyNotes.Remove(note); PageChanged?.Invoke(); });
    }

    private void AddStamp(AiActionItem item)
    {
        int p = Page(item);
        string title = Arg(item, "stamp", "APPROVED").ToUpperInvariant();
        var def = StampCatalog.BuiltIn.FirstOrDefault(d => d.Title == title && !d.Dynamic)
                  ?? new StampDefinition(StampCatalog.CustomCategory, title, StampCatalog.ColorFor(title));
        var size = _document!.PageSizes[p - 1];
        double h = 34, w = Math.Clamp(def.Title.Length * 12.5 + 34, 90, 330);
        var ann = new FreeTextAnnotation
        {
            PageNumber = p,
            Left = size.Width - w - 36, Bottom = size.Height - h - 36,
            Width = w, Height = h,
            Text = def.Title, IsStamp = true,
            FontSize = 18, FontFamily = "Arial", IsBold = true, FontColor = def.Color,
            TextAlignment = TextAlign.Center,
        };
        FreeTextAnnotations.Add(ann);
        PushUndo(
            undo: () => { FreeTextAnnotations.Remove(ann); PageChanged?.Invoke(); },
            redo: () => { FreeTextAnnotations.Add(ann); PageChanged?.Invoke(); });
        CurrentPageIndex = p - 1;
        PageChanged?.Invoke();
        Applied(item, "", () => { FreeTextAnnotations.Remove(ann); PageChanged?.Invoke(); });
    }

    private void RotateFromAi(AiActionItem item)
    {
        int p = Page(item);
        int deg = int.TryParse(item.Arg("degrees"), out var d) ? d : 90;
        deg = ((int)Math.Round(deg / 90.0) * 90 % 360 + 360) % 360;
        if (deg == 0) throw new InvalidOperationException("The rotation must be 90, 180 or 270 degrees.");
        CurrentPageIndex = p - 1;
        RotatePage(deg);
        Applied(item, "", () => { CurrentPageIndex = p - 1; RotatePage(-deg); });
    }

    /// <summary>An edit written into the PDF file; Undo puts the file back as it was.</summary>
    private async Task FileAction(AiActionItem item, Action<string, string> edit, string description)
    {
        string path = _currentFilePath!;
        byte[] before = await File.ReadAllBytesAsync(path);
        if (!await ModifyCurrentFileAsync(edit, description))
        {
            item.Message = $"{description} failed.";
            item.State = AiActionState.Failed;
            return;
        }
        Applied(item, "", () =>
        {
            if (!string.Equals(_currentFilePath, path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("That PDF is no longer open.");
            _ = ModifyCurrentFileAsync((_, o) => File.WriteAllBytes(o, before), $"Undo: {description}");
        });
    }

    // ── App commands the assistant can run ──────────────────────────────────

    private Dictionary<string, (string Label, Func<ICommand> Command)>? _aiCommands;

    /// <summary>Commands the assistant may run, by the name it uses.</summary>
    public Dictionary<string, (string Label, Func<ICommand> Command)> AiCommands => _aiCommands ??= new()
    {
        ["save"] = ("Save", () => SaveCommand),
        ["save_as"] = ("Save As", () => SaveAsCommand),
        ["save_to_cloud"] = ("Save to cloud", () => SaveToCloudCommand),
        ["print"] = ("Print", () => PrintCommand),
        ["compress"] = ("Compress the PDF", () => CompressPdfCommand),
        ["ocr"] = ("Recognise text (OCR)", () => OcrMakeSearchableCommand),
        ["export_word"] = ("Export to Word", () => ExportWordCommand),
        ["export_excel"] = ("Export to Excel", () => ExportExcelCommand),
        ["export_text"] = ("Export text", () => ExportTextCommand),
        ["export_images"] = ("Export pages as images", () => ExportPagesAsImagesCommand),
        ["export_pdfa"] = ("Save as PDF/A", () => ExportPdfACommand),
        ["merge"] = ("Merge PDFs into this one", () => MergePdfCommand),
        ["split"] = ("Split the PDF", () => SplitPdfCommand),
        ["insert_pdf"] = ("Insert pages from a PDF", () => InsertPdfCommand),
        ["insert_blank_page"] = ("Insert a blank page", () => InsertBlankPageCommand),
        ["duplicate_page"] = ("Duplicate this page", () => DuplicateCurrentPageCommand),
        ["extract_page"] = ("Extract this page", () => ExtractCurrentPageCommand),
        ["rotate_all"] = ("Rotate all pages right", () => RotateAllPagesCWCommand),
        ["crop"] = ("Crop pages", () => CropPagesCommand),
        ["page_numbers"] = ("Add page numbers", () => AddPageNumbersCommand),
        ["header_footer"] = ("Add a header or footer", () => AddHeaderFooterCommand),
        ["bates"] = ("Bates numbering", () => BatesNumberCommand),
        ["watermark"] = ("Watermark", () => WatermarkCommand),
        ["remove_watermark"] = ("Remove watermark", () => RemoveWatermarkCommand),
        ["password"] = ("Protect with a password", () => PasswordProtectCommand),
        ["sanitize"] = ("Remove hidden information", () => SanitizeCommand),
        ["flatten"] = ("Flatten & save", () => FlattenAndSaveCommand),
        ["apply_redactions"] = ("Apply redactions", () => ApplyRedactionsCommand),
        ["accessibility_check"] = ("Accessibility check", () => AccessibilityCheckCommand),
        ["detect_fields"] = ("Detect form fields", () => DetectFieldsCommand),
        ["clear_fields"] = ("Clear all fields", () => ClearAllFieldsCommand),
        ["check_required"] = ("Check required fields", () => ValidateRequiredFieldsCommand),
        ["sign_certificate"] = ("Sign with a certificate", () => CertSignCommand),
        ["verify_signatures"] = ("Check signatures", () => VerifySignaturesCommand),
        ["compare"] = ("Compare with another PDF", () => VisualCompareCommand),
        ["search_folder"] = ("Search a folder of PDFs", () => SearchFolderCommand),
        ["read_aloud"] = ("Read this page aloud", () => ReadPageAloudCommand),
        ["read_to_end"] = ("Read aloud to the end", () => ReadToEndAloudCommand),
        ["stop_reading"] = ("Stop reading", () => StopReadingCommand),
        ["summary_panel"] = ("Make a summary outline", () => GenerateSummaryCommand),
        ["document_properties"] = ("Document properties", () => DocumentPropertiesCommand),
        ["statistics"] = ("Document statistics", () => DocumentStatisticsCommand),
        ["first_page"] = ("Go to the first page", () => FirstPageCommand),
        ["last_page"] = ("Go to the last page", () => LastPageCommand),
        ["next_page"] = ("Next page", () => NextPageCommand),
        ["previous_page"] = ("Previous page", () => PreviousPageCommand),
        ["thumbnails"] = ("Show or hide thumbnails", () => ToggleThumbnailsCommand),
        ["settings"] = ("Open Settings", () => OpenSettingsCommand),
        ["slide_show"] = ("Slide show", () => SlideShowCommand),
        ["night_mode"] = ("Night mode on/off", () => ToggleNightModeCommand),
        ["two_page_view"] = ("Two-page view on/off", () => ToggleTwoPageViewCommand),
        ["extract_images"] = ("Extract images", () => ExtractImagesCommand),
        ["export_powerpoint"] = ("Export to PowerPoint", () => ExportPowerPointCommand),
        ["translate_pdf"] = ("Translate the whole PDF", () => TranslatePdfCommand),
        ["ask_across_pdfs"] = ("Ask across several PDFs", () => AskAcrossPdfsCommand),
        ["mind_map"] = ("Mind map", () => MindMapCommand),
        ["clean_up_scans"] = ("Clean up scans", () => ScanCleanupCommand),
        ["import_office"] = ("Import a Word / Office file", () => CreatePdfFromOfficeCommand),
        ["import_google_docs"] = ("Import from a Google Docs link", () => ImportGoogleLinkCommand),
    };

    private void ZoomFromAi(AiActionItem item)
    {
        double before = Zoom;
        string mode = item.Arg("mode").ToLowerInvariant();
        if (mode is "fit_width" or "width") ZoomWidthCommand.Execute(null);
        else if (mode is "fit_page" or "page" or "fit") ZoomFitCommand.Execute(null);
        else if (double.TryParse(item.Arg("percent").TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct) && pct > 0)
            Zoom = Math.Clamp(pct / 100.0, 0.1, 8.0);
        else throw new InvalidOperationException("No zoom level was given.");
        Applied(item, $"{Zoom * 100:0}%", () => Zoom = before);
    }

    private void RunCommandFromAi(AiActionItem item)
    {
        string name = item.Arg("name").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        if (!AiCommands.TryGetValue(name, out var entry)) throw new InvalidOperationException($"PdfEdit has no “{name}” command.");
        var cmd = entry.Command();
        if (!cmd.CanExecute(null)) throw new InvalidOperationException($"“{entry.Label}” isn't available right now.");
        cmd.Execute(null);
        Applied(item, "", null);
    }
}
