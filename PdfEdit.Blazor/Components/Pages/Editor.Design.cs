using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// The Design canvas (the Windows app's Design view): lay out a form page from text, shapes,
/// tables, pictures, marks and form fields, then export it as a fillable PDF. Designs use the
/// shared .pdfdesign format, so they open in the Windows app too.
/// </summary>
public partial class Editor
{
    public bool DesignMode { get; private set; }
    public DesignDocument Design { get; private set; } = NewDesign();
    private string? _selectedDesignId;

    /// <summary>The selected element (the last one clicked when several are selected).</summary>
    public string? SelectedDesignId
    {
        get => _selectedDesignId;
        set
        {
            _selectedDesignId = value;
            SelectedDesignIds.Clear();
            if (value != null) SelectedDesignIds.Add(value);
        }
    }

    /// <summary>Every selected element, in the order clicked (Ctrl+click adds one).</summary>
    public List<string> SelectedDesignIds { get; } = new();
    public DesignTool DesignToolNow => _designTool;
    private DesignTool _designTool = DesignTool.Select;
    private string _designName = "Untitled design";
    private readonly Stack<string> _designUndo = new();
    private readonly Stack<string> _designRedo = new();
    private (double X, double Y)? _designDragStart;

    public DesignItem? SelectedDesignItem => Design.Elements?.FirstOrDefault(e => e.Id == SelectedDesignId);

    private static DesignDocument NewDesign() => new() { PageSize = "A4", BgColor = "#FFFFFFFF", Elements = new() };

    public void SetDesignMode(bool on)
    {
        DesignMode = on;
        _designTool = DesignTool.Select;
        if (on) { _tab = RibbonTab.Design; ShowRight(RightTab.Properties); Status("Design: insert things from the Design tab, drag to move, drag the corner to resize"); }
        else
        {
            if (_tab == RibbonTab.Design) _tab = RibbonTab.Home;
            _observePages = true;   // the document's pages are drawn again: watch them for the status bar
            Status(Doc == null ? "Ready" : $"Live View — {Doc.FileName}");
        }
    }

    public void StartDesignTool(DesignTool tool)
    {
        if (!DesignMode) SetDesignMode(true);
        SetDesignTool(tool);
    }

    public void SetDesignTool(DesignTool tool)
    {
        _designTool = tool;
        _designDragStart = null;
        Status(tool == DesignTool.Select ? "Select: click to select, drag to move" : "Click on the page to add it, or drag to draw it at a size");
    }

    /// <summary>Remember the design before a change, for Undo.</summary>
    public void DesignCheckpoint()
    {
        _designUndo.Push(Design.ToJson());
        if (_designUndo.Count > 100) { var keep = _designUndo.Take(100).Reverse().ToList(); _designUndo.Clear(); foreach (var k in keep) _designUndo.Push(k); }
        _designRedo.Clear();
    }

    public bool CanUndoDesign => _designUndo.Count > 0;
    public bool CanRedoDesign => _designRedo.Count > 0;

    public void UndoDesign()
    {
        if (_designUndo.Count == 0) return;
        _designRedo.Push(Design.ToJson());
        Design = DesignDocument.FromJson(_designUndo.Pop());
        SelectedDesignId = null;
    }

    public void RedoDesign()
    {
        if (_designRedo.Count == 0) return;
        _designUndo.Push(Design.ToJson());
        Design = DesignDocument.FromJson(_designRedo.Pop());
        SelectedDesignId = null;
    }

    public (double W, double H) DesignSize => Design.Size;
    private double DesignWidthPx => DesignSize.W * PointsToPx * _zoom;
    private double DesignHeightPx => DesignSize.H * PointsToPx * _zoom;

    // ── Adding things ────────────────────────────────────────────────────────

    private void DesignDown(MouseEventArgs e)
    {
        double scale = DesignWidthPx / DesignSize.W;
        _designDragStart = (e.OffsetX / scale, e.OffsetY / scale);
    }

