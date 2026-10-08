using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Models;

namespace PdfEdit.Blazor.Components.Pages;

// The tools the floating toolbox offers beyond the ribbon (as in the Windows app's quick-tools
// rail): hand and marquee zoom, callouts, insert / replace text, underline / squiggly /
// strikethrough, clouds, polygons and polylines, the eraser, circle and line marks, vertical text
// and initials.
public partial class Editor
{
    private string _highlightColor = "#FFEB00";
    /// <summary>Colour picked for ticks, crosses and other marks; null = green ticks, black crosses.</summary>
    private string? _markColor;
    public byte[]? InitialsPng { get; set; }
    /// <summary>The signature dialog is making initials rather than a signature.</summary>
    public bool MakingInitials { get; set; }

    public static readonly (string Hex, string Name)[] HighlightColors =
        [("#FFEB00", "Yellow"), ("#7CFC00", "Green"), ("#00E5FF", "Cyan"), ("#FF80AB", "Pink"), ("#FFA726", "Orange")];
    public static readonly (string Hex, string Name)[] InkColors =
        [("#000000", "Black"), ("#1F4FB5", "Blue"), ("#C62828", "Red"), ("#1B7A2E", "Green"), ("#6A1B9A", "Purple"), ("#D35400", "Orange")];

    public string HighlightColor { get => _highlightColor; set => _highlightColor = value; }
    public string StrokeColor { get => _strokeColor; set => _strokeColor = value; }
    public string? MarkColorChoice { get => _markColor; set => _markColor = value; }
    public Tool CurrentTool => _tool;

    private string MarkColor(Tool tool) => _markColor ?? (tool == Tool.Check ? "#1B7A2E" : "#000000");

    private string? ToolboxHint(Tool tool) => tool switch
    {
        Tool.Hand => "Drag to move around the page",
        Tool.Zoom => "Drag round the part to zoom into (click to zoom in)",
        Tool.Callout => "Drag to draw the callout box, then type",
        Tool.InsertText => "Click where the text should go in",
        Tool.ReplaceText => "Drag over the text to replace",
        Tool.Underline => "Drag over the text to underline",
        Tool.Squiggly => "Drag over the text for a squiggly underline",
        Tool.Strikeout => "Drag over the text to strike it through",
        Tool.Cloud => "Drag to draw a cloud",
        Tool.Polygon => "Click each corner, double-click (or Enter) to close",
        Tool.Polyline => "Click each point, double-click (or Enter) to finish",
        Tool.Eraser => "Click a drawing, shape or markup you've added to remove it",
        Tool.Circle or Tool.LineMark => "Click on the page to add the mark",
        Tool.VerticalText => "Click where the text should start (it reads upwards)",
        Tool.Initials => "Click on the page to place your initials",
        _ => null,
    };

    public async Task InitialsToolAsync()
    {
        if (InitialsPng == null) { MakingInitials = true; _dialog = DialogKind.Signature; return; }
        SetTool(Tool.Initials);
        await Task.CompletedTask;
    }

