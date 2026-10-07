using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// The editor: the Windows app's main window for the browser — ribbon, thumbnails, the pages
/// with their form fields, the side panel and the status bar. The PDF work itself is done on the
/// server by PdfEdit.Core and PdfEdit.Render (see PdfDocumentStore).
/// </summary>
public partial class Editor
{
    public const string GitHubUrl = "https://github.com/dotnetappdev/pdfedit";
    private const double PointsToPx = 96.0 / 72.0;

    [Inject] public PdfDocumentStore Store { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IConfiguration Config { get; set; } = default!;
    [Inject] private IWebHostEnvironment Env { get; set; } = default!;

    public PdfSession? Doc { get; private set; }

    // ── View state ───────────────────────────────────────────────────────────
    private RibbonTab _tab = RibbonTab.Home;
    private RightTab _right = RightTab.Fields;
    private Backstage? _backstage;
    public DialogKind Dialog => _dialog;
    private DialogKind _dialog;
    private Tool _tool = Tool.Select;
    private bool _dark, _showLeft = true, _showRight = true, _busy;
    private double _zoom = 1.0;
    private int _page;
    private ElementReference _viewer;
    private DotNetObjectReference<Editor>? _self;
    private bool _observePages;

    // ── Status and messages ──────────────────────────────────────────────────
    private string _status = "Ready";
    private string? _toast;
    private string _toastKind = "";
    private CancellationTokenSource? _toastTimer;

    // ── Form fields and things added on the page ─────────────────────────────
    public Dictionary<string, string> Values { get; } = new();
    private readonly Dictionary<string, string> _original = new();
    public List<PageItem> Items => _items;
    private readonly List<PageItem> _items = new();
    public string? SelectedField { get; set; }
    public PageItem? EditingNote { get; set; }

    // Fill & Sign settings
    private int _fontSize = 12;
    private string _textColor = "#000000";
    private string _strokeColor = "#C62828";
    public byte[]? SignaturePng { get; set; }
    private (int Page, double X, double Y)? _dragStart;

    // Search
    private string _searchText = "";
    public List<TextMatch> SearchHits => _searchHits;
    private List<TextMatch> _searchHits = new();
    public int HitIndex { get; private set; } = -1;
    private TextMatch? CurrentHit => HitIndex >= 0 && HitIndex < _searchHits.Count ? _searchHits[HitIndex] : null;

    // Samples and passwords
    public List<string> Samples { get; private set; } = new();
    public (Func<string, Task> Retry, string Name, bool Wrong)? PendingPassword { get; private set; }

    public bool NoDoc => Doc == null;
    public int PageCount => Doc?.Info.PageCount ?? 0;
    public int CurrentPage => _page;
    public int FieldCount => Doc?.Info.FormFields.Select(f => f.Name).Distinct().Count() ?? 0;
    public bool HasPending => _items.Count > 0 || ChangedValues().Count > 0 || FieldChangeCount > 0;
    private bool CanUndo => Doc != null && (Doc.UndoStack.Count > 0 || HasPending);
    private bool CanRedo => Doc != null && Doc.RedoStack.Count > 0;

    private static string TabName(RibbonTab t) => t switch { RibbonTab.FillSign => "Fill & Sign", RibbonTab.AI => "AI Assistant", _ => t.ToString() };

    protected override void OnInitialized()
    {
        // The repository's Samples folder, when the site runs from a checkout.
        var folder = Path.GetFullPath(Path.Combine(Env.ContentRootPath, Config["PdfEdit:SamplesFolder"] ?? "../Samples"));
        if (Directory.Exists(folder))
            Samples = Directory.GetFiles(folder, "*.pdf").Order().ToList();
        InitAi();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _self = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("pdfedit.listenKeys", _self);
            await JS.InvokeVoidAsync("pdfedit.listenDrag", _self);
        }
        if (_observePages && Doc != null)
        {
            _observePages = false;
            await JS.InvokeVoidAsync("pdfedit.observePages", _self, _viewer);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Doc != null) Store.Close(Doc);
        _self?.Dispose();
        await Task.CompletedTask;
    }

