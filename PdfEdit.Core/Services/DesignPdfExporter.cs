using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Extgstate;
using iText.Layout;
using iText.Layout.Element;
using PdfEdit.Models;

namespace PdfEdit.Services;

/// <summary>
/// Turns a design-canvas page (<see cref="DesignDocument"/>) into a one-page PDF with real form
/// fields. The same drawing as the Windows app's DesignExportService, on the shared design format
/// so any front end can use it.
/// </summary>
public static class DesignPdfExporter
{
    public static void Export(DesignDocument design, string outputPath)
    {
        var (pageW, pageH) = design.Size;
        using var writer = new PdfWriter(outputPath);
        using var pdf = new PdfDocument(writer);
        var pageSize = new PageSize((float)pageW, (float)pageH);
        var page = pdf.AddNewPage(pageSize);

        var canvas = new PdfCanvas(page);
        var document = new Document(pdf, pageSize);
        document.SetMargins(0, 0, 0, 0);

        // Page background, when it isn't white.
        var bg = DesignColor.Parse(design.BgColor, (255, 255, 255, 255));
        if (bg.R != 255 || bg.G != 255 || bg.B != 255 || bg.A != 255)
        {
            canvas.SaveState()
                  .SetFillColor(new DeviceRgb(bg.R / 255f, bg.G / 255f, bg.B / 255f))
                  .Rectangle(0, 0, (float)pageW, (float)pageH).Fill()
                  .RestoreState();
        }

        // Let PDF viewers regenerate field appearances from their values.
        PdfAcroForm.GetAcroForm(pdf, true).SetNeedAppearances(true);

        var radioGroups = new Dictionary<string, (PdfButtonFormField Group, int ButtonCount)>();
        var usedNames = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in design.Elements ?? [])
            DrawItem(item, canvas, document, pdf, page, pageH, radioGroups, usedNames);

        // Option-button groups go on the page once all their buttons are in them: a group added
        // before its buttons leaves the buttons off the page.
        var form = PdfAcroForm.GetAcroForm(pdf, true);
        foreach (var (group, _) in radioGroups.Values)
            form.AddField(group, page);