    /// <summary>A click or drag with one of the toolbox tools; returns what to add, or null.</summary>
    private Task<PageItem?> ToolboxItemAsync(int page, double x, double y, double left, double top, double width, double height)
    {
        bool dragged = width >= 3 && height >= 3;
        PageItem? item = null;
        switch (_tool)
        {
            case Tool.Callout:
                if (!dragged) { left = x; top = y; width = 150; height = 50; }
                item = new PageItem
                {
                    Kind = ItemKind.Callout, Page = page, Left = left, Top = top, Width = Math.Max(60, width), Height = Math.Max(24, height),
                    Color = _strokeColor, FontSize = 10,
                };
                // The leader points down and to the left of the box, like Acrobat's default.
                item.Points = [new PointD(Math.Max(0, item.Left - 36), item.Top + item.Height + 36)];
                break;
            case Tool.InsertText:
                item = new PageItem { Kind = ItemKind.InsertText, Page = page, Left = x - 5, Top = y - 10, Width = 10, Height = 10, Color = "#1565C0" };
                EditingNote = item;
                _dialog = DialogKind.Note;
                break;
            case Tool.ReplaceText:
                if (!dragged) { Toast("Drag over the text to replace."); return Task.FromResult<PageItem?>(null); }
                item = new PageItem { Kind = ItemKind.ReplaceText, Page = page, Left = left, Top = top, Width = width, Height = height, Color = "#1565C0" };
                EditingNote = item;
                _dialog = DialogKind.Note;
                break;
            case Tool.Underline or Tool.Squiggly or Tool.Strikeout:
                if (!dragged) { Toast("Drag over the text."); return Task.FromResult<PageItem?>(null); }
                item = new PageItem
                {
                    Kind = ItemKind.Highlight, Page = page, Left = left, Top = top, Width = width, Height = height,
                    Markup = _tool switch { Tool.Underline => HighlightKind.Underline, Tool.Squiggly => HighlightKind.Squiggly, _ => HighlightKind.Strikethrough },
                    Color = _tool == Tool.Strikeout ? "#C62828" : "#1F4FB5",
                };
                break;
            case Tool.Cloud:
                if (!dragged) { Toast("Drag to draw the cloud."); return Task.FromResult<PageItem?>(null); }
                item = new PageItem { Kind = ItemKind.Cloud, Page = page, Left = left, Top = top, Width = width, Height = height, Color = _strokeColor };
                break;
            case Tool.Circle or Tool.LineMark:
                double m = Math.Max(10, _fontSize);
                double w = _tool == Tool.LineMark ? m * 2.5 : m;
                item = new PageItem
                {
                    Kind = ItemKind.Mark, Page = page, Left = x - w / 2, Top = y - m / 2, Width = w, Height = m,
                    FontSize = m, Color = MarkColor(_tool), Text = _tool == Tool.Circle ? "○" : "—",
                };
                break;
            case Tool.VerticalText:
                double size = _fontSize;
                item = new PageItem
                {
                    Kind = ItemKind.Text, Page = page, Vertical = true, Left = x - size * 0.7, Top = Math.Max(0, y - 160),
                    Width = size * 1.45, Height = 160, FontSize = size, Color = _textColor,
                };
                break;
            case Tool.Initials when InitialsPng != null:
                var (iw, ih) = PngSize(InitialsPng);
                double sw = 70, sh = ih > 0 ? sw * ih / iw : 30;
                item = new PageItem
                {
                    Kind = ItemKind.Signature, Page = page, Left = x - sw / 2, Top = y - sh / 2, Width = sw, Height = sh,
                    Image = InitialsPng, ImageUrl = "data:image/png;base64," + Convert.ToBase64String(InitialsPng),
                };
                break;
            case Tool.Eraser:
                Erase(page, x, y);
                break;
        }
        return Task.FromResult(item);
    }

    // The eraser removes the topmost drawing, shape or markup added on the page at the point.
    private void Erase(int page, double x, double y)
    {
        var hit = _items.LastOrDefault(i => i.Page == page && i.Kind is ItemKind.Ink or ItemKind.Line or ItemKind.Arrow or ItemKind.Rectangle
                or ItemKind.Ellipse or ItemKind.Cloud or ItemKind.Polygon or ItemKind.Polyline or ItemKind.Distance or ItemKind.Perimeter
                or ItemKind.Area or ItemKind.Highlight or ItemKind.Callout
            && x >= i.Left - 3 && x <= i.Left + i.Width + 3 && y >= i.Top - 3 && y <= i.Top + i.Height + 3
            && (i.Kind != ItemKind.Ink || i.Points!.Any(p => Math.Abs(p.X - x) < 8 && Math.Abs(p.Y - y) < 8)));
        if (hit == null) { Status("Nothing you've added is there to erase"); return; }
        _items.Remove(hit);
        Status($"Erased {hit.Describe().ToLowerInvariant()}");
    }

    /// <summary>Marquee zoom: the dragged area fills the window (a click zooms in a step).</summary>
    private async Task ZoomToAsync(int page, double left, double top, double width, double height)
    {
        if (width < 8 || height < 8)
        {
            await ZoomByAsync(1.25);
            return;
        }
        double viewer = await JS.InvokeAsync<double>("pdfedit.viewerWidth", _viewer);
        double fit = (viewer - 60) / (width * PointsToPx);
        _zoom = Math.Clamp(fit, 0.25, 6);
        StateHasChanged();
        await Task.Delay(50);
        await ScrollToSpotAsync(page, top / ViewSize(page).H * 100);
        Status($"Zoomed to {Math.Round(_zoom * 100)}%");
    }

