using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;

namespace PdfEdit.Blazor.Components.Pages;

// Several documents open at once, as tabs above the pages (like the Windows app's document tabs).
public partial class Editor
{
    private DocTab? _active;
    private double? _restoreScroll;
    private Timer? _keepAlive;

    public List<DocTab> Tabs { get; } = new();
    public DocTab? ActiveTab => _active;

    /// <summary>The document shown.</summary>
    public PdfSession? Doc => _active?.Session;

    /// <summary>Opens a document in a new tab and shows it.</summary>
    private void SetDocument(PdfSession session)
    {
        var existing = Tabs.FirstOrDefault(t => t.Session == session);
        if (existing != null) { Activate(existing); return; }
        SaveActiveState();
        var tab = new DocTab { Session = session };
        Tabs.Add(tab);
        Activate(tab);
        _backstage = null;
        LoadValues();
        if (FieldCount > 0) _right = RightTab.Fields;
        _fitOnOpen = Settings.OpenFitWidth;
        _rememberedViewFor = tab;
        _keepAlive ??= new Timer(_ =>
        {
            // Open tabs stay open however long they sit in the background.
            foreach (var t in Tabs.ToList()) Store.Get(t.Session.Id);
        }, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    /// <summary>The next or previous open document (Alt+Page Down / Up — the browser keeps Ctrl+Tab).</summary>
    private async Task CycleTabAsync(int by)
    {
        if (Tabs.Count < 2) return;
        int i = _active == null ? 0 : Tabs.IndexOf(_active);
        await SwitchTabAsync(Tabs[((i + by) % Tabs.Count + Tabs.Count) % Tabs.Count]);
    }

    public async Task SwitchTabAsync(DocTab tab)
    {
        if (tab == _active) { if (DesignMode) SetDesignMode(false); return; }
        double? scroll = null;
        try { if (_active != null && !DesignMode) scroll = await JS.InvokeAsync<double>("pdfedit.scrollTop", _viewer); } catch { }
        SaveActiveState(scroll);
        Activate(tab);
        if (DesignMode) SetDesignMode(false);
        Status($"{tab.Session.FileName}");
    }

    /// <summary>Closes a tab (asking first if it has changes that would be lost).</summary>
    public async Task CloseTabAsync(DocTab tab)
    {
        if (tab.HasChanges)
        {
            bool ok;
            try { ok = await JS.InvokeAsync<bool>("confirm", $"Close {tab.Session.FileName}? Changes you haven't downloaded or saved will be lost."); }
            catch { ok = true; }
            if (!ok) return;
        }
        int index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        Store.Close(tab.Session);
        if (tab != _active) return;
        _active = null;
        if (Tabs.Count > 0) Activate(Tabs[Math.Clamp(index, 0, Tabs.Count - 1)]);
        else ClearDocumentState();
        Status(Tabs.Count > 0 ? $"Closed — showing {Doc!.FileName}" : "Ready");
    }

    /// <summary>File → Close: closes the document shown.</summary>
    public void CloseDocument()
    {
        if (_active == null) return;
        var tab = _active;
        int index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        Store.Close(tab.Session);
        _active = null;
        if (Tabs.Count > 0) Activate(Tabs[Math.Clamp(index, 0, Tabs.Count - 1)]);
        else ClearDocumentState();
        _backstage = null;
        Status("Ready");
    }

    // The shown tab's scalar state goes back into it (its collections are shared already). Without
    // the exact scroll position (another document opening), the tab comes back at its page.
    private void SaveActiveState(double? scrollTop = null)
    {
        if (_active == null) return;
        _active.ScrollTop = scrollTop ?? -1;
        _active.Page = _page;
        _active.Zoom = _zoom;
        _active.SearchHits = _searchHits;
        _active.HitIndex = HitIndex;
        _active.SelectedField = SelectedField;
        _active.PrepareMode = PrepareMode;
        _active.SelectedWidget = SelectedWidget;
    }

    private void Activate(DocTab tab)
    {
        _active = tab;
        Values = tab.Values;
        _original = tab.Original;
        _items = tab.Items;
        _bounds = tab.Bounds;
        _edits = tab.Edits;
        _deleted = tab.Deleted;
        _page = tab.Page;
        _zoom = tab.Zoom;
        _searchHits = tab.SearchHits;
        HitIndex = tab.HitIndex;
        SelectedField = tab.SelectedField;
        PrepareMode = tab.PrepareMode;
        SelectedWidget = tab.SelectedWidget;
        _tool = Tool.Select;
        EditingNote = null;
        DetectedFields = null;
        Comparison = null;
        SignatureChecks = null;
        _observePages = true;
        _restoreScroll = tab.ScrollTop;
    }

    private void ClearDocumentState()
    {
        Values = new();
        _original = new();
        _items = new();
        _bounds = new();
        _edits = new();
        _deleted = new();
        _searchHits = new();
        HitIndex = -1;
        SelectedField = null;
        PrepareMode = false;
        SelectedWidget = null;
        _page = 0;
    }

    public bool TabHasChanges(DocTab tab) => tab == _active ? Doc!.IsModified || HasPending : tab.HasChanges;
}