    /// <summary>Re-draws the editor (child components call this after changing its state).</summary>
    public void Refresh() => StateHasChanged();

    // ── Opening and closing ──────────────────────────────────────────────────

    public async Task OpenUploadAsync(IBrowserFile file)
    {
        if (file.Size > PdfDocumentStore.MaxUploadBytes)
        {
            Toast($"That file is too big (the limit is {PdfDocumentStore.MaxUploadBytes / 1024 / 1024} MB).", "error");
            return;
        }
        // Read it now: the browser file can't be read again later (for a password retry).
        var temp = Path.Combine(Path.GetTempPath(), $"pdfedit-upload-{Guid.NewGuid():N}.pdf");
        await using (var s = file.OpenReadStream(PdfDocumentStore.MaxUploadBytes))
        await using (var f = File.Create(temp))
            await s.CopyToAsync(f);
        try { await OpenPathAsync(temp, file.Name, null); }
        finally { if (PendingPassword == null) TryDelete(temp); }
    }

    public Task OpenSampleAsync(string path) => OpenPathAsync(path, Path.GetFileName(path), null);

    private async Task OpenPathAsync(string path, string name, string? password)
    {
        await RunAsync($"Opening {name}…", async () =>
        {
            try
            {
                PdfSession session;
                await using (var stream = File.OpenRead(path))
                    session = await Store.OpenAsync(stream, name, password);
                PendingPassword = null;
                if (path.Contains("pdfedit-upload-")) TryDelete(path);
                SetDocument(session);
                Status($"Opened {name}");
            }
            catch (PasswordRequiredException ex)
            {
                PendingPassword = (pw => OpenPathAsync(path, name, pw), name, ex.WrongPassword);
                _dialog = DialogKind.Password;
            }
        });
    }

    public async Task NewBlankAsync()
    {
        await RunAsync("Creating a blank document…", async () =>
        {
            SetDocument(await Store.CreateBlankAsync());
            Status("New blank document");
        });
    }

    /// <summary>Opens a PDF made from several uploaded files (PDFs and pictures), in order.</summary>
    public async Task CombineFilesAsync(IReadOnlyList<IBrowserFile> files)
    {
        await RunAsync("Combining files…", async () =>
        {
            var folder = Directory.CreateTempSubdirectory("pdfedit-combine-").FullName;
            try
            {
                var paths = await SaveUploadsAsync(files, folder);
                var dest = Path.Combine(folder, "Combined.pdf");
                int pages = await Task.Run(() => PdfToolsService.CombineFiles(paths, dest));
                SetDocument(await Store.OpenFileAsync(dest));
                Status($"Made a {pages}-page PDF from {files.Count} file{(files.Count == 1 ? "" : "s")}");
            }
            finally { try { Directory.Delete(folder, true); } catch { } }
        });
    }

    private void SetDocument(PdfSession session)
    {
        if (Doc != null && Doc != session) Store.Close(Doc);
        Doc = session;
        _page = 0;
        _items.Clear();
        _searchHits = new();
        HitIndex = -1;
        SelectedField = null;
        _tool = Tool.Select;
        _backstage = null;
        ClearFieldChanges();
        PrepareMode = false;
        LoadValues();
        _observePages = true;
        if (FieldCount > 0) _right = RightTab.Fields;
    }

    public void CloseDocument()
    {
        if (Doc != null) Store.Close(Doc);
        Doc = null;
        _items.Clear();
        Values.Clear();
        _original.Clear();
        ClearFieldChanges();
        PrepareMode = false;
        _searchHits = new();
        _backstage = null;
        Status("Ready");
    }

    private void LoadValues()
    {
        Values.Clear();
        _original.Clear();
        if (Doc == null) return;
        foreach (var f in Doc.Info.FormFields)
        {
            Values.TryAdd(f.Name, f.Value);
            _original.TryAdd(f.Name, f.Value);
        }
    }

