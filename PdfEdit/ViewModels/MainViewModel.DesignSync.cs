using System.IO;
using System.Windows;
using System.Windows.Media;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>
/// Keeps the Design canvas and Live View in step for everything you add, not only existing fields:
/// <list type="bullet">
/// <item>Design → Live (switching to Live, or saving in Design): new form fields are written into
/// the PDF; shapes, text, ✓ ✕ marks, drawings and images become Live View annotations.</item>
/// <item>Live → Design (switching to Design): annotations added or changed in Live View appear on
/// the canvas.</item>
/// <item>Each element remembers the Live object it stands for (<see cref="DesignElement.LiveId"/>),
/// so going back and forth updates it instead of adding copies, and deleting it on one side
/// deletes it on the other.</item>
/// </list>
/// Elements reconstructed from the page's own content (<see cref="DesignElement.IsFromPage"/>) are
/// the page itself and are never added as annotations.
/// </summary>
public partial class MainViewModel
{
    // Live annotation ids the Design canvas showed / created for its source page (see signatures).
    private readonly HashSet<string> _designLiveIds = new();

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color FromHex(string? hex, Color fallback)
    {
        try { return string.IsNullOrEmpty(hex) ? fallback : (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    private static IEnumerable<string> Ids(string? liveId) =>
        (liveId ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsLiveShapeKind(ShapeKind k) => k is ShapeKind.Rectangle or ShapeKind.Ellipse
        or ShapeKind.Line or ShapeKind.Arrow;

    // ── Design → Live ────────────────────────────────────────────────────────

    /// <summary>Adds / updates / removes the Live annotations for the Design canvas's own elements.</summary>
    private void SyncDesignAnnotationsToLive(int pageIndex, double pageH)
    {
        int pageNum = pageIndex + 1;
        var claimed = new HashSet<string>();
        var current = new HashSet<string>();

        // A pasted copy shares its original's link: give it its own annotation.
        string? Claim(string? id) => id != null && claimed.Add(id) ? id : null;

        foreach (var el in DesignCanvas.Elements.ToList())
        {
            if (el.IsFromPage) continue;
            switch (el)
            {
                case ShapeDesignElement sh when sh.ElementType is DesignElementType.Rectangle or DesignElementType.Ellipse
                                                  or DesignElementType.Line or DesignElementType.Arrow:
                {
                    var shape = Claim(el.LiveId) is { } id ? ShapeAnnotations.FirstOrDefault(s => s.Comment.Id == id) : null;
                    if (shape == null)
                    {
                        shape = new ShapeAnnotation();
                        ShapeAnnotations.Add(shape);
                        shape.PageNumber = pageNum;
                    }
                    ShapeFromDesign(sh, shape, pageH);
                    el.LiveId = shape.Comment.Id;
                    claimed.Add(shape.Comment.Id);
                    current.Add(shape.Comment.Id);
                    break;
                }
                case TextDesignElement t:
                {
                    var ann = Claim(el.LiveId) is { } id ? FreeTextAnnotations.FirstOrDefault(a => a.Comment.Id == id) : null;
                    if (ann == null)
                    {
                        ann = new FreeTextAnnotation { PageNumber = pageNum };
                        FreeTextAnnotations.Add(ann);
                    }
                    TextFromDesign(t, ann, pageH);
                    el.LiveId = ann.Comment.Id;
                    claimed.Add(ann.Comment.Id);
                    current.Add(ann.Comment.Id);
                    break;
                }
                case FreehandDesignElement fh:
                {
                    // One ink annotation per stroke; rebuilt each time (strokes can't be edited one by one).
                    foreach (var old in Ids(Claim(el.LiveId) == null ? null : el.LiveId))
                        if (FreeTextAnnotations.FirstOrDefault(a => a.Comment.Id == old) is { } o) FreeTextAnnotations.Remove(o);
                    var ids = new List<string>();
                    foreach (var stroke in fh.GetTransformedStrokes())
                    {
                        var pts = stroke.Select(p => new Point(p.X, pageH - p.Y)).ToList();
                        if (pts.Count == 0) continue;
                        var ink = new FreeTextAnnotation
                        {
                            PageNumber = pageNum,
                            Text = Controls.PdfViewerControl.EncodeInk(ToHex(fh.Color), fh.Thickness, pts),
                            FontSize = 0, FontFamily = "Ink", FontColor = ToHex(fh.Color),
                        };
                        double pad = fh.Thickness / 2;
                        ink.Left = pts.Min(p => p.X) - pad; ink.Bottom = pts.Min(p => p.Y) - pad;
                        ink.Width = Math.Max(2, pts.Max(p => p.X) - pts.Min(p => p.X) + fh.Thickness);
                        ink.Height = Math.Max(2, pts.Max(p => p.Y) - pts.Min(p => p.Y) + fh.Thickness);
                        FreeTextAnnotations.Add(ink);
                        ids.Add(ink.Comment.Id);
                    }
                    el.LiveId = string.Join(",", ids);
                    foreach (var id in ids) { claimed.Add(id); current.Add(id); }
                    break;
                }
                case ImageDesignElement img when img.SignatureBytes == null && img.FilePath.Length > 0 && File.Exists(img.FilePath):
                    // A picture added in Design is placed on the page in Live View (like a signature image).
                    try { img.SignatureBytes = File.ReadAllBytes(img.FilePath); } catch { }
                    break;
                case FormFieldDesignElement { LabelLiveId: { } labelId } ff:
                    current.Add(labelId);
                    claimed.Add(labelId);
                    UpdateFieldLabel(ff, pageH);
                    break;
            }
        }

        // Deleted in Design → delete in Live (only what the canvas itself put there / showed).
        foreach (var id in _designLiveIds.Where(id => !current.Contains(id)).ToList())
        {
            if (ShapeAnnotations.FirstOrDefault(s => s.Comment.Id == id) is { } s) ShapeAnnotations.Remove(s);
            if (FreeTextAnnotations.FirstOrDefault(a => a.Comment.Id == id) is { } a) FreeTextAnnotations.Remove(a);
        }
        _designLiveIds.Clear();
        _designLiveIds.UnionWith(current);
    }

    private static void ShapeFromDesign(ShapeDesignElement sh, ShapeAnnotation shape, double pageH)
    {
        shape.Kind = sh.ElementType switch
        {
            DesignElementType.Ellipse => ShapeKind.Ellipse,
            DesignElementType.Line => ShapeKind.Line,
            DesignElementType.Arrow => ShapeKind.Arrow,
            _ => ShapeKind.Rectangle,
        };
        if (shape.Kind is ShapeKind.Line or ShapeKind.Arrow)
        {
            var (a, b) = sh.GetLocalEndpoints();
            shape.X1 = sh.X + a.X; shape.Y1 = pageH - (sh.Y + a.Y);
            shape.X2 = sh.X + b.X; shape.Y2 = pageH - (sh.Y + b.Y);
        }
        else
        {
            shape.X1 = sh.X; shape.X2 = sh.X + sh.Width;
            shape.Y1 = pageH - sh.Y - sh.Height; shape.Y2 = pageH - sh.Y;
        }
        shape.StrokeColor = ToHex(sh.StrokeColor);
        shape.LineWidth = sh.StrokeThickness;
        shape.FillColor = sh.FillColor.A == 0 ? "" : ToHex(sh.FillColor);
        shape.Opacity = sh.Opacity;
    }

    private static void TextFromDesign(TextDesignElement t, FreeTextAnnotation ann, double pageH)
    {
        ann.Left = t.X; ann.Width = t.Width; ann.Height = t.Height;
        ann.Bottom = pageH - t.Y - t.Height;
        ann.Text = MarkShapes.FromGlyph(t.Text.Trim()) != null ? t.Text.Trim() : t.Text;
        ann.FontSize = t.FontSize;
        ann.FontFamily = t.FontFamily;
        ann.IsBold = t.Bold; ann.IsItalic = t.Italic; ann.IsUnderline = t.Underline;
        ann.FontColor = ToHex(t.Color);
        ann.TextAlignment = t.Alignment;
        ann.AutoSize = false;
    }

    /// <summary>New fields drawn in Design are written into the PDF (like Live View's Add field).</summary>
    private void CreateDesignFieldsInPdf(int pageIndex, double pageH)
    {
        if (_currentFilePath == null) return;
        var seen = new HashSet<(string, int)>();
        var newFields = new List<FormFieldDesignElement>();
        foreach (var f in DesignCanvas.Elements.OfType<FormFieldDesignElement>())
        {
            // A pasted copy of an existing field becomes a new field of its own.
            if (f.SourceFieldName != null && !seen.Add((f.SourceFieldName, f.SourceWidgetIndex)))
                f.SourceFieldName = null;
            if (f.SourceFieldName == null) newFields.Add(f);
        }
        if (newFields.Count == 0) return;

        string src = _currentFilePath, tmp = src + ".design.tmp";
        int pageNum = pageIndex + 1;
        var svc = new PdfFormService();
        var created = new List<(FormFieldDesignElement El, string Name, int Widget)>();
        try
        {
            foreach (var f in newFields)
            {
                // The field box, leaving room for a caption drawn to its left / right.
                double labelW = f.LabelPosition is FieldLabelPosition.Left or FieldLabelPosition.Right && f.Label.Length > 0
                    ? Math.Min(f.Width * 0.4, f.Label.Length * 5.5 + 8) : 0;
                double gap = labelW > 0 ? f.LabelOffset : 0;
                double fx = f.LabelPosition == FieldLabelPosition.Left ? f.X + labelW + gap : f.X;
                double fw = Math.Max(8, f.Width - labelW - gap);
                float l = (float)fx, b = (float)(pageH - f.Y - f.Height), w = (float)fw, h = (float)f.Height;

                string name;
                int widget = 0;
                if (f.FieldKind == FormFieldKind.Radio)
                {
                    name = string.IsNullOrWhiteSpace(f.FieldName) ? NextFieldName("Group") : f.FieldName.Trim();
                    widget = AllFields.Count(x => x.Name == name) + created.Count(c => c.Name == name);
                    if (f.ExportValue is "Yes" or "") f.ExportValue = f.Label is { Length: > 0 } lb && lb != name ? lb : $"Choice{widget + 1}";
                    svc.AddRadioButtonField(src, tmp, pageNum, l, b, Math.Min(w, h), name, f.ExportValue);
                }
                else
                {
                    name = string.IsNullOrWhiteSpace(f.FieldName) ? f.FieldKind.ToString() : f.FieldName.Trim();
                    if (AllFields.Any(x => x.Name == name) || created.Any(c => c.Name == name))
                        name = NextFieldName(name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'));
                    switch (f.FieldKind)
                    {
                        case FormFieldKind.Checkbox:  svc.AddCheckboxField(src, tmp, pageNum, l, b, Math.Min(w, h), name); break;
                        case FormFieldKind.ComboBox:  svc.AddComboBoxField(src, tmp, pageNum, l, b, w, h, name, f.Options); break;
                        case FormFieldKind.Signature: svc.AddSignatureField(src, tmp, pageNum, l, b, w, h, name); break;
                        default:                      svc.AddTextFormField(src, tmp, pageNum, l, b, w, h, name); break;
                    }
                }
                File.Copy(tmp, src, overwrite: true);
                created.Add((f, name, widget));
            }
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Could not add the new Design fields to the PDF.", ex);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        if (created.Count == 0) return;

        foreach (var (el, name, widget) in created)
        {
            el.SourceFieldName = name;
            el.SourceWidgetIndex = widget;
            el.SourcePageNumber = pageNum;
            el.FieldName = name;
            if (el.HasValue) FieldValues[name] = el.Value;
            if (el.LabelPosition is FieldLabelPosition.Left or FieldLabelPosition.Right && el.Label.Length > 0)
                UpdateFieldLabel(el, pageH);
        }
        StatusText = $"{created.Count} new field(s) from Design added to the form.";
        // Reload so Live View shows the new fields, then apply what can't be set at creation. Queued
        // so the rest of the Design → Live sync (annotations) is in the saved state first.
        var list = created.Select(c => (c.El, c.Name)).ToList();
        Application.Current?.Dispatcher.BeginInvoke(new Func<Task>(() => ReloadAfterDesignFieldsAsync(list)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private async Task ReloadAfterDesignFieldsAsync(List<(FormFieldDesignElement El, string Name)> created)
    {
        bool wasDesign = IsDesignMode;          // e.g. saved while in Design: stay there
        await ReloadCurrentFileAsync();
        if (wasDesign) IsDesignMode = true;
        foreach (var (el, name) in created)
        {
            foreach (var w in AllFields.Where(x => x.Name == name))
            {
                if (el.FieldKind == FormFieldKind.Memo) w.IsMultiline = true;
                w.IsRequired = el.Required;
                if (el.FieldKind == FormFieldKind.ComboBox && w.Options.Count == 0) w.Options = el.Options.ToList();
            }
            ModifiedFieldNames.Add(name);
            if (el.HasValue) UpdateFieldValue(name, el.Value);
        }
        PageChanged?.Invoke();
    }

    /// <summary>A new field's Left / Right caption, kept as a linked Live View text annotation.</summary>
    private void UpdateFieldLabel(FormFieldDesignElement f, double pageH)
    {
        bool hasLabel = f.LabelPosition is FieldLabelPosition.Left or FieldLabelPosition.Right && f.Label.Length > 0;
        var ann = f.LabelLiveId is { } id ? FreeTextAnnotations.FirstOrDefault(a => a.Comment.Id == id) : null;
        if (!hasLabel)
        {
            if (ann != null) FreeTextAnnotations.Remove(ann);
            f.LabelLiveId = null;
            return;
        }
        if (ann == null)
        {
            ann = new FreeTextAnnotation { PageNumber = f.SourcePageNumber > 0 ? f.SourcePageNumber : _currentPageIndex + 1 };
            FreeTextAnnotations.Add(ann);
            f.LabelLiveId = ann.Comment.Id;
        }
        double labelW = Math.Min(f.Width * 0.4, f.Label.Length * 5.5 + 8);
        ann.Text = f.Label;
        ann.FontSize = 10;
        ann.FontFamily = "Arial";
        ann.FontColor = "#000000";
        ann.Width = labelW;
        ann.Height = Math.Min(f.Height, 16);
        ann.Left = f.LabelPosition == FieldLabelPosition.Left ? f.X : f.X + f.Width - labelW;
        ann.Bottom = pageH - f.Y - f.Height / 2 - ann.Height / 2;
        _designLiveIds.Add(ann.Comment.Id);
    }

    // ── Live → Design ────────────────────────────────────────────────────────

    /// <summary>Shows Live View's shapes, text, marks and drawings for the page on the canvas.</summary>
    private void SyncLiveAnnotationsToDesign(int pageIndex, double pageH)
    {
        int pageNum = pageIndex + 1;
        var labelIds = DesignCanvas.Elements.OfType<FormFieldDesignElement>()
            .Select(f => f.LabelLiveId).Where(i => i != null).ToHashSet();
        var byId = new Dictionary<string, DesignElement>();
        foreach (var el in DesignCanvas.Elements)
            foreach (var id in Ids(el.LiveId))
                byId.TryAdd(id, el);

        var liveIds = new HashSet<string>();

        foreach (var shape in ShapeAnnotations.Where(s => s.PageNumber == pageNum && IsLiveShapeKind(s.Kind)).ToList())
        {
            liveIds.Add(shape.Comment.Id);
            if (byId.TryGetValue(shape.Comment.Id, out var el) && el is ShapeDesignElement sh) DesignFromShape(shape, sh, pageH);
            else
            {
                var created = new ShapeDesignElement(shape.Kind switch
                {
                    ShapeKind.Ellipse => DesignElementType.Ellipse,
                    ShapeKind.Line => DesignElementType.Line,
                    ShapeKind.Arrow => DesignElementType.Arrow,
                    _ => DesignElementType.Rectangle,
                }) { LiveId = shape.Comment.Id, ZOrder = DesignCanvas.Elements.Count };
                DesignFromShape(shape, created, pageH);
                DesignCanvas.Elements.Add(created);
            }
        }

        foreach (var ann in FreeTextAnnotations.Where(a => a.PageNumber == pageNum && !a.IsHighlight).ToList())
        {
            if (labelIds.Contains(ann.Comment.Id)) { liveIds.Add(ann.Comment.Id); continue; }
            liveIds.Add(ann.Comment.Id);
            byId.TryGetValue(ann.Comment.Id, out var el);
            if (Controls.PdfViewerControl.ParseInk(ann.Text) is { } ink)
            {
                // Strokes are rebuilt from Design each time; only add ones that came from Live View.
                if (el != null) continue;
                var pts = ink.Points.Select(p => new Point(p.X, pageH - p.Y)).ToList();
                if (pts.Count == 0) continue;
                double minX = pts.Min(p => p.X), minY = pts.Min(p => p.Y);
                DesignCanvas.Elements.Add(new FreehandDesignElement
                {
                    Strokes = new List<List<Point>> { pts },
                    X = minX, Y = minY,
                    Width = Math.Max(4, pts.Max(p => p.X) - minX), Height = Math.Max(4, pts.Max(p => p.Y) - minY),
                    Color = FromHex(ink.Color, Colors.Black), Thickness = ink.Width,
                    LiveId = ann.Comment.Id, ZOrder = DesignCanvas.Elements.Count,
                });
            }
            else if (el is TextDesignElement t) DesignFromText(ann, t, pageH);
            else if (el == null)
            {
                var t2 = new TextDesignElement { LiveId = ann.Comment.Id, ZOrder = DesignCanvas.Elements.Count };
                DesignFromText(ann, t2, pageH);
                DesignCanvas.Elements.Add(t2);
            }
        }

        // Removed in Live View → remove from the canvas (only what the canvas knew was in Live).
        foreach (var el in DesignCanvas.Elements.Where(e => !e.IsFromPage && e.LiveId != null).ToList())
        {
            var ids = Ids(el.LiveId).ToList();
            if (ids.Count > 0 && ids.All(id => !liveIds.Contains(id) && _designLiveIds.Contains(id)))
                DesignCanvas.Elements.Remove(el);
        }
        _designLiveIds.Clear();
        _designLiveIds.UnionWith(liveIds);
    }

    private static void DesignFromShape(ShapeAnnotation s, ShapeDesignElement sh, double pageH)
    {
        if (s.Kind is ShapeKind.Line or ShapeKind.Arrow)
        {
            double y1 = pageH - s.Y1, y2 = pageH - s.Y2;   // design y runs down
            sh.X = Math.Min(s.X1, s.X2); sh.Y = Math.Min(y1, y2);
            sh.Width = Math.Max(1, Math.Abs(s.X2 - s.X1)); sh.Height = Math.Max(1, Math.Abs(y2 - y1));
            sh.FlipX = s.X2 < s.X1;
            sh.FlipY = y2 < y1;
        }
        else
        {
            sh.X = Math.Min(s.X1, s.X2); sh.Y = pageH - Math.Max(s.Y1, s.Y2);
            sh.Width = Math.Abs(s.X2 - s.X1); sh.Height = Math.Abs(s.Y2 - s.Y1);
        }
        var stroke = FromHex(s.StrokeColor, Colors.Red);
        sh.StrokeColor = stroke;
        sh.StrokeThickness = s.LineWidth;
        sh.FillColor = s.FillColor == "" ? Colors.Transparent
            : string.IsNullOrEmpty(s.FillColor) ? Color.FromArgb(30, stroke.R, stroke.G, stroke.B)
            : FromHex(s.FillColor, Colors.Transparent);
        sh.Opacity = s.Opacity;
    }

    private static void DesignFromText(FreeTextAnnotation a, TextDesignElement t, double pageH)
    {
        t.X = a.Left; t.Y = pageH - a.Bottom - a.Height;
        t.Width = Math.Max(4, a.Width); t.Height = Math.Max(4, a.Height);
        t.Text = a.Text;
        t.FontSize = a.FontSize > 0 ? a.FontSize : 12;
        t.FontFamily = a.FontFamily;
        t.Bold = a.IsBold; t.Italic = a.IsItalic; t.Underline = a.IsUnderline;
        t.Color = FromHex(a.FontColor, Colors.Black);
        t.Alignment = MarkShapes.FromGlyph(a.Text) != null ? TextAlignment.Center : a.TextAlignment;
        t.Wrap = !a.AutoSize;
    }
}
