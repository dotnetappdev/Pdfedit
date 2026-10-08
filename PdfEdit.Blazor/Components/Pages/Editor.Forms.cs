using System.Text.Json;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// Prepare Form: add form fields (by hand or Detect Fields), select, move and resize them with the
/// mouse, change their properties and delete them. Moves, resizes, property changes and deletions
/// are kept until Apply Changes (like the Windows app); adding a field writes it straight away.
/// </summary>
public partial class Editor
{
    public bool PrepareMode { get; private set; }
    public (string Name, int Widget)? SelectedWidget { get; private set; }

    private Dictionary<(string Name, int WidgetIndex), FieldBounds> _bounds = new();
    private Dictionary<string, FormFieldInfo> _edits = new();
    private HashSet<string> _deleted = new();

    public List<NewField>? DetectedFields { get; private set; }

    private int FieldChangeCount => _bounds.Count + _edits.Count + _deleted.Count;

    public void TogglePrepareMode()
    {
        PrepareMode = !PrepareMode;
        SelectedWidget = null;
        if (PrepareMode) { SetTool(Tool.Select); ShowRight(RightTab.Fields); Status("Prepare Form: click a field to select it, drag to move, drag its corner to resize"); }
        else Status("Back to filling in the form");
    }

    /// <summary>The field's rectangle, including a move or resize not applied yet.</summary>
    public (double Left, double Bottom, double Width, double Height) FieldRect(FormFieldInfo f) =>
        _bounds.TryGetValue((f.Name, f.WidgetIndex), out var b) ? (b.Left, b.Bottom, b.Width, b.Height) : (f.Left, f.Bottom, f.Width, f.Height);

    public bool IsDeleted(FormFieldInfo f) => _deleted.Contains(f.Name);

    public string FieldLabel(FormFieldInfo f) => _edits.TryGetValue(f.Name, out var e) ? e.DisplayName : f.Name;

    public void SelectWidget(FormFieldInfo f)
    {
        SelectedWidget = (f.Name, f.WidgetIndex);
        SelectedField = f.Name;
    }

    public FormFieldInfo? SelectedFieldInfo =>
        SelectedWidget is { } w ? Doc?.Info.FormFields.FirstOrDefault(f => f.Name == w.Name && f.WidgetIndex == w.Widget) : null;

    /// <summary>The editable copy of a field's properties (made on first use).</summary>
    public FormFieldInfo FieldDraft(FormFieldInfo f)
    {
        if (!_edits.TryGetValue(f.Name, out var e))
        {
            e = JsonSerializer.Deserialize<FormFieldInfo>(JsonSerializer.Serialize(f))!;
            _edits[f.Name] = e;
        }
        return e;
    }

    public void DeleteSelectedField()
    {
        if (SelectedFieldInfo is not { } f) return;
        _deleted.Add(f.Name);
        _edits.Remove(f.Name);
        SelectedWidget = null;
        SelectedField = null;
        Status($"Field “{f.Name}” will be deleted when you apply changes");
    }