    // ── Applying changes ─────────────────────────────────────────────────────

    public Dictionary<string, string> ChangedValues() =>
        Values.Where(kv => _original.TryGetValue(kv.Key, out var o) && o != kv.Value)
              .ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>
    /// Writes the filled-in fields and everything added on the pages into the PDF (a new version,
    /// so Undo takes it back out).
    /// </summary>
    public async Task CommitAsync()
    {
        if (Doc == null || !HasPending) return;
        await RunAsync("Applying changes…", async () =>
        {
            await CommitPendingAsync();
            Status("Changes applied to the PDF");
        });
    }

    private async Task CommitPendingAsync()
    {
        if (Doc == null || !HasPending) return;
        var doc = Doc;
        var values = ChangedValues();
        var onValues = doc.Info.FormFields.Where(f => f.FieldType == FieldType.Checkbox)
                          .GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First().ExportValue);
        double H(int page) => PageSize(page).H;

        var texts = _items.Where(i => i.Kind is ItemKind.Text or ItemKind.Mark && !string.IsNullOrWhiteSpace(i.Text))
            .Select(i => new FreeTextAnnotation
            {
                PageNumber = i.Page + 1, Left = i.Left, Bottom = H(i.Page) - i.Top - i.Height,
                Width = i.Width, Height = i.Height, Text = i.Text, FontSize = i.FontSize,
                FontColor = i.Color, FontFamily = "Helvetica", GrowToFit = true,
            }).ToList();
        var signatures = _items.Where(i => i.Kind == ItemKind.Signature && i.Image != null)
            .Select(i => new PlacedSignature
            {
                PageNumber = i.Page + 1, Left = i.Left, Bottom = H(i.Page) - i.Top - i.Height,
                Width = i.Width, Height = i.Height, ImageBytes = i.Image!,
            }).ToList();
        var notes = _items.Where(i => i.Kind == ItemKind.Note)
            .Select(i => new StickyNoteAnnotation
            {
                PageNumber = i.Page + 1, Left = i.Left, Bottom = H(i.Page) - i.Top, Text = i.Text,
                Comment = new CommentInfo { Author = "PdfEdit web", Note = i.Text },
            }).ToList();
        var highlights = _items.Where(i => i.Kind == ItemKind.Highlight)
            .Select(i => new HighlightAnnotation
            {
                PageNumber = i.Page + 1, Left = i.Left, Bottom = H(i.Page) - i.Top - i.Height,
                Width = i.Width, Height = i.Height, Color = "#FFEB00",
            }).ToList();
        var shapes = _items.Where(i => i.Kind is ItemKind.Rectangle or ItemKind.Ellipse)
            .Select(i => new ShapeAnnotation
            {
                PageNumber = i.Page + 1, Kind = i.Kind == ItemKind.Ellipse ? ShapeKind.Ellipse : ShapeKind.Rectangle,
                X1 = i.Left, Y1 = H(i.Page) - i.Top - i.Height, X2 = i.Left + i.Width, Y2 = H(i.Page) - i.Top,
                StrokeColor = i.Color, FillColor = "", LineWidth = 2,
            }).ToList();
        var redactions = _items.Where(i => i.Kind == ItemKind.Redact)
            .Select(i => (i.Page + 1, (float)i.Left, (float)(H(i.Page) - i.Top - i.Height), (float)i.Width, (float)i.Height))
            .ToList();

        var deleted = _deleted.ToList();
        var bounds = new Dictionary<(string Name, int WidgetIndex), FieldBounds>(_bounds);
        var edits = _edits.Values.Where(e => !_deleted.Contains(e.Name)).ToList();