    private async Task ZoomByAsync(double factor)
    {
        _zoom = Math.Clamp(_zoom * factor, 0.25, 6);
        StateHasChanged();
        await Task.CompletedTask;
    }

    // ── Writing them into the PDF ────────────────────────────────────────────

    private IEnumerable<ShapeAnnotation> ToolboxShapes()
    {
        foreach (var i in _items)
        {
            var (l, b, w, h) = ToUser(i.Page, i.Left, i.Top, i.Width, i.Height);
            if (i.Kind == ItemKind.Cloud)
                yield return new ShapeAnnotation
                {
                    PageNumber = i.Page + 1, Kind = ShapeKind.Cloud, X1 = l, Y1 = b, X2 = l + w, Y2 = b + h,
                    StrokeColor = i.Color, FillColor = "", LineWidth = 1.5, Comment = CommentFor(i),
                };
            else if (i.Kind == ItemKind.Callout && !string.IsNullOrWhiteSpace(i.Text))
            {
                var tip = i.Points is { Count: > 0 } p ? ToUserPoint(i.Page, p[0].X, p[0].Y) : new PointD(l, b - 20);
                yield return new ShapeAnnotation
                {
                    PageNumber = i.Page + 1, Kind = ShapeKind.Callout, X1 = l, Y1 = b, X2 = l + w, Y2 = b + h,
                    StrokeColor = i.Color, FillColor = "#FFFDE7", LineWidth = 1, CalloutText = i.Text, Points = [tip],
                    Comment = CommentFor(i, i.Text),
                };
            }
        }
    }

    private IEnumerable<TextEditMark> ToolboxTextEdits() =>
        _items.Where(i => i.Kind is ItemKind.InsertText or ItemKind.ReplaceText).Select(i =>
        {
            var (l, b, w, h) = ToUser(i.Page, i.Left, i.Top, i.Width, i.Height);
            return new TextEditMark
            {
                PageNumber = i.Page + 1, Kind = i.Kind == ItemKind.InsertText ? TextEditKind.Insert : TextEditKind.Replace,
                Left = l, Bottom = b, Width = w, Height = h, Color = i.Color,
                Comment = CommentFor(i, i.Text),
            };
        });

    // The Windows app's single-letter tool shortcuts.
    private async Task ToolboxKeyAsync(char key)
    {
        if (DesignMode)
        {
            DesignTool? d = key switch
            {
                'v' => DesignTool.Select, 't' => DesignTool.Text, 'r' => DesignTool.Rectangle, 'e' => DesignTool.Ellipse,
                'l' => DesignTool.Line, 'a' => DesignTool.Arrow, 'p' => DesignTool.Pen, 'b' => DesignTool.Table,
                'k' => DesignTool.Check, 'x' => DesignTool.Cross, _ => null,
            };
            if (d is { } dt) StartDesignTool(dt);
            else if (key == 'i') _dialog = DialogKind.DesignPicture;
            else if (key == 's') AddDesignSignature();
            else return;
            StateHasChanged();
            return;
        }
        if (Doc == null) return;
        switch (key)
        {
            case 'v' or 'f': SetTool(Tool.Select); break;
            case 'h': SetTool(Tool.Hand); break;
            case 'z': SetTool(Tool.Zoom); break;
            case 't': SetTool(Tool.Text); break;
            case 'i': SetTool(Tool.Highlight); break;
            case 'w': SetTool(Tool.Ink); break;
            case 'm': SetTool(Tool.Stamp); break;
            case 'd': SetTool(Tool.Date); break;
            case 's': await SignatureToolAsync(); break;
            case 'e': TogglePrepareMode(); break;
            default: return;
        }
        StateHasChanged();
    }

    // ── Page tools beside the toolbox ────────────────────────────────────────

    public Task RotatePageClockwiseAsync() => RotateAsync(90);
    public Task InsertBlankAfterAsync() => InsertBlankAsync(before: false);
    public Task DeleteThisPageAsync() => PageCount < 2 ? ToastAsync("The last page can't be deleted.") : DeletePageAsync();
    public Task ExtractThisPageAsync() => ExtractRangeAsync((_page + 1).ToString());

    private Task ToastAsync(string text) { Toast(text, "error"); return Task.CompletedTask; }
}