    private void DesignUp(MouseEventArgs e)
    {
        double scale = DesignWidthPx / DesignSize.W;
        double x = e.OffsetX / scale, y = e.OffsetY / scale;
        var start = _designDragStart ?? (x, y);
        _designDragStart = null;
        double left = Math.Min(start.X, x), top = Math.Min(start.Y, y);
        double w = Math.Abs(x - start.X), h = Math.Abs(y - start.Y);
        // A drag in either direction counts: thin rules and straight lines are only a few points tall.
        bool dragged = w > 6 || h > 6;

        var item = NewDesignItem(_designTool);
        if (item == null) return;
        if (dragged && _designTool is not (DesignTool.Check or DesignTool.Cross or DesignTool.Checkbox or DesignTool.Radio))
        {
            item.X = left; item.Y = top; item.W = Math.Max(w, 1); item.H = Math.Max(h, 1);
            if (_designTool is DesignTool.Line or DesignTool.Arrow)
            {
                item.FlipX = x < start.X;   // keep the direction the line was drawn in
                item.FlipY = y < start.Y;
            }
        }
        else { item.X = x; item.Y = y; }
        item.X = Snap(item.X); item.Y = Snap(item.Y);
        var (pw, ph) = DesignSize;
        item.X = Math.Clamp(item.X, 0, Math.Max(0, pw - item.W));
        item.Y = Math.Clamp(item.Y, 0, Math.Max(0, ph - item.H));

        DesignCheckpoint();
        Design.Elements ??= new();
        Design.Elements.Add(item);
        SelectedDesignId = item.Id;
        _designTool = DesignTool.Select;
        ShowRight(RightTab.Properties);
        Status($"Added {DesignItemName(item)} — change it in Properties");
    }

    private string NextFieldName(string prefix)
    {
        var names = Design.Elements?.Select(e => e.FieldName).ToHashSet() ?? [];
        for (int n = 1; ; n++)
            if (!names.Contains(prefix + n)) return prefix + n;
    }

    private DesignItem? NewDesignItem(DesignTool tool) => tool switch
    {
        DesignTool.Text => new DesignItem { Type = "text", W = 220, H = 28, Text = "Your text", FontFamily = "Helvetica", FontSize = 14, Color = "#FF000000", BgColor = "#00000000", Alignment = "Left", Wrap = true },
        DesignTool.Rectangle => new DesignItem { Type = "shape", ShapeType = "Rectangle", W = 160, H = 90, FillColor = "#1E0078FF", StrokeColor = "#FF000000", StrokeThick = 2 },
        DesignTool.Ellipse => new DesignItem { Type = "shape", ShapeType = "Ellipse", W = 120, H = 90, FillColor = "#00000000", StrokeColor = "#FF000000", StrokeThick = 2 },
        DesignTool.Line => new DesignItem { Type = "shape", ShapeType = "Line", W = 160, H = 1, FillColor = "#00000000", StrokeColor = "#FF000000", StrokeThick = 2 },
        DesignTool.Arrow => new DesignItem { Type = "shape", ShapeType = "Arrow", W = 140, H = 40, FillColor = "#00000000", StrokeColor = "#FFC62828", StrokeThick = 2 },
        DesignTool.Table => NewTable(),
        DesignTool.Check => new DesignItem { Type = "text", W = 18, H = 18, Text = "✓", FontSize = 16, Color = "#FF1E7B34", Alignment = "Center" },
        DesignTool.Cross => new DesignItem { Type = "text", W = 18, H = 18, Text = "✕", FontSize = 16, Color = "#FFC62828", Alignment = "Center" },
        DesignTool.TextField => Field("Text", "Name", "Name", 300, 22),
        DesignTool.Memo => Field("Memo", "Comments", "Comments", 360, 70),
        DesignTool.Checkbox => Field("Checkbox", "Agree", "I agree", 140, 16, labelPosition: "Right"),
        DesignTool.Radio => Field("Radio", "Choice", "Option", 120, 16, labelPosition: "Right", uniqueName: false),
        DesignTool.ComboBox => Field("ComboBox", "Dropdown", "Choose", 260, 22),
        DesignTool.Signature => Field("Signature", "Signature", "Signature", 300, 44),
        _ => null,
    };

    private DesignItem Field(string kind, string prefix, string label, double w, double h, string labelPosition = "Left", bool uniqueName = true) => new()
    {
        Type = "field", FieldKind = kind, FieldName = uniqueName ? NextFieldName(prefix) : prefix, Label = label,
        LabelPosition = labelPosition, LabelOffset = 6, W = w, H = h, Wrap = true, ExportValue = "Yes",
        OptionsCsv = kind == "ComboBox" ? "Option 1, Option 2, Option 3" : null,
    };

    private static DesignItem NewTable()
    {
        var t = new DesignItem
        {
            Type = "table", W = 300, H = 90, Rows = 3, Columns = 3, BorderColor = "#FF000000",
            HeaderBgColor = "#FFDCE6F5", CellBgColor = "#FFFFFFFF", BorderThick = 1,
        };
        t.FitCells();
        for (int c = 0; c < t.Columns; c++) t.Cells![0][c] = $"Header {c + 1}";
        return t;
    }

