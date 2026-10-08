using System.Globalization;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Models;

namespace PdfEdit.Blazor.Components.Pages;

// Stamps, freehand ink, lines/arrows and the Measure tools (distance, perimeter, area).
public partial class Editor
{
    private string _stampKey = StampCatalog.KeyOf(StampCatalog.BuiltIn[0]);
    private string _stampAuthor = "";
    private string _measureUnit = "in";
    private double _inkWidth = 2;
    private string? _timeZone;

    public IEnumerable<IGrouping<string, StampDefinition>> StampGroups => AllStamps.GroupBy(d => d.Category);

    private StampDefinition SelectedStamp =>
        AllStamps.FirstOrDefault(d => StampCatalog.KeyOf(d) == _stampKey) ?? StampCatalog.BuiltIn[0];

    public string StampKey => _stampKey;
    public string MeasureUnit { get => _measureUnit; set => _measureUnit = value; }

    public void PickStamp(string key)
    {
        _stampKey = key;
        SetTool(Tool.Stamp);
    }

    /// <summary>How the page's tool layer collects points for a drawing tool (JS draws it live), or null.</summary>
    private string? SketchMode => _tool switch
    {
        Tool.Ink => "ink",
        Tool.Line or Tool.Arrow or Tool.Distance => "line",
        Tool.Perimeter or Tool.Polyline => "poly",
        Tool.Area or Tool.Polygon => "closed",
        Tool.Hand => "pan",
        Tool.Zoom => "zoom",
        _ => null,
    };

    private string? MeasureMode => _tool switch { Tool.Area => "area", Tool.Distance or Tool.Perimeter => "length", _ => null };
    private double SketchWidth => _tool is Tool.Distance or Tool.Perimeter or Tool.Area ? 1.5 : _inkWidth;

    private string SketchColor => _tool is Tool.Distance or Tool.Perimeter or Tool.Area ? MeasureColor : _strokeColor;
    private const string MeasureColor = "#1F4FB5";

    private async Task<PageItem> MakeStampAsync(int page, double x, double y)
    {
        var def = SelectedStamp;
        string? subtitle = null;
        if (def.Dynamic)
        {
            var now = await BrowserNowAsync();
            string who = string.IsNullOrWhiteSpace(_stampAuthor) ? "" : $"By {_stampAuthor.Trim()} ";
            subtitle = $"{who}at {now.ToString("t", CultureInfo.CurrentCulture)}, {now.ToString("d", CultureInfo.CurrentCulture)}";
            subtitle = char.ToUpper(subtitle[0]) + subtitle[1..];
        }
        var (w, h) = StampCatalog.SizeFor(def.Title, subtitle);
        return new PageItem
        {
            Kind = ItemKind.Stamp, Page = page, Left = x - w / 2, Top = y - h / 2, Width = w, Height = h,
            Text = def.Title, Subtitle = subtitle, Color = def.Color,
        };
    }

