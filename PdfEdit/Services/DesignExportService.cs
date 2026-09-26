using System.Windows;
using System.Windows.Media;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Extgstate;
using iText.Kernel.Pdf.Canvas.Draw;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using PdfEdit.Models;

namespace PdfEdit.Services;

/// <summary>Converts a list of DesignElements into a PDF page via iText7.</summary>
public static class DesignExportService
{
    /// <summary>Export elements to a new PDF file.</summary>
    public static void ExportToPdf(
        IEnumerable<DesignElement> elements,
        double pageWidthPt,
        double pageHeightPt,
        string outputPath,
        System.Windows.Media.Color? backgroundColor = null)
    {
        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(writer);
        var pageSize     = new PageSize((float)pageWidthPt, (float)pageHeightPt);
        var page         = pdf.AddNewPage(pageSize);

        var pdfCanvas = new PdfCanvas(page);
        var document  = new Document(pdf, pageSize);
        document.SetMargins(0, 0, 0, 0);

        // Fill page background if not white
        if (backgroundColor is { } bg && (bg.R != 255 || bg.G != 255 || bg.B != 255 || bg.A != 255))
        {
            pdfCanvas.SaveState();
            pdfCanvas.SetFillColor(new iText.Kernel.Colors.DeviceRgb(bg.R / 255f, bg.G / 255f, bg.B / 255f));
            pdfCanvas.Rectangle(0, 0, (float)pageWidthPt, (float)pageHeightPt);
            pdfCanvas.Fill();
            pdfCanvas.RestoreState();
        }

        // Tell Acrobat to regenerate field appearances from their values
        var acroForm = PdfAcroForm.GetAcroForm(pdf, true);
        acroForm.SetNeedAppearances(true);

        // Per-export state for form fields
        var radioGroups = new Dictionary<string, (PdfButtonFormField Group, int ButtonCount)>();
        var usedNames   = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var elem in elements.OrderBy(e => e.ZOrder))
        {
            DrawElement(elem, pdfCanvas, document, pdf, page, pageWidthPt, pageHeightPt, radioGroups, usedNames);
        }