    public static string DesignItemName(DesignItem e) => e.Type switch
    {
        "text" when MarkShapes.FromGlyph((e.Text ?? "").Trim()) != null => "mark",
        "text" => "text",
        "shape" => (e.ShapeType ?? "shape").ToLowerInvariant(),
        "image" => "picture",
        "table" => "table",
        "freehand" => "drawing",
        "field" => e.FieldKind switch { "Memo" => "multi-line field", "ComboBox" => "dropdown", "Checkbox" => "check box", "Radio" => "option button", "Signature" => "signature field", _ => "text field" },
        _ => e.Type,
    };

    /// <summary>Adds an uploaded picture to the design (kept inside the design file).</summary>
    public async Task AddDesignPictureAsync(IBrowserFile file)
    {
        if (file.Size > 20 * 1024 * 1024) { Toast("That picture is too big (20 MB at most).", "error"); return; }
        using var ms = new MemoryStream();
        await using (var s = file.OpenReadStream(20 * 1024 * 1024)) await s.CopyToAsync(ms);
        var bytes = ms.ToArray();
        double w = 200, h = 150;
        try
        {
            var data = iText.IO.Image.ImageDataFactory.Create(bytes);
            double iw = data.GetWidth(), ih = data.GetHeight();
            if (iw > 0 && ih > 0) { w = Math.Min(300, iw); h = w * ih / iw; }
        }
        catch { Toast("That file isn't a picture PdfEdit can use (PNG, JPEG, GIF, BMP or TIFF).", "error"); return; }
        DesignCheckpoint();
        var item = new DesignItem { Type = "image", X = 60, Y = 60, W = w, H = h, Signature = Convert.ToBase64String(bytes) };
        Design.Elements ??= new();
        Design.Elements.Add(item);
        SelectedDesignId = item.Id;
        ShowRight(RightTab.Properties);
    }

    // ── Arranging ────────────────────────────────────────────────────────────

    public void DeleteDesignItem()
    {
        if (SelectedDesignItem is not { } e) return;
        DesignCheckpoint();
        Design.Elements!.Remove(e);
        SelectedDesignId = null;
    }

    public void DuplicateDesignItem()
    {
        if (SelectedDesignItem is not { } e) return;
        DesignCheckpoint();
        var copy = e.Clone();
        copy.X += 12; copy.Y += 12;
        if (copy.Type == "field" && copy.FieldKind != "Radio") copy.FieldName = NextFieldName(new string((copy.FieldName ?? "Field").TakeWhile(c => !char.IsDigit(c)).ToArray()));
        Design.Elements!.Add(copy);
        SelectedDesignId = copy.Id;
    }

    public void BringToFront()
    {
        if (SelectedDesignItem is not { } e) return;
        DesignCheckpoint();
        Design.Elements!.Remove(e);
        Design.Elements.Add(e);
    }

    public void SendToBack()
    {
        if (SelectedDesignItem is not { } e) return;
        DesignCheckpoint();
        Design.Elements!.Remove(e);
        Design.Elements.Insert(0, e);
    }

    public void SetDesignPage(string size)
    {
        DesignCheckpoint();
        Design.PageSize = size;
    }

    public void SetDesignBackground(string rgb)
    {
        DesignCheckpoint();
        Design.BgColor = "#FF" + rgb.TrimStart('#');
    }

    /// <summary>Moved or resized on the page (percentages of the page).</summary>
    private void DesignBoxMoved(string id, double l, double t, double w, double h)
    {
        var e = Design.Elements?.FirstOrDefault(x => x.Id == id);
        if (e == null) return;
        DesignCheckpoint();
        var (pw, ph) = DesignSize;
        e.W = Math.Max(1, Snap(w / 100 * pw));
        e.H = Math.Max(1, Snap(h / 100 * ph));
        e.X = Math.Clamp(Snap(l / 100 * pw), 0, pw - Math.Min(e.W, pw));
        e.Y = Math.Clamp(Snap(t / 100 * ph), 0, ph - Math.Min(e.H, ph));
        SelectedDesignId = id;
    }

    // ── Grid, snap, pen and signature (the Design toolbox) ───────────────────

    public const double DesignGridSize = 10;
    public bool DesignGrid { get; set; }
    public bool DesignSnap { get; set; }

    private double Snap(double v) => DesignSnap ? Math.Round(v / DesignGridSize) * DesignGridSize : v;