    // Dynamic stamps show the time where the user is, not where the server is.
    private async Task<DateTime> BrowserNowAsync()
    {
        try
        {
            _timeZone ??= await JS.InvokeAsync<string>("pdfedit.timeZone");
            return TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(_timeZone));
        }
        catch
        {
            return DateTime.Now;
        }
    }

    /// <summary>
    /// A finished ink stroke, line or measurement from the page's tool layer: the points as
    /// percentages of the page (x, y, x, y …).
    /// </summary>
    [JSInvokable]
    public Task OnSketch(int page, double[] pct)
    {
        if (page < 0 && DesignMode) { AddDesignStroke(pct); return InvokeAsync(StateHasChanged); }
        if (Doc == null || pct.Length < 4) return Task.CompletedTask;
        var (pw, ph) = ViewSize(page);
        var pts = new List<PointD>();
        for (int i = 0; i + 1 < pct.Length; i += 2)
            pts.Add(new PointD(Math.Clamp(pct[i], 0, 100) / 100 * pw, Math.Clamp(pct[i + 1], 0, 100) / 100 * ph));
        if (_tool == Tool.Zoom)
            return InvokeAsync(() => ZoomToAsync(page, Math.Min(pts[0].X, pts[^1].X), Math.Min(pts[0].Y, pts[^1].Y),
                Math.Abs(pts[^1].X - pts[0].X), Math.Abs(pts[^1].Y - pts[0].Y)));

        var kind = _tool switch
        {
            Tool.Ink => ItemKind.Ink,
            Tool.Line => ItemKind.Line,
            Tool.Arrow => ItemKind.Arrow,
            Tool.Distance => ItemKind.Distance,
            Tool.Perimeter => ItemKind.Perimeter,
            Tool.Area => ItemKind.Area,
            Tool.Polygon => ItemKind.Polygon,
            Tool.Polyline => ItemKind.Polyline,
            _ => (ItemKind?)null,
        };
        if (kind == null) return Task.CompletedTask;
        if (kind is ItemKind.Line or ItemKind.Arrow or ItemKind.Distance) pts = [pts[0], pts[^1]];
        if (kind is ItemKind.Area or ItemKind.Polygon && pts.Count < 3) { Toast("Click at least three corners, then double-click to finish."); return InvokeAsync(StateHasChanged); }

        var item = new PageItem
        {
            Kind = kind.Value, Page = page, Points = pts, Unit = _measureUnit,
            Color = kind is ItemKind.Distance or ItemKind.Perimeter or ItemKind.Area ? MeasureColor : _strokeColor,
            LineWidth = kind == ItemKind.Ink ? _inkWidth : kind is ItemKind.Line or ItemKind.Arrow or ItemKind.Polygon or ItemKind.Polyline ? 2 : 1.5,
        };
        FitSketchBox(item);
        _items.Add(item);
        _page = page;
        Status(item.Describe() + " added — Apply Changes or Download writes it into the PDF");
        return InvokeAsync(StateHasChanged);
    }

    // The item's box: its points plus room for the line width, arrow head and measurement label.
    private void FitSketchBox(PageItem item)
    {
        var p = item.Points!;
        double pad = item.Kind switch
        {
            ItemKind.Arrow => 12 + item.LineWidth,
            ItemKind.Distance or ItemKind.Perimeter or ItemKind.Area => 14,
            _ => item.LineWidth + 4,
        };
        var (pw, ph) = ViewSize(item.Page);
        double l = Math.Max(0, p.Min(q => q.X) - pad), t = Math.Max(0, p.Min(q => q.Y) - pad);
        double r = Math.Min(pw, p.Max(q => q.X) + pad), b = Math.Min(ph, p.Max(q => q.Y) + pad);
        item.Left = l; item.Top = t;
        item.Width = Math.Max(4, r - l); item.Height = Math.Max(4, b - t);
    }

    /// <summary>After a drawing's box is moved or resized, move (and stretch) its points with it.</summary>
    private static void MoveSketchPoints(PageItem item, double oldLeft, double oldTop, double oldWidth, double oldHeight)
    {
        if (item.Points == null) return;
        double sx = oldWidth > 0 ? item.Width / oldWidth : 1, sy = oldHeight > 0 ? item.Height / oldHeight : 1;
        item.Points = item.Points.Select(p => new PointD(item.Left + (p.X - oldLeft) * sx, item.Top + (p.Y - oldTop) * sy)).ToList();
    }

    // ── Writing them into the PDF ────────────────────────────────────────────

    private IEnumerable<FreeTextAnnotation> StampAndInkAnnotations()
    {
        foreach (var i in _items)
        {
            var (l, b, w, h) = ToUser(i.Page, i.Left, i.Top, i.Width, i.Height);
            if (i.Kind == ItemKind.Stamp)
            {
                yield return new FreeTextAnnotation
                {
                    PageNumber = i.Page + 1, Left = l, Bottom = b, Width = w, Height = h, RotationAngle = i.Rotation - Rotation(i.Page),
                    Text = i.Text, IsStamp = true, StampSubtitle = i.Subtitle, FontColor = i.Color,
                    Comment = new CommentInfo { Author = string.IsNullOrWhiteSpace(_stampAuthor) ? "PdfEdit web" : _stampAuthor.Trim() },
                };
            }
            else if (i.Kind == ItemKind.Ink && i.Points is { Count: > 0 } pts)
            {
                // The Windows app's ink format: __INK__:#RRGGBB|width:x,y;x,y;… in PDF points.
                var inv = CultureInfo.InvariantCulture;
                string path = string.Join(';', pts.Select(p => ToUserPoint(i.Page, p.X, p.Y))
                                                   .Select(p => $"{p.X.ToString("0.##", inv)},{p.Y.ToString("0.##", inv)}"));
                yield return new FreeTextAnnotation
                {
                    PageNumber = i.Page + 1, Left = l, Bottom = b, Width = w, Height = h,
                    Text = $"__INK__:{i.Color.ToUpperInvariant()}|{i.LineWidth.ToString("0.##", inv)}:{path}",
                    Comment = new CommentInfo { Author = "PdfEdit web" },
                };
            }
        }
    }

    private IEnumerable<ShapeAnnotation> LineAndMeasureShapes()
    {
        foreach (var i in _items.Where(i => i.Points is { Count: > 1 } && i.Kind is ItemKind.Line or ItemKind.Arrow
                                             or ItemKind.Distance or ItemKind.Perimeter or ItemKind.Area or ItemKind.Polygon or ItemKind.Polyline))
        {
            var pts = i.Points!.Select(p => ToUserPoint(i.Page, p.X, p.Y)).ToList();
            bool poly = i.Kind is ItemKind.Perimeter or ItemKind.Area or ItemKind.Polygon or ItemKind.Polyline;
            var shape = new ShapeAnnotation
            {
                PageNumber = i.Page + 1,
                Kind = i.Kind switch
                {
                    ItemKind.Arrow => ShapeKind.Arrow,
                    ItemKind.Distance => ShapeKind.Distance,
                    ItemKind.Perimeter => ShapeKind.Perimeter,
                    ItemKind.Area => ShapeKind.Area,
                    ItemKind.Polygon => ShapeKind.Polygon,
                    ItemKind.Polyline => ShapeKind.Polyline,
                    _ => ShapeKind.Line,
                },
                X1 = poly ? pts.Min(p => p.X) : pts[0].X, Y1 = poly ? pts.Min(p => p.Y) : pts[0].Y,
                X2 = poly ? pts.Max(p => p.X) : pts[^1].X, Y2 = poly ? pts.Max(p => p.Y) : pts[^1].Y,
                Points = poly ? pts : null,
                StrokeColor = i.Color, LineWidth = i.LineWidth, FillColor = "", MeasureUnit = i.Unit,
                Comment = new CommentInfo { Author = "PdfEdit web" },
            };
            yield return shape;
        }
    }
}