    /// <summary>A field box was dragged or resized in the browser (percentages of the page).</summary>
    [JSInvokable]
    public Task OnBoxMoved(string key, double leftPct, double topPct, double widthPct, double heightPct)
    {
        if (key.StartsWith("d:"))
        {
            DesignBoxMoved(key[2..], leftPct, topPct, widthPct, heightPct);
            return InvokeAsync(StateHasChanged);
        }
        if (Doc == null) return Task.CompletedTask;
        if (key.StartsWith("img:"))
        {
            ImageBoxMoved(leftPct, topPct, widthPct, heightPct);
            return InvokeAsync(StateHasChanged);
        }
        if (key.StartsWith("f:"))
        {
            var parts = key[2..].Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int widget)) return Task.CompletedTask;
            string name = Uri.UnescapeDataString(parts[0]);
            var f = Doc.Info.FormFields.FirstOrDefault(x => x.Name == name && x.WidgetIndex == widget);
            if (f == null) return Task.CompletedTask;
            int fp = f.PageNumber - 1;
            var (pw, ph) = ViewSize(fp);
            double w = Math.Max(4, widthPct / 100 * pw), h = Math.Max(4, heightPct / 100 * ph);
            double left = Math.Clamp(leftPct / 100 * pw, 0, pw - w), top = Math.Clamp(topPct / 100 * ph, 0, ph - h);
            var u = ToUser(fp, left, top, w, h);
            _bounds[(name, widget)] = new FieldBounds(u.Left, u.Bottom, u.Width, u.Height);
            if (!IsWidgetSelected(f)) SelectWidgetMulti(f, false);
            Status($"Moved “{name}” — Apply Changes writes it into the PDF");
        }
        else if (key.StartsWith("i:"))
        {
            var item = _items.FirstOrDefault(i => i.Id == key[2..]);
            if (item == null) return Task.CompletedTask;
            var (pw, ph) = ViewSize(item.Page);
            var (oldLeft, oldTop, oldWidth, oldHeight) = (item.Left, item.Top, item.Width, item.Height);
            item.Width = Math.Max(4, widthPct / 100 * pw);
            item.Height = Math.Max(4, heightPct / 100 * ph);
            item.Left = Math.Clamp(leftPct / 100 * pw, 0, pw - item.Width);
            item.Top = Math.Clamp(topPct / 100 * ph, 0, ph - item.Height);
            if (item.Kind == ItemKind.Mark) item.FontSize = Math.Min(item.Width, item.Height);
            // Sizing a text box by hand stops it auto-sizing (it wraps to the new width instead).
            bool sized = Math.Abs(item.Width - oldWidth) > 0.5 || Math.Abs(item.Height - oldHeight) > 0.5;
            if (sized && item.Kind == ItemKind.Text && item.Fit == TextFit.Auto) item.Fit = TextFit.Wrap;
            MoveSketchPoints(item, oldLeft, oldTop, oldWidth, oldHeight);
        }
        return InvokeAsync(StateHasChanged);
    }

    // ── Adding fields ────────────────────────────────────────────────────────

    private static bool IsFieldTool(Tool t) => t is Tool.FieldText or Tool.FieldCheckbox or Tool.FieldRadio
        or Tool.FieldCombo or Tool.FieldList or Tool.FieldDate or Tool.FieldSignature;

    private string UniqueFieldName(string prefix)
    {
        var names = Doc?.Info.FormFields.Select(f => f.Name).ToHashSet() ?? [];
        for (int n = 1; ; n++)
            if (!names.Contains(prefix + n)) return prefix + n;
    }

    private async Task AddFieldAsync(int page, double left, double top, double width, double height)
    {
        if (Doc == null) return;
        var tool = _tool;
        bool small = tool is Tool.FieldCheckbox or Tool.FieldRadio;
        if (small) { width = height = 14; left -= 7; top -= 7; }
        else if (width < 8 || height < 8) { width = tool == Tool.FieldSignature ? 180 : 150; height = tool switch { Tool.FieldSignature => 40, Tool.FieldList => 60, _ => 20 }; }
        var u = ToUser(page, left, top, width, height);
        float l = (float)u.Left, b = (float)u.Bottom, w = (float)u.Width, h = (float)u.Height;
        int pageNo = page + 1;
        var (prefix, kind) = tool switch
        {
            Tool.FieldCheckbox => ("Check", "check box"),
            Tool.FieldRadio => ("Choice", "option button"),
            Tool.FieldCombo => ("Dropdown", "dropdown"),
            Tool.FieldList => ("List", "list box"),
            Tool.FieldDate => ("Date", "date field"),
            Tool.FieldSignature => ("Signature", "signature field"),
            _ => ("Text", "text field"),
        };
        string name = UniqueFieldName(prefix);
        string[] choices = ["Option 1", "Option 2", "Option 3"];
        await ChangeAsync($"Adding a {kind}…", $"Added {kind} “{name}” — set its name and options in Properties", (src, dest) =>
        {
            switch (tool)
            {
                case Tool.FieldCheckbox: Store.Forms.AddCheckboxField(src, dest, pageNo, l, b, w, name); break;
                case Tool.FieldRadio: Store.Forms.AddRadioButtonField(src, dest, pageNo, l, b, w, name, "Choice1"); break;
                case Tool.FieldCombo: Store.Forms.AddComboBoxField(src, dest, pageNo, l, b, w, h, name, choices); break;
                case Tool.FieldList: Store.Forms.AddListBoxField(src, dest, pageNo, l, b, w, h, name, choices); break;
                case Tool.FieldDate: Store.Forms.AddDateField(src, dest, pageNo, l, b, w, h, name); break;
                case Tool.FieldSignature: Store.Forms.AddSignatureField(src, dest, pageNo, l, b, w, h, name); break;
                default: Store.Forms.AddTextFormField(src, dest, pageNo, l, b, w, h, name); break;
            }
        });
        _tool = Tool.Select;   // like Add Text: straight back to selecting, so the new field can be adjusted
        if (Doc.Info.FormFields.FirstOrDefault(f => f.Name == name) is { } added)
        {
            PrepareMode = true;
            SelectWidget(added);
            ShowRight(RightTab.Properties);
        }
    }

    // ── Detect Fields ────────────────────────────────────────────────────────

    /// <summary>
    /// Acrobat Prepare Form's auto-detect, as in the Windows app: find the empty boxes, squares and
    /// blank lines on each upright page, name them from their printed labels, and offer them.
    /// </summary>
    public async Task DetectFieldsAsync()
    {
        if (Doc == null) return;
        await RunAsync("Looking for fields…", async () =>
        {
            await CommitPendingAsync();
            var doc = Doc;
            var found = new List<NewField>();
            for (int i = 0; i < PageCount; i++)
            {
                Status($"Looking for fields: page {i + 1} of {PageCount}…");
                StateHasChanged();
                if (Rotation(i) != 0) continue;   // detection assumes an upright page
                var (wPt, hPt) = PageSize(i);
                var page = await doc.Renderer.RenderPageAsync(i, 1.0, 2.0 * 72 / 96);   // 2 pixels per point
                int w = page.PixelWidth, h = page.PixelHeight;
                var px = page.Pixels ?? [];
                var lum = new byte[w * h];
                for (int p = 0, q = 0; p < lum.Length && q + 2 < px.Length; p++, q += 4)
                    lum[p] = (byte)((px[q] * 29 + px[q + 1] * 150 + px[q + 2] * 77) >> 8);
                double s = w / wPt;
                int pageNo = i + 1;
                var path = doc.CurrentPath;
                var boxes = await Task.Run(() => FieldDetector.Detect(lum, w, h, s));
                var chunks = await Task.Run(() => PageTextLocator.GetChunks(path, pageNo));
                var existing = doc.Info.FormFields.Where(f => f.PageNumber == pageNo).ToList();
                foreach (var bx in boxes)
                {
                    double left = bx.Left / s, top = hPt - bx.Top / s, right = bx.Right / s, bottom = hPt - bx.Bottom / s;
                    if (existing.Any(f => f.Left < right && f.Left + f.Width > left && f.Bottom < top && f.Bottom + f.Height > bottom)) continue;
                    double pad = bx.IsCheckBox ? 1 : 0.5;
                    found.Add(new NewField
                    {
                        Page = pageNo, IsCheckBox = bx.IsCheckBox, Multiline = !bx.IsCheckBox && top - bottom > 34,
                        Left = left + pad, Bottom = bottom + pad, Width = right - left - pad * 2, Height = top - bottom - pad * 2,
                        Name = PageTextLocator.LabelFor(chunks, left, bottom, right, top, bx.IsCheckBox) ?? "",
                    });
                }
            }
            // Unique names, like the Windows app's detect dialog.
            var used = doc.Info.FormFields.Select(f => f.Name).ToHashSet();
            int n = 0;
            foreach (var f in found)
            {
                var baseName = string.IsNullOrWhiteSpace(f.Name) ? (f.IsCheckBox ? "Check" : "Text") + (++n) : f.Name;
                var name = baseName;
                for (int k = 2; used.Contains(name); k++) name = $"{baseName} {k}";
                f.Name = name;
                used.Add(name);
            }
            if (found.Count == 0)
            {
                Toast("No empty boxes, squares or blank lines were found. You can still add fields by hand.");
                Status("No fields found");
                return;
            }
            DetectedFields = found;
            _dialog = DialogKind.DetectFields;
            Status($"Found {found.Count} possible field{(found.Count == 1 ? "" : "s")}");
        });
    }

    public async Task AddDetectedFieldsAsync()
    {
        var chosen = DetectedFields?.Where(f => f.Include).ToList() ?? [];
        DetectedFields = null;
        if (chosen.Count == 0) return;
        await ChangeAsync("Adding fields…", $"Added {chosen.Count} field{(chosen.Count == 1 ? "" : "s")}", (src, dest) => DetectFieldsService.AddFields(src, dest, chosen));
        PrepareMode = true;
        ShowRight(RightTab.Fields);
        Toast($"{chosen.Count} fillable field{(chosen.Count == 1 ? "" : "s")} added.", "success");
    }

    // ── Keyboard in Prepare Form ─────────────────────────────────────────────

    [JSInvokable]
    public Task OnDeleteKey()
    {
        if (DesignMode && SelectedDesignItem != null) { DeleteDesignItem(); return InvokeAsync(StateHasChanged); }
        if (PrepareMode && SelectedWidget != null) { DeleteSelectedField(); return InvokeAsync(StateHasChanged); }
        if (!PrepareMode && SelectedItem != null) { DeleteSelectedItem(); return InvokeAsync(StateHasChanged); }
        return Task.CompletedTask;
    }
}