        document.Close();
    }

    private static void DrawElement(
        DesignElement elem,
        PdfCanvas pdfCanvas,
        Document doc,
        PdfDocument pdf,
        PdfPage page,
        double pageW,
        double pageH,
        Dictionary<string, (PdfButtonFormField Group, int ButtonCount)> radioGroups,
        Dictionary<string, int> usedNames)
    {
        // WPF origin is top-left (y↓); PDF origin is bottom-left (y↑).
        // Flip: pdf_y = pageH - (elem.Y + elem.Height)
        float x  = (float)elem.X;
        float y  = (float)(pageH - elem.Y - elem.Height);
        float w  = (float)elem.Width;
        float h  = (float)elem.Height;

        // Apply opacity via ExtGState if less than fully opaque
        if (elem.Opacity < 0.999)
        {
            var gs = new PdfExtGState()
                .SetFillOpacity((float)elem.Opacity)
                .SetStrokeOpacity((float)elem.Opacity);
            pdfCanvas.SetExtGState(gs);
        }

        switch (elem)
        {
            case TextDesignElement t:
                DrawText(t, doc, x, y, w, h, pageW, pageH);
                break;

            case ShapeDesignElement s:
                DrawShape(s, pdfCanvas, x, y, w, h);
                break;

            case ImageDesignElement img:
                DrawImage(img, doc, x, y, w, h);
                break;

            case FreehandDesignElement fh:
                DrawFreehand(fh, pdfCanvas, pageH);
                break;

            case TableDesignElement tb:
                DrawTable(tb, pdfCanvas, doc, x, y, w, h);
                break;

            case FormFieldDesignElement f:
                DrawFormField(f, doc, pdf, page, x, y, w, h, radioGroups, usedNames);
                break;
        }
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    private static void DrawText(TextDesignElement t, Document doc, float x, float y, float w, float h, double pageW, double pageH)
    {
        try
        {
            var fontName = t.Bold && t.Italic ? iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLDOBLIQUE
                         : t.Bold            ? iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD
                         : t.Italic          ? iText.IO.Font.Constants.StandardFonts.HELVETICA_OBLIQUE
                         :                     iText.IO.Font.Constants.StandardFonts.HELVETICA;

            var font     = PdfFontFactory.CreateFont(fontName);
            var color    = ToDeviceRgb(t.Color);
            // Wrap=false: lay out on a much wider box than the visible element so lines
            // never break — PDF has no literal "no-wrap" flag for free-standing text.
            float layoutW = t.Wrap ? w : Math.Max(w, 2000f);
            var para     = new Paragraph(t.Text)
                .SetFont(font)
                .SetFontSize((float)t.FontSize)
                .SetFontColor(color)
                .SetFixedPosition(x, y, layoutW)
                .SetHeight(h)
                .SetTextAlignment(ToITextAlignment(t.Alignment));

            if (t.Underline) para.SetUnderline();
            if (t.BgColor.A > 0)
                para.SetBackgroundColor(ToDeviceRgb(t.BgColor), t.BgColor.A / 255f);

            doc.Add(para);
        }
        catch { /* fall back silently */ }
    }

    // ── Shapes ───────────────────────────────────────────────────────────────

    private static void DrawShape(ShapeDesignElement s, PdfCanvas canvas, float x, float y, float w, float h)
    {
        canvas.SaveState();

        bool hasFill   = s.FillColor.A > 0;
        bool hasStroke = s.StrokeColor.A > 0 && s.StrokeThickness > 0;

        if (hasFill)   canvas.SetFillColor(ToDeviceRgb(s.FillColor));
        if (hasStroke) { canvas.SetStrokeColor(ToDeviceRgb(s.StrokeColor)); canvas.SetLineWidth((float)s.StrokeThickness); }

        switch (s.ElementType)
        {
            case DesignElementType.Rectangle:
                if (s.CornerRadius > 0)
                    canvas.RoundRectangle(x, y, w, h, (float)Math.Min(s.CornerRadius, Math.Min(w, h) / 2));
                else
                    canvas.Rectangle(x, y, w, h);
                ApplyFillStroke(canvas, hasFill, hasStroke);
                break;

            case DesignElementType.Ellipse:
                // Approximate ellipse with Bezier curves
                DrawEllipse(canvas, x + w / 2, y + h / 2, w / 2, h / 2);
                ApplyFillStroke(canvas, hasFill, hasStroke);
                break;

            case DesignElementType.Line:
                canvas.MoveTo(x, y + h).LineTo(x + w, y);
                if (hasStroke) canvas.Stroke();
                break;

            case DesignElementType.Arrow:
                DrawArrow(canvas, x, y + h, x + w, y, (float)s.StrokeThickness);
                if (hasStroke) canvas.Stroke();
                break;
        }

        canvas.RestoreState();
    }

    private static void DrawEllipse(PdfCanvas canvas, float cx, float cy, float rx, float ry)
    {
        const float k = 0.5523f; // Bezier handle ratio for circle approximation
        canvas.MoveTo(cx + rx, cy);
        canvas.CurveTo(cx + rx, cy + k * ry, cx + k * rx, cy + ry, cx, cy + ry);
        canvas.CurveTo(cx - k * rx, cy + ry, cx - rx, cy + k * ry, cx - rx, cy);
        canvas.CurveTo(cx - rx, cy - k * ry, cx - k * rx, cy - ry, cx, cy - ry);
        canvas.CurveTo(cx + k * rx, cy - ry, cx + rx, cy - k * ry, cx + rx, cy);
        canvas.ClosePath();
    }

    private static void DrawArrow(PdfCanvas canvas, float x1, float y1, float x2, float y2, float thickness)
    {
        canvas.MoveTo(x1, y1).LineTo(x2, y2);
        // Arrowhead
        double angle = Math.Atan2(y2 - y1, x2 - x1);
        double al = Math.Max(10, thickness * 4);
        float ax1 = (float)(x2 - al * Math.Cos(angle - 0.4));
        float ay1 = (float)(y2 - al * Math.Sin(angle - 0.4));
        float ax2 = (float)(x2 - al * Math.Cos(angle + 0.4));
        float ay2 = (float)(y2 - al * Math.Sin(angle + 0.4));
        canvas.MoveTo(x2, y2).LineTo(ax1, ay1);
        canvas.MoveTo(x2, y2).LineTo(ax2, ay2);
    }

    private static void ApplyFillStroke(PdfCanvas canvas, bool fill, bool stroke)
    {
        if (fill && stroke) canvas.FillStroke();
        else if (fill)      canvas.Fill();
        else if (stroke)    canvas.Stroke();
    }

    // ── Image ─────────────────────────────────────────────────────────────────

    private static void DrawImage(ImageDesignElement img, Document doc, float x, float y, float w, float h)
    {
        if (string.IsNullOrEmpty(img.FilePath) || !System.IO.File.Exists(img.FilePath)) return;
        try
        {
            var imageData = iText.IO.Image.ImageDataFactory.Create(img.FilePath);
            var image     = new iText.Layout.Element.Image(imageData)
                .SetFixedPosition(x, y)
                .ScaleToFit(w, h);
            doc.Add(image);
        }
        catch { /* ignore missing/corrupt images */ }
    }

    // ── Freehand ─────────────────────────────────────────────────────────────

    private static void DrawFreehand(FreehandDesignElement fh, PdfCanvas canvas, double pageH)
    {
        canvas.SaveState();
        canvas.SetStrokeColor(ToDeviceRgb(fh.Color));
        canvas.SetLineWidth((float)fh.Thickness);
        canvas.SetLineCapStyle(1);  // 1 = Round
        canvas.SetLineJoinStyle(1); // 1 = Round

        foreach (var stroke in fh.Strokes)
        {
            if (stroke.Count == 0) continue;
            var first = stroke[0];
            canvas.MoveTo(first.X, pageH - first.Y);
            foreach (var pt in stroke.Skip(1))
                canvas.LineTo(pt.X, pageH - pt.Y);
            canvas.Stroke();
        }

        canvas.RestoreState();
    }

    // ── Table ─────────────────────────────────────────────────────────────────

    private static void DrawTable(TableDesignElement tb, PdfCanvas canvas, Document doc, float x, float y, float w, float h)
    {
        if (tb.Rows == 0 || tb.Columns == 0) return;

        float cellW = w / tb.Columns;
        float cellH = h / tb.Rows;
        var borderColor = ToDeviceRgb(tb.BorderColor);
        var headerBg    = ToDeviceRgb(tb.HeaderBgColor);
        var cellBg      = ToDeviceRgb(tb.CellBgColor);

        try
        {
            var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
            var fontBold = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD);

            for (int r = 0; r < tb.Rows; r++)
            {
                for (int c = 0; c < tb.Columns; c++)
                {
                    float cx = x + c * cellW;
                    float cy = y + h - (r + 1) * cellH;

                    // Background
                    canvas.SaveState();
                    canvas.SetFillColor(r == 0 ? headerBg : cellBg);
                    canvas.Rectangle(cx, cy, cellW, cellH);
                    canvas.Fill();
                    canvas.RestoreState();

                    // Border
                    canvas.SaveState();
                    canvas.SetStrokeColor(borderColor);
                    canvas.SetLineWidth((float)tb.BorderThickness);
                    canvas.Rectangle(cx, cy, cellW, cellH);
                    canvas.Stroke();
                    canvas.RestoreState();

                    // Text
                    string cellText = tb.GetCell(r, c);
                    if (!string.IsNullOrEmpty(cellText))
                    {
                        var para = new Paragraph(cellText)
                            .SetFont(r == 0 ? fontBold : font)
                            .SetFontSize(r == 0 ? 9f : 8f)
                            .SetFontColor(new DeviceRgb(0, 0, 0))
                            .SetFixedPosition(cx + 2, cy + 2, cellW - 4)
                            .SetHeight(cellH - 4)
                            .SetMargin(0).SetPadding(0);
                        doc.Add(para);
                    }
                }
            }
        }
        catch { /* ignore */ }
    }

    // ── Form fields ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a real AcroForm field for the placeholder and, when the label sits to the
    /// left/right of the field, burns the caption in as static (non-editable) text next to it.
    /// "Placeholder" labels become the field's /TU tooltip entry (shown as hint in Acrobat).
    ///
    /// Field-name deduplication: non-radio fields with identical names get a numeric suffix
    /// (_2, _3, …) so each widget maps to a distinct AcroForm field. Radio fields deliberately
    /// share a name — that is how Acrobat groups them into a mutually-exclusive set.
    /// </summary>
    private static void DrawFormField(
        FormFieldDesignElement f, Document doc, PdfDocument pdf, PdfPage page,
        float x, float y, float w, float h,
        Dictionary<string, (PdfButtonFormField Group, int ButtonCount)> radioGroups,
        Dictionary<string, int> usedNames)
    {
        try
        {
            var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);

            // ── Split footprint: static label region + interactive field box ──
            bool hasExternalLabel = f.LabelPosition is FieldLabelPosition.Left or FieldLabelPosition.Right;
            float labelW = hasExternalLabel
                ? Math.Min(w * 0.4f, (float)font.GetWidth(f.Label, 10f) + 8f)
                : 0f;
            float gap    = hasExternalLabel ? (float)f.LabelOffset : 0f;
            float fieldX = f.LabelPosition == FieldLabelPosition.Left ? x + labelW + gap : x;
            float fieldW = Math.Max(8f, w - labelW - gap);

            // Burn the external label as static (non-editable) text
            if (hasExternalLabel && !string.IsNullOrEmpty(f.Label))
            {
                float labelX = f.LabelPosition == FieldLabelPosition.Left ? x : x + fieldW + gap;
                doc.Add(new Paragraph(f.Label)
                    .SetFont(font).SetFontSize(10f).SetFontColor(new DeviceRgb(0, 0, 0))
                    .SetFixedPosition(labelX, y, labelW)
                    .SetHeight(h)
                    .SetMargin(0).SetPadding(0));
            }

            var rect = new Rectangle(fieldX, y, fieldW, h);
            var form = PdfAcroForm.GetAcroForm(pdf, true);

            // Tooltip: placeholder label text → PDF /TU (shown as hint in Acrobat)
            string? tooltip = f.LabelPosition == FieldLabelPosition.Placeholder ? f.Label : null;

            // ── Raw field name from the element ──
            string rawName = string.IsNullOrWhiteSpace(f.FieldName) ? f.FieldKind.ToString() : f.FieldName;

            // ── Radio buttons: shared group name is intentional (groups them) ──
            if (f.FieldKind == FormFieldKind.Radio)
            {
                if (!radioGroups.TryGetValue(rawName, out var info))
                {
                    var grp = new RadioFormFieldBuilder(pdf, rawName).CreateRadioGroup();
                    form.AddField(grp, page);
                    info = (grp, 0);
                    radioGroups[rawName] = info;
                }
                // Each radio button must export a unique value so Acrobat can tell them apart
                string btnValue = string.IsNullOrWhiteSpace(f.Label) || f.Label == f.FieldName
                    ? $"option{info.ButtonCount + 1}"
                    : f.Label;
                var btn = new RadioFormFieldBuilder(pdf, rawName).CreateRadioButton(btnValue, rect);
                info.Group.AddKid(btn);
                if (tooltip != null) info.Group.Put(PdfName.TU, new PdfString(tooltip));
                radioGroups[rawName] = (info.Group, info.ButtonCount + 1);
                return;
            }

            // ── All other fields: deduplicate names so each widget is its own field ──
            string fieldName;
            if (usedNames.TryGetValue(rawName, out int count))
            {
                usedNames[rawName] = count + 1;
                fieldName = $"{rawName}_{count + 1}";
            }
            else
            {
                usedNames[rawName] = 1;
                fieldName = rawName;
            }

            PdfFormField? field = f.FieldKind switch
            {
                FormFieldKind.Text      => new TextFormFieldBuilder(pdf, fieldName)
                                              .SetWidgetRectangle(rect).CreateText(),
                FormFieldKind.Memo      => new TextFormFieldBuilder(pdf, fieldName)
                                              .SetWidgetRectangle(rect).CreateMultilineText(),
                FormFieldKind.Checkbox  => new CheckBoxFormFieldBuilder(pdf, fieldName)
                                              .SetWidgetRectangle(rect).CreateCheckBox(),
                FormFieldKind.ComboBox  => BuildCombo(pdf, fieldName, rect, f.Options, font),
                FormFieldKind.Signature => new SignatureFormFieldBuilder(pdf, fieldName)
                                              .SetWidgetRectangle(rect).CreateSignature(),
                _                       => null
            };

            if (field == null) return;

            // Font for text-bearing fields
            if (f.FieldKind is FormFieldKind.Text or FormFieldKind.Memo)
                field.SetFont(font).SetFontSize(10f);

            field.SetRequired(f.Required);

            // Tooltip / alternate description (/TU) — shown in Acrobat's tooltip on hover
            if (tooltip != null)
                field.Put(PdfName.TU, new PdfString(tooltip));

            form.AddField(field, page);
        }
        catch { /* skip a field that fails to build rather than aborting the whole export */ }
    }

    private static PdfFormField BuildCombo(PdfDocument pdf, string fieldName, Rectangle rect,
                                           IReadOnlyList<string> options, PdfFont font)
    {
        var builder = new ChoiceFormFieldBuilder(pdf, fieldName).SetWidgetRectangle(rect);
        if (options.Count > 0) builder.SetOptions(options.ToArray());
        var combo = builder.CreateComboBox();
        combo.SetFont(font).SetFontSize(10f);
        return combo;
    }

    // ── Color conversion ──────────────────────────────────────────────────────

    private static DeviceRgb ToDeviceRgb(System.Windows.Media.Color c)
        => new(c.R / 255f, c.G / 255f, c.B / 255f);

    private static iText.Layout.Properties.TextAlignment ToITextAlignment(System.Windows.TextAlignment a) => a switch
    {
        System.Windows.TextAlignment.Center  => iText.Layout.Properties.TextAlignment.CENTER,
        System.Windows.TextAlignment.Right   => iText.Layout.Properties.TextAlignment.RIGHT,
        System.Windows.TextAlignment.Justify => iText.Layout.Properties.TextAlignment.JUSTIFIED,
        _                                    => iText.Layout.Properties.TextAlignment.LEFT
    };
}
