using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>The Design tab's templates, import, formatting, Align, layers, page and image export (as in the Windows app).</summary>
public partial class Editor
{
    public static readonly string[] DesignFonts = ["Helvetica", "Times New Roman", "Courier New", "Arial", "Segoe UI", "Georgia", "Verdana"];

    public double DesignPenWidth { get; set; } = 2;
    public string DesignPenColor { get; set; } = "#000000";

    /// <summary>Ctrl+click adds an element to the selection (or takes it out); a plain click selects just it.</summary>
    public void SelectDesign(string id, bool add)
    {
        if (!add) { SelectedDesignId = id; return; }
        if (SelectedDesignIds.Remove(id))
        {
            _selectedDesignId = SelectedDesignIds.LastOrDefault();
            return;
        }
        SelectedDesignIds.Add(id);
        _selectedDesignId = id;
    }

    /// <summary>Changes the selected elements (Text Format, Shape Format, Field, Position &amp; Size), with Undo.</summary>
    private void DesignFormat(Action<DesignItem> change)
    {
        var items = Design.Elements?.Where(e => SelectedDesignIds.Contains(e.Id)).ToList() ?? [];
        if (items.Count == 0) return;
        DesignCheckpoint();
        foreach (var e in items) change(e);
    }

    private void ArrangeDesign(ArrangeOp op)
    {
        var items = SelectedDesignIds.Select(id => Design.Elements?.FirstOrDefault(e => e.Id == id)).OfType<DesignItem>().ToList();
        if (Arrange.Needs(op, items.Count) is { } why) { Toast(why); return; }
        var (pw, ph) = DesignSize;
        var moved = Arrange.Apply(items.Select(e => new Box(e.X, e.Y, e.W, e.H)).ToList(), items.Count - 1, op, pw, ph);
        DesignCheckpoint();
        for (int i = 0; i < items.Count; i++)
        {
            items[i].X = moved[i].Left; items[i].Y = moved[i].Top;
            items[i].W = Math.Max(1, moved[i].Width); items[i].H = Math.Max(1, moved[i].Height);
        }
    }

    /// <summary>Bring Forward (+1) / Send Backward (−1): one layer.</summary>
    private void MoveDesignLayer(int by)
    {
        if (SelectedDesignItem is not { } e || Design.Elements is not { } list) return;
        int i = list.IndexOf(e), j = Math.Clamp(i + by, 0, list.Count - 1);
        if (i == j) return;
        DesignCheckpoint();
        list.RemoveAt(i);
        list.Insert(j, e);
    }

    private async Task FitDesignAsync()
    {
        double width = await JS.InvokeAsync<double>("pdfedit.viewerWidth", _viewer);
        var (pw, ph) = DesignSize;
        SetZoom(Math.Min((width - 140) / (pw * PointsToPx), 720 / (ph * PointsToPx)));
    }

    private void LoadTemplate(string name)
    {
        DesignCheckpoint();
        Design = DesignTemplates.Create(name);
        Design.Elements ??= new();
        _designName = DesignTemplates.Title(name);
        SelectedDesignId = null;
        SetDesignMode(true);
        Status($"{DesignTemplates.Title(name)} template — click anything to change it");
    }

    /// <summary>The current PDF page as editable design elements (text, pictures, boxes and its form fields).</summary>
    private async Task ImportPdfPageAsync()
    {
        if (Doc == null) return;
        int page = _page;
        await RunAsync("Importing the page…", async () =>
        {
            await CommitPendingAsync();
            var path = Doc.CurrentPath;
            var fields = Doc.Info.FormFields.ToList();
            var (items, w, h) = await Task.Run(() => PdfPageToDesign.ExtractPage(path, page + 1, fields));
            DesignCheckpoint();
            Design = new DesignDocument { PageSize = "Custom", CustomWidth = w, CustomHeight = h, BgColor = "#FFFFFFFF", Elements = items };
            _designName = $"{BaseName} page {page + 1}";
            SelectedDesignId = null;
            SetDesignMode(true);
            Status($"Imported page {page + 1}: {items.Count} element{(items.Count == 1 ? "" : "s")} — change anything, then Export PDF");
        });
    }

    /// <summary>The design as a picture (PNG made here; JPEG converted in the browser).</summary>
    private async Task ExportDesignImageAsync(string format)
    {
        var design = DesignDocument.FromJson(Design.ToJson());
        await RunAsync("Making the picture…", async () =>
        {
            var temp = Path.Combine(Path.GetTempPath(), $"pdfedit-design-{Guid.NewGuid():N}.pdf");
            PdfSession? session = null;
            try
            {
                await Task.Run(() => DesignPdfExporter.Export(design, temp));
                session = await Store.OpenFileAsync(temp);
                var png = await Store.RenderPageAsync(session, 0, 2);
                string name = _designName + (format == "jpeg" ? ".jpg" : ".png");
                if (format == "jpeg")
                    await JS.InvokeVoidAsync("pdfedit.downloadAsJpeg", Convert.ToBase64String(png), name);
                else
                {
                    var url = await Store.StageDownloadAsync(name, path => File.WriteAllBytes(path, png));
                    await JS.InvokeVoidAsync("pdfedit.download", url);
                }
                Status($"Downloaded {name}");
            }
            finally
            {
                if (session != null) Store.Close(session);
                TryDelete(temp);
            }
        });
    }
}
