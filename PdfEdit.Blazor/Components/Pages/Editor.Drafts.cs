using System.Text.Json;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Models;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// Drafts: the open documents — with everything placed on them, typed into fields or moved but not
/// applied yet — and the Design canvas are kept in this browser every couple of seconds (and on
/// Save Draft), so reloading the page, a dropped connection or closing the window loses nothing.
/// A reload brings this window's drafts straight back; the start page lists the rest.
/// </summary>
public partial class Editor
{
    private static readonly TimeSpan DraftEvery = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions DraftJson = new() { IncludeFields = false };

    private Timer? _draftTimer;
    private string _windowId = "";
    private bool _draftsReady, _draftSaving;
    /// <summary>Something may have changed since the drafts were last kept (set after every render).</summary>
    private bool _draftDirty;
    /// <summary>What was last kept for each draft id of this window, so unchanged drafts aren't written again.</summary>
    private readonly Dictionary<string, string> _draftKept = new();

    /// <summary>When the drafts were last kept (shown in the status bar).</summary>
    public DateTime? DraftSavedAt { get; private set; }
    /// <summary>Drafts from other windows (or a closed one), offered on the start page.</summary>
    public List<DraftInfo> OtherDrafts { get; private set; } = new();

    public sealed record DraftInfo(string Id, string Window, string Kind, string Name, string? SessionId, int Order,
        bool Active, bool Changes, bool HasPdf, string When);

    private sealed class DocDraft
    {
        public List<PageItem> Items { get; set; } = new();
        public Dictionary<string, string> Values { get; set; } = new();
        public List<BoundsDraft> Bounds { get; set; } = new();
        public Dictionary<string, FormFieldInfo> Edits { get; set; } = new();
        public List<string> Deleted { get; set; } = new();
        public int Page { get; set; }
        public double Zoom { get; set; } = 1;
        public bool Modified { get; set; }
    }

    private sealed record BoundsDraft(string Name, int Widget, FieldBounds Bounds);

    private sealed class DesignDraft
    {
        public string Name { get; set; } = "";
        public string Json { get; set; } = "";
        public bool Shown { get; set; }
    }

    private string DesignDraftId => "design:" + _windowId;

    // ── Keeping ──────────────────────────────────────────────────────────────

    /// <summary>File → Save Draft: keeps everything now, and says so.</summary>
    public async Task SaveDraftNowAsync()
    {
        if (!_draftsReady) { Toast("Drafts aren't available in this browser (storage is blocked).", "error"); return; }
        bool ok = await SaveDraftsAsync(force: true);
        if (ok) Status("Draft saved in this browser — reloading or closing the page won't lose your work");
        else Toast("Couldn't keep a draft in this browser (storage may be full or blocked).", "error");
    }

    private void StartDraftTimer() =>
        _draftTimer ??= new Timer(_ => _ = InvokeAsync(async () => { await SaveDraftsAsync(); }), null, DraftEvery, DraftEvery);

