using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// The ribbon commands the Windows app has that the web version gained when its ribbon was made
/// to match: page and view commands, text formatting, fields, annotations, attachments and more.
/// </summary>
public partial class Editor
{
    // ── Home ─────────────────────────────────────────────────────────────────

    public async Task CloseCurrentAsync()
    {
        if (ActiveTab != null) await CloseTabAsync(ActiveTab);
    }

    /// <summary>The working copy is already unlocked, so this downloads it without a password.</summary>
    public Task RemovePasswordAsync() =>
        DownloadExportAsync("Removing the password…", $"{BaseName} (no password).pdf", (src, dest) => Store.Forms.RemoveEncryption(src, dest));

    public Task CropAsync(float left, float bottom, float right, float top) =>
        ChangeAsync("Cropping…", "Pages cropped", (src, dest) => Store.Forms.CropAllPages(src, dest, left, bottom, -right, -top));

    // ── Fill & Sign: text format ─────────────────────────────────────────────

    private bool _bold, _italic, _underline, _upper;
    private TextAlign _textAlign = TextAlign.Left;

    public static readonly (string Hex, string Name)[] TextColours =
    [
        ("#000000", "Black"), ("#1A237E", "Dark blue"), ("#0050C8", "Blue"), ("#B71C1C", "Red"),
        ("#1B5E20", "Green"), ("#757575", "Grey"), ("#FFFFFF", "White"),
    ];

    /// <summary>The text being edited (the selected text item), which format changes apply to as well.</summary>
    private PageItem? SelectedText => SelectedItem is { Kind: ItemKind.Text or ItemKind.Callout } t ? t : null;

