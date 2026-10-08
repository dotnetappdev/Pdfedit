using System.Globalization;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Models;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// Things added with PdfEdit stay editable after they're written into the PDF, as in the Windows app:
/// PdfEdit's own annotations (/NM "pdfedit:&lt;id&gt;") are read back as page items — selectable, movable,
/// with the item toolbar — and the page image is drawn without them. Only new, changed or removed
/// items are written again; a re-written annotation replaces its old copy by id.
/// </summary>
public partial class Editor
{
    /// <summary>The annotation's comment, carrying the item's id so a later save replaces it.</summary>
    private CommentInfo CommentFor(PageItem i, string? note = null, string? author = null) =>
        new() { Id = i.Id, Author = author ?? i.Author ?? "PdfEdit web", Note = note ?? "" };

    /// <summary>Everything about an item that's written into the PDF.</summary>
    private static string Signature(PageItem i) => string.Join('|', new object?[]
    {
        i.Kind, i.Page, R(i.Left), R(i.Top), R(i.Width), R(i.Height), i.Text, R(i.FontSize), i.Color, i.Subtitle, R(i.LineWidth),
        i.Markup, i.Rotation, R(i.CharSpacing), i.Fit, i.Bold, i.Italic, i.Underline, i.Upper, i.Align, R(i.Opacity), i.DateFormat,
        i.Points == null ? "" : string.Join(';', i.Points.Select(p => R(p.X) + "," + R(p.Y))), i.Unit, i.Locked,
    });

    private static string R(double v) => Math.Round(v, 2).ToString(CultureInfo.InvariantCulture);

    public static bool IsDirty(PageItem i) => i.Baseline == null || Signature(i) != i.Baseline;

    /// <summary>Items not written into the PDF yet: added, or changed since they were read back.</summary>
    public IEnumerable<PageItem> PendingItems => _items.Where(IsDirty);

    /// <summary>Items read back from the PDF and not changed.</summary>
    public IEnumerable<PageItem> SavedItems => _items.Where(i => !IsDirty(i));

    /// <summary>PdfEdit annotations in the PDF whose items have been deleted from the page.</summary>
    private HashSet<string> RemovedOwnIds()
    {
        if (Doc == null) return [];
        var here = _items.Select(i => i.Id).ToHashSet();
        return Doc.Own.Select(o => o.Id).Where(id => !here.Contains(id)).ToHashSet();
    }

    /// <summary>After the document is (re)loaded: its own annotations as page items again.</summary>
    private void SyncOwnItems()
    {
        _items.RemoveAll(i => i.Baseline != null);
        if (Doc == null) return;
        var known = _items.Select(i => i.Id).ToHashSet();
        foreach (var o in Doc.Own)
        {
            if (known.Contains(o.Id) || ItemFrom(o) is not { } item) continue;
            item.Baseline = Signature(item);
            _items.Add(item);
        }
    }

    /// <summary>A point in the PDF's coordinates as a point on the seen page.</summary>
    private PointD ToViewPoint(int page, double ux, double uy)
    {
        var v = ToView(page, ux, uy, 0, 0);
        return new PointD(v.Left, v.Top);
    }

    private PageItem? ItemFrom(OwnAnnotation o)
    {
        int page = o.PageNumber - 1;
        if (page < 0 || page >= PageCount) return null;
        var v = ToView(page, o.Left, o.Bottom, o.Width, o.Height);
        // /Rotate holds how far the content is turned against the page; items keep their turn as seen.
        int turn = ((Rotation(page) - o.Rotate) % 360 + 360) % 360;
        var item = o.Kind switch
        {
            "Text" => new PageItem
            {
                Kind = ItemKind.Text, Text = o.Text, FontSize = o.FontSize, Color = o.Colour, Bold = o.Bold, Italic = o.Italic,
                CharSpacing = o.CharSpacing, Rotation = turn, Fit = TextFit.Wrap,
            },
            "Mark" => new PageItem { Kind = ItemKind.Mark, Text = o.Text.Trim(), Color = o.Colour, FontSize = Math.Min(v.Width, v.Height), Rotation = turn },
            "Stamp" => new PageItem { Kind = ItemKind.Stamp, Text = o.Text, Subtitle = o.Subtitle, Color = o.Colour, Rotation = turn },
            "Ink" => new PageItem
            {
                Kind = ItemKind.Ink, Color = o.Colour, LineWidth = o.LineWidth,
                Points = o.Points!.Select(p => ToViewPoint(page, p.X, p.Y)).ToList(),
            },
            "Highlight" => new PageItem { Kind = ItemKind.Highlight, Color = o.Colour, Markup = o.Markup, Opacity = o.Opacity },
            "Note" => new PageItem { Kind = ItemKind.Note, Text = o.Text, Color = o.Colour },
            "Rectangle" => new PageItem { Kind = ItemKind.Rectangle, Color = o.Colour, LineWidth = o.LineWidth },
            "Ellipse" => new PageItem { Kind = ItemKind.Ellipse, Color = o.Colour, LineWidth = o.LineWidth },
            "Line" or "Arrow" or "Distance" or "Polygon" or "Area" or "Polyline" or "Perimeter" => new PageItem
            {
                Kind = Enum.Parse<ItemKind>(o.Kind), Color = o.Colour, LineWidth = o.LineWidth, Unit = o.Unit,
                Points = o.Points!.Select(p => ToViewPoint(page, p.X, p.Y)).ToList(),
            },
            "Cloud" => new PageItem { Kind = ItemKind.Cloud, Color = o.Colour, LineWidth = o.LineWidth },
            "Callout" => new PageItem
            {
                Kind = ItemKind.Callout, Text = o.Text, Color = o.Colour, FontSize = 10,
                Points = o.Tip is { } tip ? [ToViewPoint(page, tip.X, tip.Y)] : null,
            },
            "Insert" => new PageItem { Kind = ItemKind.InsertText, Text = o.Text, Color = o.Colour },
            "Replace" => new PageItem { Kind = ItemKind.ReplaceText, Text = o.Text, Color = o.Colour },
            _ => null,
        };
        if (item == null) return null;
        var placed = new PageItem
        {
            Id = o.Id, Kind = item.Kind, Page = page, Left = v.Left, Top = v.Top, Width = v.Width, Height = v.Height,
            Text = item.Text, FontSize = item.FontSize, Color = item.Color, Subtitle = item.Subtitle, Points = item.Points,
            LineWidth = item.LineWidth, Markup = item.Markup, Rotation = item.Rotation, CharSpacing = item.CharSpacing,
            Fit = item.Fit, Bold = item.Bold, Italic = item.Italic, Opacity = item.Opacity,
            Unit = item.Unit, Author = string.IsNullOrWhiteSpace(o.Author) ? null : o.Author,
            Locked = o.Kind == "Text" && o.Locked,
        };
        // Lines and measurements: the box is worked out from the points, as when they're drawn.
        if (placed.Kind is ItemKind.Ink or ItemKind.Line or ItemKind.Arrow or ItemKind.Distance or ItemKind.Polygon
            or ItemKind.Area or ItemKind.Polyline or ItemKind.Perimeter && placed.Points is { Count: > 1 })
            FitSketchBox(placed);
        if (placed.Kind == ItemKind.Note)
        {
            // A note is drawn from its top-left corner: the 20-point icon's PDF box starts there.
            var corner = ToViewPoint(page, o.Left, o.Bottom);
            (placed.Left, placed.Top, placed.Width, placed.Height) = (corner.X, corner.Y, 20, 20);
        }
        return placed;
    }
}