    /// <summary>Writes the drafts that changed since they were last kept, and forgets closed ones.</summary>
    private async Task<bool> SaveDraftsAsync(bool force = false)
    {
        if (!_draftsReady || _draftSaving) return false;
        if (!force && !_draftDirty) return true;
        _draftSaving = true;
        _draftDirty = false;
        bool ok = true, wrote = false;
        try
        {
            var wanted = new Dictionary<string, (object Info, string State, string? PdfKey)>();
            for (int n = 0; n < Tabs.Count; n++)
            {
                var tab = Tabs[n];
                bool shown = tab == _active;
                var state = JsonSerializer.Serialize(new DocDraft
                {
                    Items = tab.Items, Values = tab.Values,
                    Bounds = tab.Bounds.Select(b => new BoundsDraft(b.Key.Name, b.Key.WidgetIndex, b.Value)).ToList(),
                    Edits = tab.Edits, Deleted = tab.Deleted.ToList(),
                    Page = shown ? _page : tab.Page, Zoom = shown ? _zoom : tab.Zoom, Modified = tab.Session.IsModified,
                }, DraftJson);
                var info = new
                {
                    id = tab.DraftId, window = _windowId, kind = "doc", name = tab.Session.FileName, sessionId = tab.Session.Id,
                    order = n, active = shown && !DesignMode, changes = tab.HasChanges,
                };
                wanted[tab.DraftId] = (info, state, $"{tab.Session.Id}:{tab.Session.Version}");
            }
            if (Design.Elements is { Count: > 0 })
            {
                var state = JsonSerializer.Serialize(new DesignDraft { Name = _designName, Json = Design.ToJson(), Shown = DesignMode }, DraftJson);
                wanted[DesignDraftId] = (new { id = DesignDraftId, window = _windowId, kind = "design", name = _designName, order = Tabs.Count, active = DesignMode, changes = true }, state, null);
            }

            if (_active != null) RememberView(_active);
            foreach (var (id, (info, state, pdfKey)) in wanted)
            {
                // The PDF's version is part of what's compared: an applied change fetches the PDF again.
                var kept = state + "\u0001" + pdfKey + "\u0001" + JsonSerializer.Serialize(info);
                if (!force && _draftKept.TryGetValue(id, out var last) && last == kept) continue;
                if (await JS.InvokeAsync<bool>("pdfeditDrafts.put", info, state, pdfKey)) { _draftKept[id] = kept; wrote = true; }
                else ok = false;
            }
            foreach (var gone in _draftKept.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
            {
                await JS.InvokeVoidAsync("pdfeditDrafts.remove", gone);
                _draftKept.Remove(gone);
            }
            await JS.InvokeVoidAsync("pdfeditDrafts.setUnsaved", !ok && Tabs.Any(t => t.HasChanges));
            if (!ok) _draftDirty = true;   // try again next time
            if (wrote || force) { DraftSavedAt = await BrowserNowAsync(); StateHasChanged(); }
        }
        catch (Exception) { ok = false; _draftDirty = true; }
        finally { _draftSaving = false; }
        return ok;
    }

    // ── Bringing back ────────────────────────────────────────────────────────

    /// <summary>On load: this window's drafts come straight back (a reload); the others are offered.</summary>
    private async Task RestoreDraftsAsync()
    {
        try
        {
            _windowId = await JS.InvokeAsync<string>("pdfeditDrafts.windowId");
            var all = await JS.InvokeAsync<List<DraftInfo>>("pdfeditDrafts.list");
            _draftsReady = true;
            var mine = all.Where(d => d.Window == _windowId).OrderBy(d => d.Order).ToList();
            int restored = 0;
            foreach (var d in mine)
                if (await RestoreDraftAsync(d, claimed: false)) restored++;
            var shown = mine.FirstOrDefault(d => d.Active && d.Kind == "doc");
            if (shown != null && Tabs.FirstOrDefault(t => t.DraftId == shown.Id) is { } tab && tab != _active) await SwitchTabAsync(tab);
            if (mine.Any(d => d.Kind == "design" && d.Active)) SetDesignMode(true);
            OtherDrafts = all.Where(d => d.Window != _windowId && (d.Changes || d.Kind == "design")).ToList();
            if (restored > 0) Status($"Brought back {restored} draft{(restored == 1 ? "" : "s")} from before the page was reloaded");
        }
        catch { _draftsReady = false; }
        StartDraftTimer();
    }

    /// <summary>Start page: opens a draft from another window (it becomes this window's).</summary>
    public async Task RestoreOtherDraftAsync(DraftInfo d)
    {
        await RunAsync($"Opening the draft of {d.Name}…", async () =>
        {
            await JS.InvokeVoidAsync("pdfeditDrafts.claim", d.Id, _windowId);
            OtherDrafts.Remove(d);
            if (await RestoreDraftAsync(d, claimed: true))
            {
                if (d.Kind == "design") SetDesignMode(true);
                Status($"Draft of {d.Name} opened");
            }
        });
    }

    public async Task DiscardDraftAsync(DraftInfo d)
    {
        bool ok;
        try { ok = await JS.InvokeAsync<bool>("confirm", $"Discard the draft of {d.Name}? It can't be brought back."); }
        catch { ok = true; }
        if (!ok) return;
        await JS.InvokeVoidAsync("pdfeditDrafts.remove", d.Id);
        OtherDrafts.Remove(d);
    }

    private async Task<bool> RestoreDraftAsync(DraftInfo d, bool claimed)
    {
        try
        {
            var json = await ReadDraftTextAsync(d.Id);
            if (json.Length == 0) return false;
            if (d.Kind == "design") return RestoreDesignDraft(json, d.Id, claimed);

            var draft = JsonSerializer.Deserialize<DocDraft>(json, DraftJson) ?? new();
            // The document is still open on the server after a reload. A draft taken over from
            // another window opens its own copy, so closing one window doesn't close the other's.
            var session = claimed || d.SessionId == null ? null : Store.Reclaim(d.SessionId);
            if (session != null && Tabs.Any(t => t.Session == session)) return false;
            if (session == null && d.HasPdf)
            {
                await using var stream = await (await JS.InvokeAsync<IJSStreamReference>("pdfeditDrafts.pdf", d.Id))
                    .OpenReadStreamAsync(PdfDocumentStore.MaxUploadBytes);
                session = await Store.OpenAsync(stream, d.Name);
                session.IsModified = draft.Modified;
            }
            if (session == null)
            {
                await JS.InvokeVoidAsync("pdfeditDrafts.remove", d.Id);
                Toast($"The draft of {d.Name} couldn't be brought back: the document is no longer on the server.", "error");
                return false;
            }

            SetDocument(session);
            var tab = _active!;
            tab.DraftId = d.Id;
            foreach (var item in draft.Items)
                if (item.Image != null) item.ImageUrl = $"data:{(item.Image is [0xFF, 0xD8, ..] ? "image/jpeg" : "image/png")};base64,{Convert.ToBase64String(item.Image)}";
            _items.Clear();
            _items.AddRange(draft.Items);
            foreach (var (k, v) in draft.Values) Values[k] = v;
            foreach (var b in draft.Bounds) _bounds[(b.Name, b.Widget)] = b.Bounds;
            foreach (var (k, v) in draft.Edits) _edits[k] = v;
            foreach (var name in draft.Deleted) _deleted.Add(name);
            _page = draft.Page;
            _zoom = draft.Zoom > 0 ? draft.Zoom : _zoom;
            _fitOnOpen = false;
            _rememberedViewFor = null;   // the draft's own page and zoom win
            _restoreScroll = -1;
            return true;
        }
        catch (Exception ex)
        {
            Toast($"The draft of {d.Name} couldn't be brought back: {ex.Message}", "error");
            return false;
        }
    }

    private bool RestoreDesignDraft(string json, string id, bool claimed)
    {
        var draft = JsonSerializer.Deserialize<DesignDraft>(json, DraftJson);
        if (draft == null || draft.Json.Length == 0) return false;
        var design = DesignDocument.FromJson(draft.Json);
        design.Elements ??= new();
        if (Design.Elements is { Count: > 0 }) DesignCheckpoint();   // Undo goes back to what was there
        Design = design;
        _designName = string.IsNullOrWhiteSpace(draft.Name) ? "Untitled design" : draft.Name;
        SelectedDesignId = null;
        // Another window's design is kept under this window's id from now on.
        if (claimed && id != DesignDraftId) _ = JS.InvokeVoidAsync("pdfeditDrafts.remove", id).AsTask();
        return true;
    }

    private async Task<string> ReadDraftTextAsync(string id)
    {
        var stream = await JS.InvokeAsync<IJSStreamReference>("pdfeditDrafts.state", id);
        await using var s = await stream.OpenReadStreamAsync(256L * 1024 * 1024);
        using var reader = new StreamReader(s);
        return await reader.ReadToEndAsync();
    }

    // ── Where each document was left ─────────────────────────────────────────

    /// <summary>Set when a document is opened: its last page and zoom are looked up after it's drawn.</summary>
    private DocTab? _rememberedViewFor;
    private string? _lastRememberedView;

    private static string ViewKey(DocTab tab) => $"{tab.Session.FileName}|{tab.Session.Info.PageCount}";

    private void RememberView(DocTab tab)
    {
        if (tab == _rememberedViewFor) return;   // just opened: its old place hasn't been put back yet
        var mark = $"{ViewKey(tab)}|{_page}|{_zoom}";
        if (mark == _lastRememberedView) return;
        _lastRememberedView = mark;
        _ = JS.InvokeVoidAsync("pdfedit.view.set", ViewKey(tab), _page, _zoom).AsTask();
    }

    private sealed record RememberedView(int Page, double Zoom);

    /// <summary>A document opened again comes back at the page and zoom it was left at.</summary>
    private async Task<bool> RememberedViewAsync(DocTab tab)
    {
        try
        {
            var v = await JS.InvokeAsync<RememberedView?>("pdfedit.view.get", ViewKey(tab));
            if (v == null || (v.Page <= 0 && Math.Abs(v.Zoom - 1) < 0.01)) return false;
            _page = Math.Clamp(v.Page, 0, Math.Max(0, PageCount - 1));
            _zoom = Math.Clamp(v.Zoom, 0.1, 5);
            _restoreScroll = -1;
            if (_page > 0) Status($"Back at page {_page + 1}, where you left {tab.Session.FileName}");
            return true;
        }
        catch { return false; }
    }
}