    private void FontSizeChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out int size)) SetFontSize(size);
    }

    private void SetFontSize(int size)
    {
        _fontSize = Math.Clamp(size, 4, 144);
        if (SelectedText is { } t)
        {
            t.FontSize = _fontSize;
            t.Height = Math.Max(t.Height, _fontSize * 1.45);
        }
    }

    private static readonly int[] FontSteps = [6, 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36, 48, 72];

    private void StepFontSize(int by)
    {
        int i = Array.FindIndex(FontSteps, s => s >= _fontSize);
        if (i < 0) i = FontSteps.Length - 1;
        SetFontSize(FontSteps[Math.Clamp(i + by, 0, FontSteps.Length - 1)]);
    }

    private void ToggleBold() { _bold = !_bold; if (SelectedText is { } t) t.Bold = _bold; }
    private void ToggleItalic() { _italic = !_italic; if (SelectedText is { } t) t.Italic = _italic; }
    private void ToggleUnderline() { _underline = !_underline; if (SelectedText is { } t) t.Underline = _underline; }
    private void ToggleUpper() { _upper = !_upper; if (SelectedText is { } t) t.Upper = _upper; }
    private void SetTextAlign(TextAlign a) { _textAlign = a; if (SelectedText is { } t) t.Align = a; }

    public void SetTextColour(string hex)
    {
        _textColor = hex;
        if (SelectedText is { } t) t.Color = hex;
    }

    /// <summary>Fill Field / Checkbox: the Select tool fills fields and ticks boxes.</summary>
    private void FillFieldTool()
    {
        if (PrepareMode) TogglePrepareMode();
        SetTool(Tool.Select);
        Status("Click a form field to type in it, or a check box to tick it");
    }

    // ── Fields ───────────────────────────────────────────────────────────────

    public bool HighlightFields { get; set; } = true;

    public void ClearAllFields()
    {
        if (Doc == null) return;
        foreach (var f in Doc.Info.FormFields)
            Values[f.Name] = f.FieldType == FieldType.Checkbox || f.FieldType == FieldType.RadioButton ? "Off" : "";
        Status("Cleared every field — Apply Changes or Download keeps it");
    }

    /// <summary>Delete Field from Fill &amp; Sign or Forms: the field is marked in Edit Fields first.</summary>
    public void DeleteFieldAnyMode()
    {
        if (!PrepareMode) PrepareMode = true;
        DeleteSelectedField();
    }

    // ── Selected page item, Undo / Redo Ann. ─────────────────────────────────

    public string? SelectedItemId { get; private set; }
    public PageItem? SelectedItem => _items.FirstOrDefault(i => i.Id == SelectedItemId);
    private readonly Stack<PageItem> _itemRedo = new();

    public void SelectItem(PageItem item)
    {
        SelectedItemId = item.Id;
        if (item.Kind is ItemKind.Text or ItemKind.Callout)
        {
            // The format controls show the selected text's format.
            _fontSize = (int)Math.Round(item.FontSize);
            _bold = item.Bold; _italic = item.Italic; _underline = item.Underline; _upper = item.Upper; _textAlign = item.Align;
            _textColor = item.Color;
        }
    }

    public void DeleteSelectedItem()
    {
        if (SelectedItem is not { } item) return;
        if (item.Locked) { Status("It's locked — unlock it (its toolbar or Properties) to delete it"); return; }
        _items.Remove(item);
        _itemRedo.Push(item);
        SelectedItemId = null;
        Status($"Deleted {item.Describe().ToLowerInvariant()}");
    }

    private void UndoAnnotation()
    {
        // The last thing added (not one already in the PDF).
        var last = _items.LastOrDefault(i => i.Baseline == null);
        if (last == null) return;
        _items.Remove(last);
        _itemRedo.Push(last);
        Status($"Took off {last.Describe().ToLowerInvariant()}");
    }

    private void RedoAnnotation()
    {
        if (_itemRedo.Count == 0) return;
        var item = _itemRedo.Pop();
        _items.Add(item);
        Status($"Put back {item.Describe().ToLowerInvariant()}");
    }

    // ── Arrange Fields (Edit Fields, Ctrl+click for more than one) ───────────

    public List<(string Name, int Widget)> SelectedWidgets { get; } = new();

    /// <summary>Ctrl+click adds a field to the selection (or takes it out); a plain click selects just it.</summary>
    public void SelectWidgetMulti(FormFieldInfo f, bool add)
    {
        var key = (f.Name, f.WidgetIndex);
        if (!add) SelectedWidgets.Clear();
        if (add && SelectedWidgets.Contains(key)) { SelectedWidgets.Remove(key); return; }
        SelectedWidgets.Remove(key);
        SelectedWidgets.Add(key);   // the last one clicked is the reference
        SelectWidget(f);
    }

    public bool IsWidgetSelected(FormFieldInfo f) => SelectedWidgets.Contains((f.Name, f.WidgetIndex));

    private void ArrangeFields(ArrangeOp op)
    {
        if (Doc == null) return;
        var fields = SelectedWidgets
            .Select(w => Doc.Info.FormFields.FirstOrDefault(f => f.Name == w.Name && f.WidgetIndex == w.Widget))
            .OfType<FormFieldInfo>().ToList();
        if (Arrange.Needs(op, fields.Count) is { } why) { Toast(why); return; }
        int page = fields[^1].PageNumber - 1;
        fields = fields.Where(f => f.PageNumber - 1 == page).ToList();
        var boxes = fields.Select(f =>
        {
            var r = FieldRect(f);
            var v = ToView(page, r.Left, r.Bottom, r.Width, r.Height);
            return new Box(v.Left, v.Top, v.Width, v.Height);
        }).ToList();
        var (pw, ph) = ViewSize(page);
        var moved = Arrange.Apply(boxes, boxes.Count - 1, op, pw, ph);
        for (int i = 0; i < fields.Count; i++)
        {
            var u = ToUser(page, moved[i].Left, moved[i].Top, moved[i].Width, moved[i].Height);
            _bounds[(fields[i].Name, fields[i].WidgetIndex)] = new FieldBounds(u.Left, u.Bottom, u.Width, u.Height);
        }
        Status($"{Arrange.Commands.First(c => c.Op == op).Hint} — Apply Changes writes it into the PDF");
    }

    // ── View ─────────────────────────────────────────────────────────────────

    private void ZoomBoxChanged(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct)) SetZoom(pct / 100);
    }

    private Task ResetRotationAsync()
    {
        int page = _page, r = Rotation(page);
        if (r == 0) return Task.CompletedTask;
        return ChangeAsync("Turning the page upright…", $"Page {page + 1} is upright again", (src, dest) =>
            Store.Forms.SaveFull(src, dest, new Dictionary<string, string>(), new Dictionary<int, int> { [page] = (360 - r) % 360 },
                Array.Empty<FreeTextAnnotation>()));
    }

    private Task RotateAllAsync(int degrees)
    {
        var all = Enumerable.Range(0, PageCount).ToDictionary(p => p, _ => (degrees + 360) % 360);
        return ChangeAsync("Rotating every page…", $"Rotated every page {(degrees > 0 ? "clockwise" : "anticlockwise")}", (src, dest) =>
            Store.Forms.SaveFull(src, dest, new Dictionary<string, string>(), all, Array.Empty<FreeTextAnnotation>()));
    }

    public bool TwoPages { get; set; }
    public bool NightMode { get; set; }
    public bool AutoScrolling { get; private set; }

    private async Task ToggleAutoScrollAsync()
    {
        AutoScrolling = !AutoScrolling;
        await JS.InvokeVoidAsync("pdfedit.autoScroll", _viewer, AutoScrolling ? 40 : 0);
        Status(AutoScrolling ? "Auto scroll — click Auto Scroll again (or scroll yourself) to stop" : "Auto scroll stopped");
    }

    [JSInvokable]
    public Task OnAutoScrollStopped()
    {
        AutoScrolling = false;
        return InvokeAsync(StateHasChanged);
    }

    // Slide show: one page at a time, full screen.
    public int? SlidePage { get; private set; }

    private async Task StartSlideShowAsync()
    {
        if (Doc == null) return;
        await CommitPendingAsync();
        SlidePage = _page;
        StateHasChanged();
        await Task.Yield();
        await JS.InvokeVoidAsync("pdfedit.fullscreen", "pe-slideshow");
    }

    public void SlideTo(int page)
    {
        if (page < 0 || page >= PageCount) return;
        SlidePage = page;
    }

    public async Task EndSlideShowAsync()
    {
        if (SlidePage is { } p) _page = p;
        SlidePage = null;
        await JS.InvokeVoidAsync("pdfedit.exitFullscreen");
        await GoToPageAsync(_page, false);
    }

    private async Task SlideKeyAsync(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
    {
        if (SlidePage is not { } p) return;
        switch (e.Key)
        {
            case "ArrowRight" or "ArrowDown" or "PageDown" or " " or "Enter": SlideTo(p + 1); break;
            case "ArrowLeft" or "ArrowUp" or "PageUp" or "Backspace": SlideTo(p - 1); break;
            case "Home": SlideTo(0); break;
            case "End": SlideTo(PageCount - 1); break;
            case "Escape": await EndSlideShowAsync(); break;
        }
    }

    // UI scale (the Windows app's UI Scale): the whole window bigger or smaller, kept for this browser.
    public double UiScale { get; private set; } = 1;

    private async Task ChangeUiScaleAsync(double by)
    {
        UiScale = Math.Clamp(Math.Round(UiScale + by, 1), 0.8, 1.6);
        Settings.UiScale = UiScale;
        SaveSettings();
        await Task.CompletedTask;
    }

    // ── Bookmarks, attachments ───────────────────────────────────────────────

    public Task AddBookmarkAsync(string title, int page)
    {
        title = title.Trim();
        if (title.Length == 0) { Toast("Type a name for the bookmark.", "error"); return Task.CompletedTask; }
        return ChangeAsync("Adding the bookmark…", $"Bookmark “{title}” added", (src, dest) => Store.Forms.AddBookmark(src, dest, title, page));
    }

    public List<PdfAttachmentInfo> Attachments { get; private set; } = new();

    private async Task ShowAttachmentsAsync()
    {
        if (Doc == null) return;
        var path = Doc.CurrentPath;
        Attachments = await Task.Run(() => Store.Forms.GetAttachments(path));
        _dialog = DialogKind.Attachments;
    }

    public async Task AttachFilesAsync(IReadOnlyList<IBrowserFile> files)
    {
        if (Doc == null || files.Count == 0) return;
        var folder = Directory.CreateTempSubdirectory("pdfedit-attach-").FullName;
        try
        {
            var paths = new List<string>();
            await RunAsync("Reading the files…", async () => paths = await SaveUploadsAsync(files, folder));
            if (paths.Count != files.Count) return;
            var names = files.Select(f => f.Name).ToList();
            await ChangeAsync("Attaching…", $"Attached {files.Count} file{(files.Count == 1 ? "" : "s")}", (src, dest) =>
            {
                var step = src;
                for (int i = 0; i < paths.Count; i++)
                {
                    var next = i == paths.Count - 1 ? dest : dest + $".{i}.tmp";
                    Store.Forms.AddAttachment(step, next, paths[i], names[i]);
                    if (step != src) File.Delete(step);
                    step = next;
                }
            });
            Attachments = Store.Forms.GetAttachments(Doc.CurrentPath);
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    public async Task DownloadAttachmentAsync(PdfAttachmentInfo a)
    {
        if (Doc == null) return;
        var doc = Doc;
        await RunAsync("Getting the file…", async () =>
        {
            var bytes = await Task.Run(() => Store.Forms.ExtractAttachment(doc.CurrentPath, a.Name));
            var url = await Store.ExportAsync(doc, PdfDocumentStore.SafeName(a.Name), path => File.WriteAllBytes(path, bytes));
            await JS.InvokeVoidAsync("pdfedit.download", url);
        });
    }

    public async Task RemoveAttachmentAsync(PdfAttachmentInfo a)
    {
        await ChangeAsync("Removing the attachment…", $"Removed {a.Name}", (src, dest) => Store.Forms.RemoveAttachment(src, dest, a.Name));
        if (Doc != null) Attachments = Store.Forms.GetAttachments(Doc.CurrentPath);
    }

    // ── Annotations: XFDF and the summary ────────────────────────────────────

    private Task ExportXfdfAsync()
    {
        string name = Doc?.FileName ?? "document.pdf";
        return DownloadExportAsync("Exporting the comments…", BaseName + ".xfdf", (src, dest) =>
        {
            var a = PdfAnnotationReader.ReadModels(src);
            XfdfService.Export(dest, name, a.Highlights, a.Notes, a.Texts, a.Shapes);
        });
    }

    public async Task ImportXfdfAsync(IBrowserFile file)
    {
        if (Doc == null) return;
        var folder = Directory.CreateTempSubdirectory("pdfedit-xfdf-").FullName;
        try
        {
            var path = (await SaveUploadsAsync([file], folder))[0];
            var x = XfdfService.Import(path);
            int n = x.Highlights.Count + x.StickyNotes.Count + x.FreeTexts.Count + x.Shapes.Count;
            if (n == 0) { Toast("There are no comments in that file.", "error"); return; }
            await ChangeAsync("Importing the comments…", $"Imported {n} comment{(n == 1 ? "" : "s")}", (src, dest) =>
                Store.Forms.SaveFull(src, dest, new Dictionary<string, string>(), new Dictionary<int, int>(), x.FreeTexts,
                    highlightAnnotations: x.Highlights, stickyNotes: x.StickyNotes, shapeAnnotations: x.Shapes));
            Toast($"Imported {n} comment{(n == 1 ? "" : "s")} from {file.Name}.", "success");
        }
        catch (Exception ex) { Toast("Couldn't read that XFDF file: " + ex.Message, "error"); }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    private Task AnnotationSummaryAsync() =>
        DownloadExportAsync("Making the summary…", BaseName + "_annotations.csv", (src, dest) =>
        {
            static string Csv(string? s) => s == null ? "" : s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            var sb = new StringBuilder("Type,Page,Left,Bottom,Width,Height,Colour,Author,Modified,Text\n");
            foreach (var a in PdfAnnotationReader.Read(src))
                sb.Append(Csv(a.Kind)).Append(',').Append(a.PageNumber).Append(',')
                  .Append(F(a.Left)).Append(',').Append(F(a.Bottom)).Append(',').Append(F(a.Width)).Append(',').Append(F(a.Height)).Append(',')
                  .Append(Csv(a.Colour)).Append(',').Append(Csv(a.Author)).Append(',')
                  .Append(a.Modified?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "").Append(',')
                  .Append(Csv(a.Text)).Append('\n');
            File.WriteAllText(dest, sb.ToString(), new UTF8Encoding(true));
            static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        });

    // ── Find & Highlight, Find & Replace (field values), Import JSON ─────────

    public async Task FindAndHighlightAsync(string text, string colour)
    {
        if (Doc == null || string.IsNullOrWhiteSpace(text)) return;
        var path = Doc.CurrentPath;
        var hits = await Task.Run(() => PdfTextExtractorService.FindTextPositions(path, text.Trim(), ignoreCase: true));
        if (hits.Count == 0) { Toast($"“{text}” wasn't found."); return; }
        foreach (var h in hits)
        {
            int page = h.PageNumber - 1;
            var v = ToView(page, h.Left, h.Bottom, h.Width, h.Height);
            _items.Add(new PageItem
            {
                Kind = ItemKind.Highlight, Page = page, Left = v.Left, Top = v.Top, Width = v.Width, Height = v.Height,
                Color = colour, Opacity = _highlightOpacity,
            });
        }
        _dialog = DialogKind.None;
        Toast($"Highlighted {hits.Count} match{(hits.Count == 1 ? "" : "es")} of “{text}”. Apply Changes or Download keeps them.", "success");
    }

    public int FindReplaceInFields(string find, string replace, bool matchCase, bool wholeField)
    {
        if (Doc == null || find.Length == 0) return 0;
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int n = 0;
        foreach (var f in Doc.Info.FormFields.Where(f => f.FieldType is FieldType.Text or FieldType.ComboBox).GroupBy(f => f.Name).Select(g => g.First()))
        {
            var value = Values.GetValueOrDefault(f.Name, "");
            string next = wholeField
                ? (string.Equals(value, find, cmp) ? replace : value)
                : value.Replace(find, replace, cmp);
            if (next != value) { Values[f.Name] = next; n++; }
        }
        Status(n == 0 ? $"“{find}” isn't in any field" : $"Replaced in {n} field{(n == 1 ? "" : "s")} — Apply Changes or Download keeps it");
        return n;
    }

    /// <summary>{"field name": value} pairs, as the Windows app's Import from JSON.</summary>
    public int ImportJson(string json)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        if (dict == null || Doc == null) return 0;
        int filled = 0;
        foreach (var (key, value) in dict)
        {
            var field = Doc.Info.FormFields.FirstOrDefault(f =>
                string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.Name.Replace(" ", "_"), key.Replace(" ", "_"), StringComparison.OrdinalIgnoreCase));
            if (field == null) continue;
            Values[field.Name] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.True => field.FieldType == FieldType.Checkbox ? field.ExportValue : "true",
                JsonValueKind.False => field.FieldType == FieldType.Checkbox ? "Off" : "false",
                _ => value.GetRawText(),
            };
            filled++;
        }
        return filled;
    }

    // ── Links ────────────────────────────────────────────────────────────────

    /// <summary>The rectangle dragged with the Link tool, waiting for its target (dialog).</summary>
    public (int Page, double Left, double Top, double Width, double Height)? PendingLink { get; private set; }

    private void StartLink(int page, double left, double top, double width, double height)
    {
        if (width < 6 || height < 6) { width = 120; height = 16; left -= 60; top -= 8; }
        PendingLink = (page, left, top, width, height);
        _dialog = DialogKind.Link;
    }

    /// <summary>Kind: url, email, phone or page.</summary>
    public async Task AddLinkAsync(string kind, string target, bool visibleBorder)
    {
        if (PendingLink is not { } l || Doc == null) return;
        if (MakeTarget(kind, target) is not { } link) return;
        PendingLink = null;
        _dialog = DialogKind.None;
        var r = ToUser(l.Page, l.Left, l.Top, l.Width, l.Height);
        int pageNo = l.Page + 1;
        await ChangeAsync("Adding the link…", $"Link added: {link.Describe()}", (src, dest) =>
            LinkService.AddLink(src, dest, pageNo, r.Left, r.Bottom, r.Width, r.Height, link, visibleBorder));
    }

    /// <summary>The link's target from the dialog (kind: url, email, phone or page), or null with a message.</summary>
    private LinkTarget? MakeTarget(string kind, string target)
    {
        target = target.Trim();
        LinkTarget link;
        switch (kind)
        {
            case "page" when int.TryParse(target, out int p) && p >= 1 && p <= PageCount: link = LinkTarget.ToPage(p); break;
            case "page": Toast($"Enter a page from 1 to {PageCount}.", "error"); return null;
            case "email" when target.Contains('@'): link = LinkTarget.ToUri("mailto:" + target.Replace("mailto:", "")); break;
            case "phone" when target.Length > 2: link = LinkTarget.ToUri("tel:" + new string(target.Where(c => char.IsDigit(c) || c == '+').ToArray())); break;
            case "url" when target.Length > 3:
                link = LinkTarget.ToUri(target.Contains("://") ? target : "https://" + target);
                if (!Uri.TryCreate(link.Uri, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) { Toast("That isn't a web address.", "error"); return null; }
                break;
            default: Toast("Enter where the link goes.", "error"); return null;
        }
        return link;
    }

    public void CancelLink()
    {
        PendingLink = null;
        EditingLink = null;
        _dialog = DialogKind.None;
    }

    // ── Links already in the PDF ─────────────────────────────────────────────

    /// <summary>The link being changed in the Link dialog (null when adding one).</summary>
    public PdfLinkInfo? EditingLink { get; private set; }

    public IEnumerable<PdfLinkInfo> LinksOn(int page) => Doc?.Links.Where(l => l.PageNumber == page + 1) ?? [];

    /// <summary>Only web, email and phone links open from the page; anything else (javascript:, file:) never does.</summary>
    public static string? SafeHref(LinkTarget t) =>
        t.Uri is { } u && Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" or "tel" ? uri.AbsoluteUri : null;

    /// <summary>A click on a link: a page link goes there; with the Link tool, any link opens to be edited.</summary>
    private async Task LinkClickAsync(PdfLinkInfo link)
    {
        if (_tool == Tool.Link) { EditLink(link); return; }
        if (link.Target.PageNumber is { } p) await GoToPageAsync(Math.Clamp(p - 1, 0, PageCount - 1));
    }

    public void EditLink(PdfLinkInfo link)
    {
        EditingLink = link;
        PendingLink = null;
        _dialog = DialogKind.Link;
    }

    public async Task UpdateLinkAsync(string kind, string target, bool visibleBorder)
    {
        if (EditingLink is not { } l) return;
        if (MakeTarget(kind, target) is not { } link) return;
        EditingLink = null;
        _dialog = DialogKind.None;
        await ChangeAsync("Changing the link…", $"Link changed: {link.Describe()}", (src, dest) =>
            LinkService.UpdateLink(src, dest, l.PageNumber, l.AnnotIndex, link, visibleBorder));
    }

    public async Task RemoveLinkAsync(PdfLinkInfo l)
    {
        EditingLink = null;
        _dialog = DialogKind.None;
        await ChangeAsync("Removing the link…", "Link removed — Undo brings it back", (src, dest) =>
            LinkService.RemoveLinks(src, dest, l.PageNumber, l.AnnotIndex));
    }

    private async Task CopyLinkAsync(PdfLinkInfo l)
    {
        string text = l.Target.PageNumber is { } p ? $"Page {p}" : l.Target.Uri ?? "";
        await TryCopyText(text);
        Status("Copied " + text);
    }

    private async Task RemoveLinksAsync()
    {
        if (Doc == null) return;
        int removed = 0;
        await ChangeAsync("Removing links…", "Links removed", (src, dest) => removed = LinkService.RemoveLinks(src, dest, 0));
        Toast(removed == 0 ? "This PDF has no links." : $"Removed {removed} link{(removed == 1 ? "" : "s")}. Undo brings them back.", removed == 0 ? "" : "success");
    }

    // ── Accessibility ────────────────────────────────────────────────────────

    public List<AccessibilityCheck> AccessibilityResults { get; private set; } = new();

    private async Task AccessibilityCheckAsync()
    {
        if (Doc == null) return;
        await RunAsync("Checking accessibility…", async () =>
        {
            await CommitPendingAsync();
            var path = Doc.CurrentPath;
            AccessibilityResults = await Task.Run(() => AccessibilityService.Check(path));
            _dialog = DialogKind.Accessibility;
            Status("Accessibility check done");
        });
    }

    public async Task FixAccessibilityAsync(AccessibilityFixOptions options)
    {
        string summary = "";
        await ChangeAsync("Fixing…", "Accessibility problems fixed", (src, dest) => summary = AccessibilityService.Fix(src, dest, options));
        if (Doc != null) AccessibilityResults = await Task.Run(() => AccessibilityService.Check(Doc.CurrentPath));
        if (!string.IsNullOrWhiteSpace(summary)) Toast(summary, "success");
    }

    // ── Search PDFs (a folder or several files) ──────────────────────────────

    public List<(string File, int Page, string Snippet)> FolderHits { get; private set; } = new();
    public string? FolderSearchStatus { get; private set; }

    public async Task SearchFilesAsync(IReadOnlyList<IBrowserFile> files, string text)
    {
        text = text.Trim();
        if (text.Length == 0) { FolderSearchStatus = "Type what to look for."; return; }
        var pdfs = files.Where(f => f.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pdfs.Count == 0) { FolderSearchStatus = "There are no PDFs in what you chose."; return; }
        var folder = Directory.CreateTempSubdirectory("pdfedit-search-").FullName;
        try
        {
            FolderSearchStatus = $"Reading {pdfs.Count} PDF{(pdfs.Count == 1 ? "" : "s")}…";
            StateHasChanged();
            var paths = await SaveUploadsAsync(pdfs, folder);
            var hits = new List<(string, int, string)>();
            await Task.Run(() =>
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    try
                    {
                        int pages;
                        using (var r = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(paths[i]))) pages = r.GetNumberOfPages();
                        for (int p = 1; p <= pages; p++)
                        {
                            var pageText = PdfTextExtractorService.GetPageText(paths[i], p);
                            int at = pageText.IndexOf(text, StringComparison.OrdinalIgnoreCase);
                            if (at < 0) continue;
                            int start = Math.Max(0, at - 50), end = Math.Min(pageText.Length, at + text.Length + 50);
                            hits.Add((pdfs[i].Name, p, (start > 0 ? "…" : "") + pageText[start..end].Replace('\n', ' ').Trim() + (end < pageText.Length ? "…" : "")));
                        }
                    }
                    catch { /* not a readable PDF: skip it */ }
                }
            });
            FolderHits = hits;
            FolderSearchStatus = hits.Count == 0 ? $"“{text}” isn't in any of the {pdfs.Count} PDFs."
                : $"Found on {hits.Count} page{(hits.Count == 1 ? "" : "s")} in {hits.Select(h => h.Item1).Distinct().Count()} of {pdfs.Count} PDFs.";
        }
        catch (Exception ex) { FolderSearchStatus = "Couldn't search: " + ex.Message; }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    // ── Helpers for the design ribbon ────────────────────────────────────────

    private static double Num(ChangeEventArgs e, double fallback) =>
        double.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    /// <summary>A colour input's #rrggbb as a design file's #FFRRGGBB.</summary>
    private static string Argb(ChangeEventArgs e) => "#FF" + (e.Value?.ToString() ?? "#000000").TrimStart('#').ToUpperInvariant();
}