        await Store.ApplyAsync(doc, (src, dest) =>
        {
            bool annotate = values.Count > 0 || texts.Count > 0 || signatures.Count > 0 || notes.Count > 0
                            || highlights.Count > 0 || shapes.Count > 0 || deleted.Count > 0 || bounds.Count > 0 || edits.Count > 0;
            var step = src;
            if (annotate)
            {
                step = redactions.Count > 0 ? dest + ".tmp" : dest;
                Store.Forms.SaveFull(src, step, values, new Dictionary<int, int>(), texts, signatures,
                    flatten: false, deletedFieldNames: deleted, fieldExportValues: onValues, highlightAnnotations: highlights,
                    stickyNotes: notes, shapeAnnotations: shapes, fieldBounds: bounds, fieldEdits: edits);
            }
            if (redactions.Count > 0)
            {
                Store.Forms.ApplyRedactions(step, dest, redactions);
                if (step != src) File.Delete(step);
            }
        });
        _items.Clear();
        ClearFieldChanges();
        LoadValues();
    }

    private void ClearFieldChanges()
    {
        _bounds.Clear();
        _edits.Clear();
        _deleted.Clear();
        SelectedWidget = null;
    }

    public void DiscardPending()
    {
        _items.Clear();
        ClearFieldChanges();
        LoadValues();
        Status("Discarded the changes you hadn't applied");
    }

    /// <summary>
    /// Runs a change to the document: applies pending edits first (page tools can move pages
    /// around under them), then <paramref name="change"/>(current file, new file).
    /// </summary>
    public async Task ChangeAsync(string busy, string done, Action<string, string> change)
    {
        if (Doc == null) return;
        await RunAsync(busy, async () =>
        {
            await CommitPendingAsync();
            await Store.ApplyAsync(Doc, change);
            LoadValues();
            _page = Math.Clamp(_page, 0, Math.Max(0, PageCount - 1));
            _searchHits = new();
            _observePages = true;
            Status(done);
        });
    }

    /// <summary>Makes a file and downloads it.</summary>
    public async Task DownloadExportAsync(string busy, string fileName, Action<string, string> write)
    {
        if (Doc == null) return;
        await RunAsync(busy, async () =>
        {
            await CommitPendingAsync();
            var current = Doc.CurrentPath;
            var url = await Store.ExportAsync(Doc, fileName, path => write(current, path));
            await JS.InvokeVoidAsync("pdfedit.download", url);
            Status($"Downloaded {fileName}");
        });
    }

    public async Task UndoAsync()
    {
        if (Doc == null) return;
        if (HasPending) { DiscardPending(); return; }
        await RunAsync("Undoing…", async () =>
        {
            if (await Store.UndoAsync(Doc)) { LoadValues(); _observePages = true; Status("Undone"); }
        });
    }

    public async Task RedoAsync()
    {
        if (Doc == null) return;
        await RunAsync("Redoing…", async () =>
        {
            if (await Store.RedoAsync(Doc)) { LoadValues(); _observePages = true; Status("Redone"); }
        });
    }

    /// <summary>Shows a busy bar while <paramref name="work"/> runs, and any error as a message.</summary>
    public async Task RunAsync(string busy, Func<Task> work)
    {
        if (_busy) return;
        _busy = true;
        _status = busy;
        StateHasChanged();
        try { await work(); }
        catch (Exception ex)
        {
            Toast(ex.Message, "error");
            _status = "Something went wrong";
        }
        finally
        {
            _busy = false;
            StateHasChanged();
        }
    }

    public void Status(string text) => _status = text;

    public void Toast(string text, string kind = "")
    {
        _toast = text;
        _toastKind = kind;
        _toastTimer?.Cancel();
        var cts = _toastTimer = new CancellationTokenSource();
        _ = Task.Delay(kind == "error" ? 8000 : 4000, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            _toast = null;
            InvokeAsync(StateHasChanged);
        }, TaskScheduler.Default);
    }

    // ── Dialogs, panels, tools ───────────────────────────────────────────────

    public void ShowDialog(DialogKind kind) => _dialog = kind;

    public void CloseDialog()
    {
        _dialog = DialogKind.None;
        PendingPassword = null;
    }

    public void ShowRight(RightTab tab)
    {
        _right = tab;
        _showRight = true;
    }

    public RightTab RightPanelTab => _right;

    public void OpenBackstage(Backstage page) => _backstage = page;
    public void CloseBackstage() => _backstage = null;
    public Backstage? BackstagePage => _backstage;

    public void SetTool(Tool tool)
    {
        _tool = tool;
        _dragStart = null;
        _status = ToolHint;
    }

    private string ToolHint => _tool switch
    {
        Tool.Text => "Click on the page where the text should go",
        Tool.Date => "Click on the page to add today's date",
        Tool.Check or Tool.Cross or Tool.Dot => "Click on the page to add the mark",
        Tool.Signature => "Click on the page to place your signature",
        Tool.Note => "Click on the page to add a sticky note",
        Tool.Highlight => "Drag over the area to highlight",
        Tool.Redact => "Drag over what to remove; Apply Redactions removes it for good",
        Tool.Rectangle or Tool.Ellipse => "Drag to draw",
        Tool.FieldCheckbox or Tool.FieldRadio => "Click where the box should go",
        Tool.FieldText or Tool.FieldCombo or Tool.FieldList or Tool.FieldDate or Tool.FieldSignature => "Drag to draw the field (or click for a standard size)",
        _ => "Ready",
    };

    public async Task SignatureToolAsync()
    {
        if (SignaturePng == null) { _dialog = DialogKind.Signature; return; }
        SetTool(Tool.Signature);
        await Task.CompletedTask;
    }

    // Page tools: a click (or a drag for the box tools) on the tool layer over the page.
    private void ToolDown(int page, MouseEventArgs e)
    {
        var (x, y) = ToPoints(page, e);
        _dragStart = (page, x, y);
    }

    private async Task ToolUpAsync(int page, MouseEventArgs e)
    {
        if (Doc == null) return;
        if (Rotation(page) != 0)
        {
            Toast("Adding things to rotated pages isn't supported here yet. Rotate the page upright first.", "error");
            return;
        }
        var (x, y) = ToPoints(page, e);
        var start = _dragStart is { } d && d.Page == page ? (d.X, d.Y) : (x, y);
        _dragStart = null;
        double left = Math.Min(start.Item1, x), top = Math.Min(start.Item2, y);
        double width = Math.Abs(x - start.Item1), height = Math.Abs(y - start.Item2);
        var (pw, ph) = PageSize(page);

        if (IsFieldTool(_tool))
        {
            await AddFieldAsync(page, left, top, width, height);
            return;
        }

        PageItem? added = null;
        switch (_tool)
        {
            case Tool.Text:
            case Tool.Date:
                double size = _fontSize;
                string text = _tool == Tool.Date ? DateTime.Today.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) : "";
                added = new PageItem
                {
                    Kind = ItemKind.Text, Page = page, Left = x, Top = y - size * 0.6,
                    Width = _tool == Tool.Date ? Math.Max(60, text.Length * size * 0.55) : 160, Height = size * 1.45,
                    FontSize = size, Color = _textColor, Text = text,
                };
                break;
            case Tool.Check or Tool.Cross or Tool.Dot:
                double m = Math.Max(10, _fontSize);
                added = new PageItem
                {
                    Kind = ItemKind.Mark, Page = page, Left = x - m / 2, Top = y - m / 2, Width = m, Height = m,
                    FontSize = m, Color = _textColor, Text = _tool switch { Tool.Check => "✓", Tool.Cross => "✕", _ => "●" },
                };
                break;
            case Tool.Signature when SignaturePng != null:
                var (iw, ih) = PngSize(SignaturePng);
                double sw = 150, sh = ih > 0 ? sw * ih / iw : 50;
                added = new PageItem
                {
                    Kind = ItemKind.Signature, Page = page, Left = x - sw / 2, Top = y - sh / 2, Width = sw, Height = sh,
                    Image = SignaturePng, ImageUrl = "data:image/png;base64," + Convert.ToBase64String(SignaturePng),
                };
                break;
            case Tool.Note:
                added = new PageItem { Kind = ItemKind.Note, Page = page, Left = x, Top = y, Width = 20, Height = 20 };
                EditingNote = added;
                _dialog = DialogKind.Note;
                break;
            case Tool.Highlight or Tool.Redact or Tool.Rectangle or Tool.Ellipse:
                if (width < 3 || height < 3) { Toast("Drag across the area."); return; }
                added = new PageItem
                {
                    Kind = _tool switch { Tool.Highlight => ItemKind.Highlight, Tool.Redact => ItemKind.Redact, Tool.Ellipse => ItemKind.Ellipse, _ => ItemKind.Rectangle },
                    Page = page, Left = left, Top = top, Width = width, Height = height, Color = _strokeColor,
                };
                break;
        }
        if (added == null) return;
        added.Left = Math.Clamp(added.Left, 0, Math.Max(0, pw - added.Width));
        added.Top = Math.Clamp(added.Top, 0, Math.Max(0, ph - added.Height));
        _items.Add(added);
        _page = page;
        Status(added.Describe() + " added — Apply Changes or Download writes it into the PDF");

        if (added.Kind == ItemKind.Text)
        {
            _tool = Tool.Select;   // like the Windows app: type straight away
            StateHasChanged();
            await Task.Yield();
            await JS.InvokeVoidAsync("pdfedit.focus", "item-" + added.Id);
        }
    }

    public void RemoveItem(PageItem item)
    {
        _items.Remove(item);
        Status("Removed");
    }

    private (double X, double Y) ToPoints(int page, MouseEventArgs e)
    {
        var (w, _) = PageSize(page);
        double scale = DisplayWidth(page) / w;   // pixels per point
        return (e.OffsetX / scale, e.OffsetY / scale);
    }

    private static (int W, int H) PngSize(byte[] png) =>
        png.Length > 24 ? (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)),
                           System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))) : (0, 0);

    // ── Navigation and zoom ──────────────────────────────────────────────────

    [JSInvokable]
    public Task OnPageInView(int page)
    {
        if (page == _page) return Task.CompletedTask;
        _page = page;
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task OnShortcut(string keys)
    {
        switch (keys)
        {
            case "Ctrl+o": OpenBackstage(Backstage.Open); break;
            case "Ctrl+s": await SaveAsync(); break;
            case "Ctrl+p": await PrintAsync(); break;
            case "Ctrl+z": if (DesignMode) UndoDesign(); else await UndoAsync(); break;
            case "Ctrl+y" or "Shift+Ctrl+z": if (DesignMode) RedoDesign(); else await RedoAsync(); break;
            case "Ctrl+f": _tab = RibbonTab.Home; ShowRight(RightTab.Search); break;
            case "Ctrl+=" or "Ctrl++": Zoom(1.25); break;
            case "Ctrl+-": Zoom(0.8); break;
            case "Ctrl+0": SetZoom(1); break;
            case "Escape":
                if (_dialog != DialogKind.None) CloseDialog();
                else if (_backstage != null) _backstage = null;
                else if (DesignMode && _designTool != DesignTool.Select) SetDesignTool(DesignTool.Select);
                else if (DesignMode) SelectedDesignId = null;
                else if (_tool != Tool.Select) SetTool(Tool.Select);
                else SelectedWidget = null;
                break;
        }
        StateHasChanged();
    }

    public async Task GoToPageAsync(int page, bool smooth = true)
    {
        if (Doc == null) return;
        _page = Math.Clamp(page, 0, PageCount - 1);
        await JS.InvokeVoidAsync("pdfedit.scrollToPage", _page, smooth);
    }

    private async Task PageBoxChangedAsync(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out int n)) await GoToPageAsync(n - 1, false);
    }

    public void Zoom(double factor) => SetZoom(_zoom * factor);

    public void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(Math.Round(zoom, 3), 0.25, 4);
        Status($"Zoom {(int)Math.Round(_zoom * 100)}%");
    }

    private async Task FitWidthAsync()
    {
        if (Doc == null) return;
        double width = await JS.InvokeAsync<double>("pdfedit.viewerWidth", _viewer);
        double pageWidth = DisplayWidth(_page) / _zoom;
        SetZoom((width - 60) / pageWidth);
    }

    private async Task FitPageAsync()
    {
        await FitWidthAsync();
        // Fit the whole page height into a typical window, too.
        double pageHeight = DisplayHeight(_page) / _zoom;
        SetZoom(Math.Min(_zoom, 760 / pageHeight));
    }

    // ── Geometry ─────────────────────────────────────────────────────────────

    /// <summary>The page's unrotated size in points.</summary>
    public (double W, double H) PageSize(int page) =>
        Doc != null && page >= 0 && page < Doc.Info.PageSizes.Count ? Doc.Info.PageSizes[page] : (612, 792);

    public int Rotation(int page) =>
        Doc != null && page >= 0 && page < Doc.Info.PageRotations.Count ? Doc.Info.PageRotations[page] : 0;

    private bool Sideways(int page) => Rotation(page) is 90 or 270;

    private double DisplayWidth(int page) => (Sideways(page) ? PageSize(page).H : PageSize(page).W) * PointsToPx * _zoom;
    private double DisplayHeight(int page) => (Sideways(page) ? PageSize(page).W : PageSize(page).H) * PointsToPx * _zoom;
    private double ThumbWidth(int page) => Math.Min(110, DisplayWidth(page) / _zoom * 0.14);

    /// <summary>Renders at about twice the screen size, in quarter steps so zooming reuses images.</summary>
    private double ImageScale => Math.Min(4, Math.Ceiling(_zoom * 2 * 4) / 4);

    public string PageUrl(int page, double scale) =>
        Doc == null ? "" : $"/documents/{Doc.Id}/pages/{page}.png?scale={Css(scale)}&v={Doc.Version}";

    public static string Css(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Positions something over the page, in percentages so it follows the page as it zooms.</summary>
    public static string OverlayStyle(double left, double bottom, double width, double height, double pageW, double pageH, double fontPts = 0)
    {
        double top = pageH - bottom - height;
        var style = $"left:{Css(left / pageW * 100)}%;top:{Css(top / pageH * 100)}%;width:{Css(width / pageW * 100)}%;height:{Css(height / pageH * 100)}%";
        if (fontPts > 0) style += $";font-size:{Css(fontPts / pageW * 100)}cqw";
        return style;
    }

    public static double FieldFontPts(FormFieldInfo f) => f.FontSize > 0 ? f.FontSize : Math.Clamp(f.Height * 0.65, 6, 14);

    private string HitStyle(TextMatch m, double w, double h) => OverlayStyle(m.Left, m.Bottom, m.Width, m.Height, w, h);

    // ── Misc ─────────────────────────────────────────────────────────────────

    public async Task OpenUrlAsync(string url) => await JS.InvokeVoidAsync("pdfedit.openInNewTab", url);

    public async Task DownloadUrlAsync(string url) => await JS.InvokeVoidAsync("pdfedit.download", url);

    public async Task FocusAsync(string elementId) => await JS.InvokeVoidAsync("pdfedit.focus", elementId);

    public async Task ScrollToSpotAsync(int page, double topPercent)
    {
        _page = page;
        await JS.InvokeVoidAsync("pdfedit.scrollToSpot", page, topPercent);
    }

    public static async Task<List<string>> SaveUploadsAsync(IReadOnlyList<IBrowserFile> files, string folder)
    {
        var paths = new List<string>();
        int n = 0;
        foreach (var file in files)
        {
            var path = Path.Combine(folder, $"{++n:000}-{PdfDocumentStore.SafeName(file.Name)}");
            await using var s = file.OpenReadStream(PdfDocumentStore.MaxUploadBytes);
            await using var f = File.Create(path);
            await s.CopyToAsync(f);
            paths.Add(path);
        }
        return paths;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