        document.Close();
    }

    private static void DrawItem(DesignItem e, PdfCanvas canvas, Document doc, PdfDocument pdf, PdfPage page, double pageH,
        Dictionary<string, (PdfButtonFormField Group, int ButtonCount)> radioGroups, Dictionary<string, int> usedNames)
    {
        // Design origin is top-left (y down); PDF origin is bottom-left (y up).
        float x = (float)e.X, y = (float)(pageH - e.Y - e.H), w = (float)e.W, h = (float)e.H;

        if (e.Opacity < 0.999)
            canvas.SetExtGState(new PdfExtGState().SetFillOpacity((float)e.Opacity).SetStrokeOpacity((float)e.Opacity));

        switch (e.Type)
        {
            case "text" when MarkShapes.FromGlyph((e.Text ?? "").Trim()) is { } mark:
                // ✓ ✕ ● ○ — drawn as shapes: the standard PDF fonts have no such glyphs.
                DrawMark(mark, e.Color, canvas, x, y, w, h);
                break;
            case "text":
                DrawText(e, doc, x, y, w, h);
                break;
            case "shape":
                DrawShape(e, canvas, x, y, w, h);
                break;
            case "image":
                DrawImage(e, doc, x, y, w, h);
                break;
            case "freehand":
                DrawFreehand(e, canvas, pageH);
                break;
            case "table":
                DrawTable(e, canvas, doc, x, y, w, h);
                break;
            case "field":
                DrawField(e, doc, pdf, page, x, y, w, h, radioGroups, usedNames);
                break;
        }
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    private static void DrawText(DesignItem t, Document doc, float x, float y, float w, float h)
    {
        try
        {
            var fontName = t.Bold && t.Italic ? iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLDOBLIQUE
                         : t.Bold ? iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD
                         : t.Italic ? iText.IO.Font.Constants.StandardFonts.HELVETICA_OBLIQUE
                         : iText.IO.Font.Constants.StandardFonts.HELVETICA;
            // Wrap off: lay out on a much wider box so lines never break.
            float layoutW = t.Wrap ? w : Math.Max(w, 2000f);
            float size = (float)(t.FontSize > 0 ? t.FontSize : 14);
            var bg = DesignColor.Parse(t.BgColor);
            // iText leaves out a paragraph that doesn't fit its fixed box, so a box drawn a little
            // smaller than its text would lose the text. Keep the top edge and let the box reach
            // down as far as the text needs (to the page bottom when there's no background to show).
            int lines = Math.Max(1, (t.Text ?? "").Split('\n').Length);
            float boxH = bg.A > 0 ? Math.Max(h, lines * size * 1.35f) : y + h;
            var para = new Paragraph(t.Text ?? "")
                .SetFont(PdfFontFactory.CreateFont(fontName))
                .SetFontSize(size)
                .SetFontColor(Rgb(t.Color))
                .SetFixedPosition(x, y + h - boxH, layoutW)
                .SetHeight(boxH)
                .SetTextAlignment(Align(t.Alignment));
            if (t.Underline) para.SetUnderline();
            if (bg.A > 0) para.SetBackgroundColor(new DeviceRgb(bg.R, bg.G, bg.B), bg.A / 255f);
            doc.Add(para);
        }
        catch { /* skip text that can't be laid out */ }
    }

    // ── Shapes ───────────────────────────────────────────────────────────────

    private static void DrawShape(DesignItem s, PdfCanvas canvas, float x, float y, float w, float h)
    {
        canvas.SaveState();
        var fill = DesignColor.Parse(s.FillColor);
        var stroke = DesignColor.Parse(s.StrokeColor, (255, 0, 0, 0));
        double thick = s.StrokeThick > 0 ? s.StrokeThick : 2;
        bool hasFill = fill.A > 0, hasStroke = stroke.A > 0 && thick > 0;
        if (hasFill) canvas.SetFillColor(new DeviceRgb(fill.R, fill.G, fill.B));
        if (hasStroke) { canvas.SetStrokeColor(new DeviceRgb(stroke.R, stroke.G, stroke.B)); canvas.SetLineWidth((float)thick); }

        switch (s.ShapeType)
        {
            case "Rectangle":
                if (s.CornerRadius > 0) canvas.RoundRectangle(x, y, w, h, (float)Math.Min(s.CornerRadius, Math.Min(w, h) / 2));
                else canvas.Rectangle(x, y, w, h);
                FillStroke(canvas, hasFill, hasStroke);
                break;
            case "Ellipse":
                Ellipse(canvas, x + w / 2, y + h / 2, w / 2, h / 2);
                FillStroke(canvas, hasFill, hasStroke);
                break;
            case "Line":
            case "Arrow":
                // By default a line runs from the top-left to the bottom-right of its box; FlipX / FlipY mirror it.
                float x1 = s.FlipX ? x + w : x, y1 = s.FlipY ? y : y + h;
                float x2 = s.FlipX ? x : x + w, y2 = s.FlipY ? y + h : y;
                canvas.MoveTo(x1, y1).LineTo(x2, y2);
                if (s.ShapeType == "Arrow")
                {
                    double angle = Math.Atan2(y2 - y1, x2 - x1), al = Math.Max(10, thick * 4);
                    canvas.MoveTo(x2, y2).LineTo((float)(x2 - al * Math.Cos(angle - 0.4)), (float)(y2 - al * Math.Sin(angle - 0.4)));
                    canvas.MoveTo(x2, y2).LineTo((float)(x2 - al * Math.Cos(angle + 0.4)), (float)(y2 - al * Math.Sin(angle + 0.4)));
                }
                if (hasStroke) canvas.Stroke();
                break;
        }
        canvas.RestoreState();
    }

    private static void Ellipse(PdfCanvas canvas, float cx, float cy, float rx, float ry)
    {
        const float k = 0.5523f;   // Bézier handle ratio for a circle
        canvas.MoveTo(cx + rx, cy);
        canvas.CurveTo(cx + rx, cy + k * ry, cx + k * rx, cy + ry, cx, cy + ry);
        canvas.CurveTo(cx - k * rx, cy + ry, cx - rx, cy + k * ry, cx - rx, cy);
        canvas.CurveTo(cx - rx, cy - k * ry, cx - k * rx, cy - ry, cx, cy - ry);
        canvas.CurveTo(cx + k * rx, cy - ry, cx + rx, cy - k * ry, cx + rx, cy);
        canvas.ClosePath();
    }

    private static void FillStroke(PdfCanvas canvas, bool fill, bool stroke)
    {
        if (fill && stroke) canvas.FillStroke();
        else if (fill) canvas.Fill();
        else if (stroke) canvas.Stroke();
    }

    // ── Pictures ─────────────────────────────────────────────────────────────

    private static void DrawImage(DesignItem img, Document doc, float x, float y, float w, float h)
    {
        try
        {
            iText.IO.Image.ImageData data;
            if (!string.IsNullOrEmpty(img.Signature)) data = iText.IO.Image.ImageDataFactory.Create(Convert.FromBase64String(img.Signature));
            else if (!string.IsNullOrEmpty(img.FilePath) && File.Exists(img.FilePath)) data = iText.IO.Image.ImageDataFactory.Create(img.FilePath);
            else return;
            doc.Add(new Image(data).SetFixedPosition(x, y).ScaleToFit(w, h));
        }
        catch { /* missing or broken picture */ }
    }

    // ── Marks ────────────────────────────────────────────────────────────────

    private static void DrawMark(MarkShapes.Kind kind, string? color, PdfCanvas canvas, float x, float y, float w, float h)
    {
        bool fill = MarkShapes.FillsWidth(kind);
        float side = Math.Min(w, h);
        float sx = fill ? w : side, sy = fill ? h : side;
        float ox = x + (w - sx) / 2, oy = y + (h - sy) / 2;
        float X(double ux) => ox + (float)ux * sx;
        float Y(double uy) => oy + (1 - (float)uy) * sy;

        var c = Rgb(color);
        canvas.SaveState().SetStrokeColor(c).SetFillColor(c)
              .SetLineWidth((float)MarkShapes.StrokeWidth(kind) * Math.Min(sx, sy))
              .SetLineCapStyle(PdfCanvasConstants.LineCapStyle.ROUND)
              .SetLineJoinStyle(PdfCanvasConstants.LineJoinStyle.ROUND);
        foreach (var stroke in MarkShapes.Strokes(kind))
        {
            canvas.MoveTo(X(stroke[0].X), Y(stroke[0].Y));
            foreach (var pt in stroke.Skip(1)) canvas.LineTo(X(pt.X), Y(pt.Y));
            canvas.Stroke();
        }
        double r = MarkShapes.Radius(kind);
        if (r > 0)
        {
            canvas.Circle(X(0.5), Y(0.5), (float)r * side);
            if (kind == MarkShapes.Kind.Dot) canvas.Fill(); else canvas.Stroke();
        }
        canvas.RestoreState();
    }

    // ── Freehand ─────────────────────────────────────────────────────────────

    /// <summary>The strokes mapped into the element's current box (it may have been moved or resized since).</summary>
    public static IEnumerable<List<(double X, double Y)>> StrokesInBox(DesignItem fh)
    {
        var strokes = fh.Strokes ?? [];
        var all = strokes.SelectMany(s => s).ToList();
        if (all.Count == 0) yield break;
        double minX = all.Min(p => p.X), minY = all.Min(p => p.Y), maxX = all.Max(p => p.X), maxY = all.Max(p => p.Y);
        double sx = maxX - minX > 0.5 ? fh.W / (maxX - minX) : 1;
        double sy = maxY - minY > 0.5 ? fh.H / (maxY - minY) : 1;
        foreach (var s in strokes)
            yield return s.Select(p => (fh.X + (p.X - minX) * sx, fh.Y + (p.Y - minY) * sy)).ToList();
    }

    private static void DrawFreehand(DesignItem fh, PdfCanvas canvas, double pageH)
    {
        canvas.SaveState()
              .SetStrokeColor(Rgb(fh.Color))
              .SetLineWidth((float)(fh.Thickness > 0 ? fh.Thickness : 2))
              .SetLineCapStyle(PdfCanvasConstants.LineCapStyle.ROUND)
              .SetLineJoinStyle(PdfCanvasConstants.LineJoinStyle.ROUND);
        foreach (var pts in StrokesInBox(fh))
        {
            if (pts.Count == 0) continue;
            canvas.MoveTo(pts[0].X, pageH - pts[0].Y);
            foreach (var p in pts.Skip(1)) canvas.LineTo(p.X, pageH - p.Y);
            canvas.Stroke();
        }
        canvas.RestoreState();
    }

    // ── Table ────────────────────────────────────────────────────────────────

    private static void DrawTable(DesignItem tb, PdfCanvas canvas, Document doc, float x, float y, float w, float h)
    {
        if (tb.Rows <= 0 || tb.Columns <= 0) return;
        float cellW = w / tb.Columns, cellH = h / tb.Rows;
        var border = Rgb(tb.BorderColor);
        var headerBg = Rgb(tb.HeaderBgColor, (255, 220, 230, 245));
        var cellBg = Rgb(tb.CellBgColor, (255, 255, 255, 255));
        float borderW = (float)(tb.BorderThick > 0 ? tb.BorderThick : 1);
        try
        {
            var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
            var bold = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD);
            for (int r = 0; r < tb.Rows; r++)
                for (int c = 0; c < tb.Columns; c++)
                {
                    float cx = x + c * cellW, cy = y + h - (r + 1) * cellH;
                    canvas.SaveState().SetFillColor(r == 0 ? headerBg : cellBg).Rectangle(cx, cy, cellW, cellH).Fill().RestoreState();
                    canvas.SaveState().SetStrokeColor(border).SetLineWidth(borderW).Rectangle(cx, cy, cellW, cellH).Stroke().RestoreState();
                    var text = tb.Cell(r, c);
                    if (text.Length > 0)
                        doc.Add(new Paragraph(text).SetFont(r == 0 ? bold : font).SetFontSize(r == 0 ? 9f : 8f)
                            .SetFontColor(new DeviceRgb(0, 0, 0)).SetFixedPosition(cx + 2, cy + 2, cellW - 4)
                            .SetHeight(cellH - 4).SetMargin(0).SetPadding(0));
                }
        }
        catch { /* skip */ }
    }

    // ── Form fields ──────────────────────────────────────────────────────────

    /// <summary>
    /// A real form field for the placeholder; a Left/Right label is drawn as text beside it and a
    /// Placeholder label becomes the tooltip. Same-named fields get _2, _3 … except option buttons,
    /// which share a name to form a group.
    /// </summary>
    private static void DrawField(DesignItem f, Document doc, PdfDocument pdf, PdfPage page, float x, float y, float w, float h,
        Dictionary<string, (PdfButtonFormField Group, int ButtonCount)> radioGroups, Dictionary<string, int> usedNames)
    {
        try
        {
            var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
            string label = f.Label ?? "";
            string position = f.LabelPosition ?? "Left";
            string kind = f.FieldKind ?? "Text";

            bool external = position is "Left" or "Right";
            bool square = kind is "Checkbox" or "Radio";   // a tick box is as wide as it is tall
            float labelW = external ? Math.Min(square ? Math.Max(0, w - h) : w * 0.4f, font.GetWidth(label, 10f) + 8f) : 0f;
            float gap = external ? (float)f.LabelOffset : 0f;
            float fieldW = square ? Math.Min(w, h) : Math.Max(8f, w - labelW - gap);
            float fieldX = position == "Left" ? x + labelW + gap : x;

            if (external && label.Length > 0)
            {
                float labelX = position == "Left" ? x : x + fieldW + gap;
                doc.Add(new Paragraph(label).SetFont(font).SetFontSize(10f).SetFontColor(new DeviceRgb(0, 0, 0))
                    .SetFixedPosition(labelX, y, labelW).SetHeight(h).SetMargin(0).SetPadding(0));
            }

            var rect = new Rectangle(fieldX, y, fieldW, h);
            var form = PdfAcroForm.GetAcroForm(pdf, true);
            string? tooltip = position == "Placeholder" ? label : null;
            string rawName = string.IsNullOrWhiteSpace(f.FieldName) ? kind : f.FieldName!;
            string value = f.Value ?? "";
            string exportValue = string.IsNullOrEmpty(f.ExportValue) ? "Yes" : f.ExportValue!;
            bool isOn = value.Length > 0 && (value == exportValue || value is "Yes" or "On" or "true");

            if (kind == "Radio")
            {
                if (!radioGroups.TryGetValue(rawName, out var info))
                {
                    info = (new RadioFormFieldBuilder(pdf, rawName).CreateRadioGroup(), 0);
                    radioGroups[rawName] = info;
                }
                // Each option button needs its own value so viewers can tell them apart.
                string btnValue = string.IsNullOrWhiteSpace(label) || label == f.FieldName ? $"option{info.ButtonCount + 1}" : label;
                var btn = new RadioFormFieldBuilder(pdf, rawName).CreateRadioButton(btnValue, rect);
                info.Group.AddKid(btn);
                if (isOn) info.Group.SetValue(btnValue);
                if (tooltip != null) info.Group.Put(PdfName.TU, new PdfString(tooltip));
                radioGroups[rawName] = (info.Group, info.ButtonCount + 1);
                return;
            }

            string name;
            if (usedNames.TryGetValue(rawName, out int count)) { usedNames[rawName] = count + 1; name = $"{rawName}_{count + 1}"; }
            else { usedNames[rawName] = 1; name = rawName; }

            PdfFormField? field = kind switch
            {
                "Text" => new TextFormFieldBuilder(pdf, name).SetWidgetRectangle(rect).CreateText(),
                "Memo" => new TextFormFieldBuilder(pdf, name).SetWidgetRectangle(rect).CreateMultilineText(),
                "Checkbox" => new CheckBoxFormFieldBuilder(pdf, name).SetWidgetRectangle(rect).CreateCheckBox(),
                "ComboBox" => Combo(pdf, name, rect, f.Options, font),
                "Signature" => new SignatureFormFieldBuilder(pdf, name).SetWidgetRectangle(rect).CreateSignature(),
                _ => null,
            };
            if (field == null) return;
            if (kind is "Text" or "Memo") field.SetFont(font).SetFontSize(f.FontSize > 0 ? (float)f.FontSize : 10f);
            field.SetRequired(f.Required);
            if (kind is "Text" or "Memo" or "ComboBox" && value.Length > 0) field.SetValue(value);
            else if (kind == "Checkbox" && isOn) field.SetValue("Yes");
            if (tooltip != null) field.Put(PdfName.TU, new PdfString(tooltip));
            form.AddField(field, page);
        }
        catch { /* skip a field that can't be built rather than failing the export */ }
    }

    private static PdfFormField Combo(PdfDocument pdf, string name, Rectangle rect, IReadOnlyList<string> options, PdfFont font)
    {
        var builder = new ChoiceFormFieldBuilder(pdf, name).SetWidgetRectangle(rect);
        if (options.Count > 0) builder.SetOptions(options.ToArray());
        var combo = builder.CreateComboBox();
        combo.SetFont(font).SetFontSize(10f);
        return combo;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DeviceRgb Rgb(string? hex, (byte A, byte R, byte G, byte B) fallback = default)
    {
        var c = DesignColor.Parse(hex, fallback);
        return new DeviceRgb(c.R / 255f, c.G / 255f, c.B / 255f);
    }

    private static iText.Layout.Properties.TextAlignment Align(string? a) => a switch
    {
        "Center" => iText.Layout.Properties.TextAlignment.CENTER,
        "Right" => iText.Layout.Properties.TextAlignment.RIGHT,
        "Justify" => iText.Layout.Properties.TextAlignment.JUSTIFIED,
        _ => iText.Layout.Properties.TextAlignment.LEFT,
    };
}