    /// <summary>A pen stroke drawn on the design page (points as % of the page).</summary>
    private void AddDesignStroke(double[] pct)
    {
        if (pct.Length < 4) return;
        var (pw, ph) = DesignSize;
        var pts = new List<DesignPoint>();
        for (int i = 0; i + 1 < pct.Length; i += 2)
            pts.Add(new DesignPoint { X = Math.Clamp(pct[i], 0, 100) / 100 * pw, Y = Math.Clamp(pct[i + 1], 0, 100) / 100 * ph });
        double x0 = pts.Min(p => p.X), y0 = pts.Min(p => p.Y);
        DesignCheckpoint();
        var item = new DesignItem
        {
            Type = "freehand", X = x0, Y = y0, W = Math.Max(1, pts.Max(p => p.X) - x0), H = Math.Max(1, pts.Max(p => p.Y) - y0),
            Color = "#FF" + DesignPenColor.TrimStart('#').ToUpperInvariant(), Thickness = DesignPenWidth, Strokes = [pts],
        };
        Design.Elements ??= new();
        Design.Elements.Add(item);
        SelectedDesignId = item.Id;
        Status("Added drawing — keep drawing, or pick Select to move it");
    }

    /// <summary>Your signature as a picture on the design (asks for one first).</summary>
    public void AddDesignSignature()
    {
        if (!DesignMode) SetDesignMode(true);
        if (SignaturePng == null) { NewSignature(initials: false); return; }
        var (iw, ih) = PngSize(SignaturePng);
        double w = 160, h = ih > 0 ? w * ih / iw : 50;
        var (pw, ph) = DesignSize;
        DesignCheckpoint();
        var item = new DesignItem { Type = "image", X = 60, Y = ph - h - 80, W = w, H = h, Signature = Convert.ToBase64String(SignaturePng) };
        Design.Elements ??= new();
        Design.Elements.Add(item);
        SelectedDesignId = item.Id;
        _designTool = DesignTool.Select;
        ShowRight(RightTab.Properties);
        Status("Signature added — drag it into place");
    }

    // ── Files ────────────────────────────────────────────────────────────────

    public void NewDesignPage()
    {
        DesignCheckpoint();
        Design = NewDesign();
        _designName = "Untitled design";
        SelectedDesignId = null;
        SetDesignMode(true);
    }

    public async Task OpenDesignAsync(IBrowserFile file)
    {
        try
        {
            using var reader = new StreamReader(file.OpenReadStream(20 * 1024 * 1024));
            var design = DesignDocument.FromJson(await reader.ReadToEndAsync());
            design.Elements ??= new();
            DesignCheckpoint();
            Design = design;
            _designName = Path.GetFileNameWithoutExtension(file.Name);
            SelectedDesignId = null;
            SetDesignMode(true);
            Status($"Opened design {file.Name}");
        }
        catch (Exception ex) { Toast("That isn't a PdfEdit design file: " + ex.Message, "error"); }
    }

    public async Task SaveDesignAsync()
    {
        var json = Design.ToJson();
        var url = await Store.StageDownloadAsync(_designName + ".pdfdesign", path => File.WriteAllText(path, json));
        await JS.InvokeVoidAsync("pdfedit.download", url);
        Status($"Downloaded {_designName}.pdfdesign — open it here or in the Windows app");
    }

    public async Task ExportDesignAsync()
    {
        var design = DesignDocument.FromJson(Design.ToJson());
        await RunAsync("Making the PDF…", async () =>
        {
            var url = await Store.StageDownloadAsync(_designName + ".pdf", path => DesignPdfExporter.Export(design, path));
            await JS.InvokeVoidAsync("pdfedit.download", url);
            Status($"Downloaded {_designName}.pdf");
        });
    }

    /// <summary>Exports the design and opens the PDF in Live View to fill in, sign or keep editing.</summary>
    public async Task OpenDesignAsPdfAsync()
    {
        var design = DesignDocument.FromJson(Design.ToJson());
        await RunAsync("Making the PDF…", async () =>
        {
            var temp = Path.Combine(Path.GetTempPath(), $"pdfedit-design-{Guid.NewGuid():N}.pdf");
            try
            {
                await Task.Run(() => DesignPdfExporter.Export(design, temp));
                var session = await Store.OpenFileAsync(temp);
                session.FileName = _designName + ".pdf";
                SetDocument(session);
                DesignMode = false;
                _tab = RibbonTab.FillSign;
                Status($"Opened {_designName}.pdf in Live View");
            }
            finally { TryDelete(temp); }
        });
    }
}
