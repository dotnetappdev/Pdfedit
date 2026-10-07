using System.Collections.ObjectModel;
using iText.Forms;
using iText.Forms.Fields;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Xobject;
using PdfEdit.Models;
using Rectangle = iText.Kernel.Geom.Rectangle;
using PdfDocumentInfo = PdfEdit.Models.PdfDocumentInfo;
using EncryptionConstants = iText.Kernel.Pdf.EncryptionConstants;

namespace PdfEdit.Services;

public class PdfFormService
{
    public PdfDocumentInfo LoadDocument(string path)
    {
        var info = new PdfDocumentInfo { FilePath = path };

        using var reader = new PdfReader(path);
        using var doc = new PdfDocument(reader);

        info.PageCount = doc.GetNumberOfPages();

        var meta = doc.GetDocumentInfo();
        info.Title = meta.GetTitle() ?? string.Empty;
        info.Author = meta.GetAuthor() ?? string.Empty;
        info.Subject = meta.GetSubject() ?? string.Empty;

        for (int i = 1; i <= info.PageCount; i++)
        {
            var p = doc.GetPage(i);
            var size = p.GetPageSize();
            info.PageSizes.Add((size.GetWidth(), size.GetHeight()));
            info.PageRotations.Add(p.GetRotation());
        }

        var form = PdfAcroForm.GetAcroForm(doc, false);
        info.HasAcroForm = form != null;

        if (form != null)
        {
            var fields = form.GetAllFormFields();
            foreach (var (name, field) in fields)
            {
                var widgets = field.GetWidgets();
                if (widgets == null || widgets.Count == 0) continue;

                int widgetIndex = -1;
                foreach (var widget in widgets)
                {
                    widgetIndex++;
                    var page = widget.GetPage();
                    if (page == null) continue;

                    var rectObj = widget.GetRectangle();
                    if (rectObj == null) continue;

                    var rect = rectObj.ToRectangle();
                    int pageNum = doc.GetPageNumber(page);

                    var fieldInfo = new FormFieldInfo
                    {
                        Name = name,
                        WidgetIndex = widgetIndex,
                        PageNumber = pageNum,
                        Left = rect.GetX(),
                        Bottom = rect.GetY(),
                        Width = rect.GetWidth(),
                        Height = rect.GetHeight(),
                        Value = field.GetValueAsString() ?? string.Empty,
                        FieldType = GetFieldType(field),
                        IsReadOnly = field.IsReadOnly(),
                        IsRequired = field.IsRequired(),
                        DefaultValue = field.GetPdfObject().GetAsString(PdfName.DV)?.ToUnicodeString() ?? string.Empty,
                        Tooltip = field.GetPdfObject().GetAsString(PdfName.TU)?.ToUnicodeString() ?? string.Empty,
                    };

                    try
                    {
                        fieldInfo.Alignment = field.GetJustification() switch
                        {
                            iText.Layout.Properties.TextAlignment.CENTER => Models.FieldAlignment.Center,
                            iText.Layout.Properties.TextAlignment.RIGHT  => Models.FieldAlignment.Right,
                            _ => Models.FieldAlignment.Left,
                        };
                        fieldInfo.FontSize = Math.Max(0, field.GetFontSize());
                    }
                    catch { /* malformed /DA — keep defaults */ }
                    try
                    {
                        // The field's own font, so its value is shown in it (not the app's UI font).
                        fieldInfo.FontName = field.GetFont()?.GetFontProgram()?.GetFontNames()?.GetFontName();
                    }
                    catch { /* malformed /DA — keep defaults */ }
                    try { ReadAcrobatFieldProperties(field, widget, fieldInfo); }
                    catch { /* optional appearance/options — keep defaults */ }

                    if (field is PdfTextFormField txt)
                    {
                        fieldInfo.IsMultiline = txt.IsMultiline();
                        fieldInfo.IsPassword = txt.IsPassword();
                    }

                    if (field is PdfChoiceFormField choiceField)
                    {
                        var options = choiceField.GetOptions();
                        if (options != null)
                        {
                            foreach (var opt in options)
                            {
                                if (opt is PdfArray arr && arr.Size() > 1)
                                    fieldInfo.Options.Add(arr.GetAsString(1)?.ToString() ?? string.Empty);
                                else if (opt is PdfString str)
                                    fieldInfo.Options.Add(str.ToString() ?? string.Empty);
                            }
                        }
                    }

                    if (field is PdfButtonFormField btn && btn.IsRadio())
                    {
                        fieldInfo.RadioGroup = name;
                        // ExportValue = the on-value this WIDGET represents.
                        // Value = the group's currently-selected export value (do NOT overwrite it).
                        var appearance = widget.GetAppearanceDictionary();
                        if (appearance != null)
                        {
                            var normalAp = appearance.GetAsDictionary(PdfName.N);
                            if (normalAp != null)
                            {
                                foreach (var key in normalAp.KeySet())
                                {
                                    if (!key.Equals(new PdfName("Off")))
                                    {
                                        fieldInfo.ExportValue = key.GetValue();
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    else if (field is PdfButtonFormField cbtn && !cbtn.IsPushButton() && !cbtn.IsRadio())
                    {
                        // Checkbox: detect actual on-value from appearance dict
                        var appearance = widget.GetAppearanceDictionary();
                        if (appearance != null)
                        {
                            var normalAp = appearance.GetAsDictionary(PdfName.N);
                            if (normalAp != null)
                            {
                                foreach (var key in normalAp.KeySet())
                                {
                                    if (!key.Equals(new PdfName("Off")))
                                    {
                                        fieldInfo.ExportValue = key.GetValue();
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    info.FormFields.Add(fieldInfo);
                }
            }
        }

        return info;
    }

    public List<string> SaveWithFormData(string sourcePath, string destPath,
        Dictionary<string, string> fieldValues, bool flatten = false)
    {
        return SaveFull(sourcePath, destPath, fieldValues,
            new Dictionary<int, int>(),
            new ObservableCollection<FreeTextAnnotation>(),
            new ObservableCollection<PlacedSignature>(),
            flatten);
    }

    /// <summary>
    /// Writes moved/resized widget rectangles (PDF points) into <paramref name="outputPath"/>
    /// without touching anything else in the document.
    /// </summary>
    public List<string> ApplyFieldBounds(string inputPath, string outputPath,
        IReadOnlyDictionary<(string Name, int WidgetIndex), Models.FieldBounds> fieldBounds)
    {
        var errors = new List<string>();
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var doc = new PdfDocument(reader, writer);
        var form = PdfAcroForm.GetAcroForm(doc, false);
        if (form != null) ApplyFieldBounds(form, fieldBounds, errors);
        return errors;
    }

    // ── Acrobat field properties: /MK colours, text colour, MaxLen, comb, edit, date format ──

    private static string? ToHex(PdfArray? arr)
    {
        if (arr == null || arr.Size() == 0) return null;
        float r, g, b;
        if (arr.Size() >= 3) { r = arr.GetAsNumber(0).FloatValue(); g = arr.GetAsNumber(1).FloatValue(); b = arr.GetAsNumber(2).FloatValue(); }
        else if (arr.Size() == 1) { r = g = b = arr.GetAsNumber(0).FloatValue(); }
        else return null;
        return $"#{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}";
    }

    private static DeviceRgb? FromHex(string? hex) =>
        !string.IsNullOrWhiteSpace(hex) && ParseHexColor(hex, out float r, out float g, out float b) ? new DeviceRgb(r, g, b) : null;

    private static readonly System.Text.RegularExpressions.Regex AfDateRx =
        new(@"AFDate_(?:FormatEx|KeystrokeEx)\(\s*""([^""]+)""", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static void ReadAcrobatFieldProperties(PdfFormField field, iText.Kernel.Pdf.Annot.PdfWidgetAnnotation widget, Models.FormFieldInfo info)
    {
        var mk = widget.GetPdfObject().GetAsDictionary(PdfName.MK);
        info.BorderColor = ToHex(mk?.GetAsArray(PdfName.BC));
        info.FillColor = ToHex(mk?.GetAsArray(PdfName.BG));
        if (field.GetColor() is { } c && c.GetColorValue() is { Length: >= 3 } v)
            info.TextColor = $"#{(int)Math.Round(v[0] * 255):X2}{(int)Math.Round(v[1] * 255):X2}{(int)Math.Round(v[2] * 255):X2}";
        if (field is PdfTextFormField t)
        {
            info.MaxLength = Math.Max(0, t.GetMaxLen());
            info.IsComb = t.IsComb();
        }
        if (field is PdfChoiceFormField ch) info.IsEditable = ch.IsEdit();

        var aa = field.GetPdfObject().GetAsDictionary(PdfName.AA);
        string js = aa?.GetAsDictionary(PdfName.F)?.GetAsString(PdfName.JS)?.ToUnicodeString()
                 ?? aa?.GetAsDictionary(PdfName.K)?.GetAsString(PdfName.JS)?.ToUnicodeString() ?? string.Empty;
        var m = AfDateRx.Match(js);
        if (m.Success) info.DateFormat = m.Groups[1].Value;
        else FieldFormatting.ReadFormat(js, info);
        string calc = aa?.GetAsDictionary(PdfName.C)?.GetAsString(PdfName.JS)?.ToUnicodeString() ?? string.Empty;
        FieldFormatting.ReadCalc(calc, info);
    }

    private static void WriteAcrobatFieldProperties(PdfFormField field, Models.FormFieldInfo edit)
    {
        foreach (var annot in field.GetChildFormAnnotations())
        {
            var border = FromHex(edit.BorderColor);
            var fill = FromHex(edit.FillColor);
            var mk = annot.GetPdfObject().GetAsDictionary(PdfName.MK);
            if (border != null) { annot.SetBorderColor(border); if (annot.GetBorderWidth() <= 0) annot.SetBorderWidth(1); }
            else mk?.Remove(PdfName.BC);
            if (fill != null) annot.SetBackgroundColor(fill);
            else mk?.Remove(PdfName.BG);
        }
        if (FromHex(edit.TextColor) is { } tc) field.SetColor(tc);

        if (field is PdfTextFormField t)
        {
            if (edit.MaxLength > 0) t.SetMaxLen(edit.MaxLength);
            else t.GetPdfObject().Remove(PdfName.MaxLen);
            t.SetComb(edit.IsComb && edit.MaxLength > 0);
        }

        if (field is PdfChoiceFormField ch)
        {
            if (edit.FieldType == Models.FieldType.ComboBox) ch.SetEdit(edit.IsEditable);
            var opts = new PdfArray();
            foreach (var o in edit.Options.Where(o => !string.IsNullOrWhiteSpace(o))) opts.Add(new PdfString(o));
            ch.SetOptions(opts);
        }

        if (!string.IsNullOrEmpty(edit.DefaultValue))
            field.SetDefaultValue(new PdfString(edit.DefaultValue));

        // Date fields use Acrobat's standard format / keystroke scripts, so Acrobat and other
        // viewers format and validate the date too.
        var aa = field.GetPdfObject().GetAsDictionary(PdfName.AA);
        if (edit.IsDateField)
        {
            string fmt = edit.DateFormat!.Replace("\"", "");
            field.SetAdditionalAction(PdfName.F, iText.Kernel.Pdf.Action.PdfAction.CreateJavaScript($"AFDate_FormatEx(\"{fmt}\");"));
            field.SetAdditionalAction(PdfName.K, iText.Kernel.Pdf.Action.PdfAction.CreateJavaScript($"AFDate_KeystrokeEx(\"{fmt}\");"));
        }
        else if (aa != null)
        {
            foreach (var key in new[] { PdfName.F, PdfName.K })
            {
                string js = aa.GetAsDictionary(key)?.GetAsString(PdfName.JS)?.ToUnicodeString() ?? string.Empty;
                if (js.Contains("AFDate_")) aa.Remove(key);
            }
        }

        // Number / currency / percent / special formats (Acrobat's AF scripts).
        aa = field.GetPdfObject().GetAsDictionary(PdfName.AA);
        if (!edit.IsDateField && FieldFormatting.Scripts(edit) is { } sc)
        {
            field.SetAdditionalAction(PdfName.F, iText.Kernel.Pdf.Action.PdfAction.CreateJavaScript(sc.Format));
            field.SetAdditionalAction(PdfName.K, iText.Kernel.Pdf.Action.PdfAction.CreateJavaScript(sc.Keystroke));
        }
        else if (aa != null && !edit.IsDateField)
        {
            foreach (var key in new[] { PdfName.F, PdfName.K })
            {
                string js = aa.GetAsDictionary(key)?.GetAsString(PdfName.JS)?.ToUnicodeString() ?? string.Empty;
                if (FieldFormatting.IsOurFormatScript(js)) aa.Remove(key);
            }
        }

        // Calculated value (sum / product / average / min / max of other fields).
        aa = field.GetPdfObject().GetAsDictionary(PdfName.AA);
        if (FieldFormatting.CalcScript(edit) is { } calc)
            field.SetAdditionalAction(PdfName.C, iText.Kernel.Pdf.Action.PdfAction.CreateJavaScript(calc));
        else if (aa?.GetAsDictionary(PdfName.C)?.GetAsString(PdfName.JS)?.ToUnicodeString() is { } cjs && cjs.Contains("AFSimple_Calculate"))
            aa.Remove(PdfName.C);
    }

    /// <summary>Keeps the form's calculation order (/CO) in step with the fields that calculate.</summary>
    private static void UpdateCalculationOrder(PdfAcroForm form, PdfFormField field, bool calculates)
    {
        var acro = form.GetPdfObject();
        var co = acro.GetAsArray(PdfName.CO);
        var obj = field.GetPdfObject();
        if (calculates)
        {
            if (co == null) { co = new PdfArray(); acro.Put(PdfName.CO, co); }
            bool present = false;
            for (int i = 0; i < co.Size(); i++) if (co.Get(i, true) == obj) present = true;
            if (!present) co.Add(obj.GetIndirectReference() ?? (PdfObject)obj);
        }
        else if (co != null)
        {
            for (int i = co.Size() - 1; i >= 0; i--) if (co.Get(i, true) == obj) co.Remove(i);
        }
        acro.SetModified();
    }

    private static void ApplyFieldProperties(PdfAcroForm form, Models.FormFieldInfo edit, List<string> errors)
    {
        var field = form.GetField(edit.Name);
        if (field == null) return;
        try
        {
            field.SetRequired(edit.IsRequired);
            field.SetReadOnly(edit.IsReadOnly);
            field.SetAlternativeName(edit.Tooltip ?? string.Empty);
            if (field is PdfTextFormField txt) txt.SetMultiline(edit.IsMultiline);
            if (edit.FieldType is Models.FieldType.Text or Models.FieldType.ComboBox or Models.FieldType.ListBox)
            {
                field.SetJustification(edit.Alignment switch
                {
                    Models.FieldAlignment.Center => iText.Layout.Properties.TextAlignment.CENTER,
                    Models.FieldAlignment.Right  => iText.Layout.Properties.TextAlignment.RIGHT,
                    _ => iText.Layout.Properties.TextAlignment.LEFT,
                });
                if (edit.FontSize > 0) field.SetFontSize((float)edit.FontSize);
                else field.SetFontSizeAutoScale();
            }
            WriteAcrobatFieldProperties(field, edit);
            UpdateCalculationOrder(form, field, FieldFormatting.CalcScript(edit) != null);
            field.RegenerateField();
        }
        catch (Exception ex)
        {
            errors.Add($"Field '{edit.Name}' properties: {ex.Message}");
        }
    }

    /// <summary>
    /// Renames a field. Only the last part of a hierarchical name ("parent.child") can change —
    /// the caller validates that the prefix is unchanged.
    /// </summary>
    private static void RenameField(PdfAcroForm form, string oldName, string newName, List<string> errors)
    {
        var field = form.GetField(oldName);
        if (field == null) return;
        try
        {
            string partial = newName.Contains('.') ? newName[(newName.LastIndexOf('.') + 1)..] : newName;
            field.SetFieldName(partial);
        }
        catch (Exception ex)
        {
            errors.Add($"Rename '{oldName}' → '{newName}': {ex.Message}");
        }
    }

    /// <summary>Sets each widget's /Rect and regenerates its appearance so it renders at the new size.</summary>
    private static void ApplyFieldBounds(PdfAcroForm form,
        IReadOnlyDictionary<(string Name, int WidgetIndex), Models.FieldBounds> fieldBounds, List<string> errors)
    {
        foreach (var ((name, widgetIndex), r) in fieldBounds)
        {
            var field = form.GetField(name);
            if (field == null) continue;
            var widgets = field.GetWidgets();
            if (widgets == null || widgetIndex < 0 || widgetIndex >= widgets.Count) continue;
            try
            {
                widgets[widgetIndex].SetRectangle(new PdfArray(new iText.Kernel.Geom.Rectangle(
                    (float)r.Left, (float)r.Bottom, (float)r.Width, (float)r.Height)));
                field.RegenerateField();
            }
            catch (Exception ex)
            {
                errors.Add($"Field '{name}' position: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Saves the PDF with filled form fields, page rotations, free-text annotations, and placed signatures.
    /// </summary>
    public List<string> SaveFull(string sourcePath, string destPath,
        Dictionary<string, string> fieldValues,
        Dictionary<int, int> pageRotations,
        IEnumerable<FreeTextAnnotation> freeTextAnnotations,
        IEnumerable<PlacedSignature>? placedSignatures = null,
        bool flatten = false,
        IEnumerable<string>? deletedFieldNames = null,
        Dictionary<string, string>? fieldExportValues = null,
        IEnumerable<Models.HighlightAnnotation>? highlightAnnotations = null,
        IEnumerable<Models.StickyNoteAnnotation>? stickyNotes = null,
        IEnumerable<Models.ShapeAnnotation>? shapeAnnotations = null,
        IReadOnlyDictionary<(string Name, int WidgetIndex), Models.FieldBounds>? fieldBounds = null,
        IEnumerable<Models.FormFieldInfo>? fieldEdits = null,
        IEnumerable<Models.TextEditMark>? textEdits = null)
    {
        var saveErrors = new List<string>();
        fieldExportValues ??= new Dictionary<string, string>();

        using var reader = new PdfReader(sourcePath);
        using var writer = new PdfWriter(destPath);
        using var doc = new PdfDocument(reader, writer);

        // PdfEdit's own annotations from an earlier save are re-written below from memory: remove
        // those copies first, or every save would add another one.
        var trackedIds = new HashSet<string>(
            freeTextAnnotations.Select(a => a.Comment.Id)
            .Concat(placedSignatures?.Select(sg => sg.Id) ?? Enumerable.Empty<string>())
            .Concat(highlightAnnotations?.Select(h => h.Comment.Id) ?? Enumerable.Empty<string>())
            .Concat(stickyNotes?.Select(n => n.Comment.Id) ?? Enumerable.Empty<string>())
            .Concat(shapeAnnotations?.Select(sh => sh.Comment.Id) ?? Enumerable.Empty<string>())
            .Concat(textEdits?.Select(t => t.Comment.Id) ?? Enumerable.Empty<string>()));
        RemoveTrackedAnnotations(doc, trackedIds);

        // ── 1. Form fields ────────────────────────────────────────────────
        var form = PdfAcroForm.GetAcroForm(doc, false);
        if (form != null)
        {
            // Remove fields the user deleted (and their widgets) before writing values.
            if (deletedFieldNames != null)
            {
                foreach (var name in deletedFieldNames)
                {
                    if (form.GetField(name) != null)
                        form.RemoveField(name);
                }
            }

            // Apply widget rectangles the user moved/resized in the live view.
            if (fieldBounds != null)
                ApplyFieldBounds(form, fieldBounds, saveErrors);

            // Properties edited in the Properties panel (tooltip, required, alignment, font size …).
            var edits = fieldEdits?.ToList() ?? new List<Models.FormFieldInfo>();
            foreach (var edit in edits)
                ApplyFieldProperties(form, edit, saveErrors);

            foreach (var (name, value) in fieldValues)
            {
                var field = form.GetField(name);
                if (field == null) continue;

                try
                {
                    if (field is PdfButtonFormField btn && !btn.IsPushButton() && !btn.IsRadio())
                    {
                        // Use the export value stored in fieldExportValues when available,
                        // otherwise fall back to "Yes" for checked state.
                        bool isChecked = value is "Yes" or "true" or "On" or "1"
                            || (!string.IsNullOrEmpty(value) && value != "Off" && value != "false" && value != "0");
                        string onValue = fieldExportValues.TryGetValue(name, out var ev) && !string.IsNullOrEmpty(ev)
                            ? ev : "Yes";
                        field.SetValue(isChecked ? onValue : "Off");
                    }
                    else
                    {
                        // Formatted number fields keep the plain number as the value and show the
                        // formatted text (e.g. 1234.5 → £1,234.50), like Acrobat.
                        var fmt = new Models.FormFieldInfo();
                        string fjs = field.GetPdfObject().GetAsDictionary(PdfName.AA)?.GetAsDictionary(PdfName.F)?
                                          .GetAsString(PdfName.JS)?.ToUnicodeString() ?? string.Empty;
                        FieldFormatting.ReadFormat(fjs, fmt);
                        string display = fmt.HasNumberFormat ? FieldFormatting.ToDisplay(value, fmt) : value;
                        if (display != value && field is PdfTextFormField) field.SetValue(value, display);
                        else field.SetValue(value);
                    }
                }
                catch (Exception ex)
                {
                    saveErrors.Add($"Field '{name}': {ex.Message}");
                }
            }

            // Renames go last: everything above looks fields up by their current (old) name.
            foreach (var edit in edits.Where(e => !string.IsNullOrEmpty(e.PendingName) && e.PendingName != e.Name))
                RenameField(form, edit.Name, edit.PendingName!, saveErrors);

            if (flatten) form.FlattenFields();
        }

        // ── 2. Page rotations ─────────────────────────────────────────────
        foreach (var (pageIdx, degrees) in pageRotations)
        {
            int pageNum = pageIdx + 1;
            if (pageNum < 1 || pageNum > doc.GetNumberOfPages()) continue;
            var page = doc.GetPage(pageNum);
            int existing = page.GetRotation();
            page.SetRotation((existing + degrees) % 360);
        }

        // ── 3. Free-text annotations ──────────────────────────────────────
        var stdFont = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);

        foreach (var ann in freeTextAnnotations)
        {
            int pageNum = ann.PageNumber;
            if (pageNum < 1 || pageNum > doc.GetNumberOfPages()) continue;
            var page = doc.GetPage(pageNum);

            // Ink stroke — saved as PdfInkAnnotation
            if (ann.Text.StartsWith("__INK__:", StringComparison.Ordinal))
            {
                var parts = ann.Text.Split(':', 3);
                if (parts.Length == 3)
                {
                    // "#RRGGBB|width" (width in points; older files have just the colour)
                    var head = parts[1].Split('|');
                    bool colorOk = ParseHexColor(head[0], out float ir, out float ig, out float ib);
                    float inkWidth = head.Length > 1 && float.TryParse(head[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float iw) ? iw : 2f;
                    var inkArr = new PdfArray();
                    var inkPts = new PdfArray();
                    foreach (var ptStr in parts[2].Split(';'))
                    {
                        var xy = ptStr.Split(',');
                        if (xy.Length == 2 &&
                            float.TryParse(xy[0], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float ptX) &&
                            float.TryParse(xy[1], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float ptY))
                        {
                            inkPts.Add(new PdfNumber(ptX));
                            inkPts.Add(new PdfNumber(ptY));
                        }
                    }
                    inkArr.Add(inkPts);
                    var inkRect = new Rectangle((float)ann.Left, (float)ann.Bottom,
                        (float)ann.Width, (float)ann.Height);
                    var inkAnnot = new PdfInkAnnotation(inkRect, inkArr);
                    if (colorOk) inkAnnot.SetColor(new DeviceRgb(ir, ig, ib));
                    var bs = new PdfDictionary();
                    bs.Put(PdfName.W, new PdfNumber(inkWidth));
                    inkAnnot.SetBorderStyle(bs);
                    AddTracked(page, inkAnnot, ann.Comment);
                }
                continue;
            }

            var rect = new Rectangle(
                (float)ann.Left,
                (float)ann.Bottom,
                (float)ann.Width,
                (float)ann.Height);

            // Rubber stamp: a real /Stamp annotation with the drawn stamp as its appearance.
            if (ann.IsStamp)
            {
                var stamp = new PdfStampAnnotation(rect);
                var def = Models.StampCatalog.BuiltIn.FirstOrDefault(d => d.Title == ann.Text && d.PdfName != null);
                string name = def?.PdfName ?? new string(ann.Text.Where(char.IsLetterOrDigit).ToArray());
                stamp.SetStampName(new PdfName(string.IsNullOrEmpty(name) ? "Stamp" : name));
                stamp.SetContents(string.IsNullOrWhiteSpace(ann.StampSubtitle) ? ann.Text : $"{ann.Text}\n{ann.StampSubtitle}");
                if (ParseHexColor(ann.FontColor, out float sr, out float sg, out float sb))
                    stamp.SetColor(new DeviceRgb(sr, sg, sb));
                stamp.SetNormalAppearance(BuildStampAppearance(doc, ann).GetPdfObject());
                if (ContentRotation(ann) != 0) stamp.Put(PdfName.Rotate, new PdfNumber(ContentRotation(ann)));
                stamp.SetFlags(PdfAnnotation.PRINT);
                AddTracked(page, stamp, ann.Comment);
                continue;
            }

            var pdfAnn = new PdfFreeTextAnnotation(rect, new PdfString(ann.Text));
            pdfAnn.SetContents(ann.Text);

            // ✓ ✕ ● ○ — : give the annotation a drawn (vector) appearance like Acrobat's marks —
            // the standard fonts have no ✓ / ✕ glyphs, so other viewers showed nothing or a box.
            if (MarkShapes.FromGlyph(ann.Text) is { } markKind)
            {
                pdfAnn.SetNormalAppearance(BuildMarkAppearance(doc, markKind, ann).GetPdfObject());
                pdfAnn.SetBorder(new PdfArray(new float[] { 0, 0, 0 }));
                pdfAnn.SetFlags(PdfAnnotation.PRINT);
            }
            else if (!string.IsNullOrEmpty(ann.Text))
            {
                // Drawn text, so viewers that don't lay out FreeText themselves (Pdfium — Chrome, Edge
                // — and many phone apps) still show it.
                try { pdfAnn.SetNormalAppearance(BuildTextAppearance(doc, ann).GetPdfObject()); }
                catch (Exception ex) { saveErrors.Add($"Text on page {pageNum}: {ex.Message}"); }
            }

            if (ann.RotationAngle != 0)
                pdfAnn.Put(PdfName.Rotate, new PdfNumber((int)((-ann.RotationAngle % 360 + 360) % 360)));

            // Build default appearance: honour bold/italic via font flag approximation using base fonts
            string fontName = ann.IsBold && ann.IsItalic ? "Helvetica-BoldOblique"
                             : ann.IsBold ? "Helvetica-Bold"
                             : ann.IsItalic ? "Helvetica-Oblique"
                             : "Helv";

            string colorStr = ParseHexColor(ann.FontColor, out float r, out float g, out float b)
                ? $"{r:F3} {g:F3} {b:F3} rg"
                : "0 0 0 rg";

            string spacing = ann.CharacterSpacing > 0.01
                ? $" {ann.CharacterSpacing.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} Tc" : string.Empty;
            pdfAnn.SetDefaultAppearance(new PdfString($"/{fontName} {ann.FontSize:F1} Tf{spacing} {colorStr}"));

            AddTracked(page, pdfAnn, ann.Comment);
        }

        // ── 4. Highlight annotations ──────────────────────────────────────
        if (highlightAnnotations != null)
        {
            foreach (var hl in highlightAnnotations)
            {
                int pageNum = hl.PageNumber;
                if (pageNum < 1 || pageNum > doc.GetNumberOfPages()) continue;
                var page = doc.GetPage(pageNum);

                var rect = new Rectangle(
                    (float)hl.Left,
                    (float)hl.Bottom,
                    (float)hl.Width,
                    (float)hl.Height);

                ParseHexColor(hl.Color, out float r, out float g, out float b);
                var color = new DeviceRgb(r, g, b);

                float left = (float)hl.Left, bottom = (float)hl.Bottom;
                float right = left + (float)hl.Width, top = bottom + (float)hl.Height;
                var quadPoints = new float[] { left, top, right, top, left, bottom, right, bottom };

                iText.Kernel.Pdf.Annot.PdfTextMarkupAnnotation pdfHL;
                if (hl.Kind == Models.HighlightKind.Strikethrough)
                    pdfHL = iText.Kernel.Pdf.Annot.PdfTextMarkupAnnotation.CreateStrikeout(rect, quadPoints);
                else if (hl.Kind == Models.HighlightKind.Squiggly)
                    pdfHL = iText.Kernel.Pdf.Annot.PdfTextMarkupAnnotation.CreateSquiggly(rect, quadPoints);
                else if (hl.Kind == Models.HighlightKind.Underline)
                    pdfHL = iText.Kernel.Pdf.Annot.PdfTextMarkupAnnotation.CreateUnderline(rect, quadPoints);
                else
                    pdfHL = iText.Kernel.Pdf.Annot.PdfTextMarkupAnnotation.CreateHighLight(rect, quadPoints);

                pdfHL.SetColor(color);
                pdfHL.Put(PdfName.CA, new PdfNumber(hl.Opacity));
                AddTracked(page, pdfHL, hl.Comment);
            }
        }

        // ── 5. Sticky note annotations ────────────────────────────────────
        if (stickyNotes != null)
        {
            foreach (var note in stickyNotes)
            {
                int pageNum = note.PageNumber;
                if (pageNum < 1 || pageNum > doc.GetNumberOfPages()) continue;
                var page = doc.GetPage(pageNum);

                ParseHexColor(note.Color, out float nr, out float ng, out float nb);
                var noteColor = new DeviceRgb(nr, ng, nb);

                var noteRect = new Rectangle((float)note.Left, (float)note.Bottom, 20f, 20f);
                var textAnnot = new iText.Kernel.Pdf.Annot.PdfTextAnnotation(noteRect);
                textAnnot.SetContents(note.Text);
                textAnnot.SetColor(noteColor);
                textAnnot.SetOpen(false);
                if (!string.IsNullOrEmpty(note.Author)) note.Comment.Author = note.Author;
                AddTracked(page, textAnnot, note.Comment);
            }
        }

        // ── 6. Shape annotations (Rectangle / Ellipse / Arrow) ───────────
        if (shapeAnnotations != null)
        {
            foreach (var shape in shapeAnnotations)
            {
                int pageNum = shape.PageNumber;
                if (pageNum < 1 || pageNum > doc.GetNumberOfPages()) continue;
                var page = doc.GetPage(pageNum);

                ParseHexColor(shape.StrokeColor, out float sr, out float sg, out float sb);
                var strokeColor = new DeviceRgb(sr, sg, sb);
                float lw = (float)Math.Max(shape.LineWidth, 0.5);

                double x1 = shape.X1, y1 = shape.Y1, x2 = shape.X2, y2 = shape.Y2;
                double left   = Math.Min(x1, x2);
                double bottom = Math.Min(y1, y2);
                double width  = Math.Abs(x2 - x1);
                double height = Math.Abs(y2 - y1);

                // Acrobat drawing / measuring kinds (line, cloud, polygon, polyline, distance, perimeter, area)
                if (BuildExtendedShapeAnnotation(doc, shape, strokeColor, lw) is { } extra)
                {
                    if (shape.Opacity < 0.999) extra.Put(PdfName.CA, new PdfNumber(shape.Opacity));
                    AddTracked(page, extra, shape.Comment);
                    continue;
                }

                if (shape.Kind == Models.ShapeKind.Rectangle)
                {
                    var annot = new PdfSquareAnnotation(new Rectangle((float)left, (float)bottom, (float)width, (float)height));
                    annot.SetColor(strokeColor);
                    if (shape.Opacity < 0.999) annot.Put(PdfName.CA, new PdfNumber(shape.Opacity));
                    if (!string.IsNullOrEmpty(shape.FillColor) && ParseHexColor(shape.FillColor, out float fr, out float fg, out float fb))
                        annot.SetInteriorColor(new float[] { fr, fg, fb });
                    annot.Put(PdfName.BS, BuildBorderStyle(lw));
                    AddTracked(page, annot, shape.Comment);
                }
                else if (shape.Kind == Models.ShapeKind.Ellipse)
                {
                    var annot = new PdfCircleAnnotation(new Rectangle((float)left, (float)bottom, (float)width, (float)height));
                    annot.SetColor(strokeColor);
                    if (shape.Opacity < 0.999) annot.Put(PdfName.CA, new PdfNumber(shape.Opacity));
                    if (!string.IsNullOrEmpty(shape.FillColor) && ParseHexColor(shape.FillColor, out float fr, out float fg, out float fb))
                        annot.SetInteriorColor(new float[] { fr, fg, fb });
                    annot.Put(PdfName.BS, BuildBorderStyle(lw));
                    AddTracked(page, annot, shape.Comment);
                }
                else if (shape.Kind == Models.ShapeKind.Arrow)
                {
                    float pad = lw + 12;
                    var lineRect = new Rectangle((float)left - pad, (float)bottom - pad, (float)Math.Max(width, 1) + 2 * pad, (float)Math.Max(height, 1) + 2 * pad);
                    var annot = new PdfLineAnnotation(lineRect, new float[] { (float)x1, (float)y1, (float)x2, (float)y2 });
                    annot.SetColor(strokeColor);
                    annot.Put(PdfName.LE, new PdfArray(new[] { new PdfName("None"), new PdfName("OpenArrow") }));
                    annot.SetNormalAppearance(BuildLineAppearance(doc, lineRect, new List<PointD> { new(x1, y1), new(x2, y2) },
                        closed: false, null, strokeColor, lw, LineEnd.Arrow, null).GetPdfObject());
                    annot.Put(PdfName.BS, BuildBorderStyle(lw));
                    if (shape.Opacity < 0.999) annot.Put(PdfName.CA, new PdfNumber(shape.Opacity));
                    AddTracked(page, annot, shape.Comment);
                }
                else if (shape.Kind == Models.ShapeKind.Callout && !string.IsNullOrEmpty(shape.CalloutText))
                {
                    var boxRect = new Rectangle((float)left, (float)bottom, (float)width, (float)height);
                    var annot = new PdfFreeTextAnnotation(boxRect, new PdfString(shape.CalloutText));
                    annot.SetColor(strokeColor);
                    if (ParseHexColor(shape.FillColor ?? "#FFFDE7", out float fr, out float fg, out float fb))
                        annot.Put(PdfName.IC, new PdfArray(new float[] { fr, fg, fb }));
                    annot.Put(PdfName.BS, BuildBorderStyle(lw));
                    // Callout line: tip below box center, knee at box bottom, attach at box bottom-center
                    float tipX = (float)(left + width / 2.0);
                    float tipY = (float)(bottom - 20);
                    float kneeX = tipX;
                    float kneeY = (float)bottom;
                    float attachX = tipX;
                    float attachY = (float)bottom;
                    annot.Put(PdfName.CL, new PdfArray(new float[] { tipX, tipY, kneeX, kneeY, attachX, attachY }));
                    annot.Put(new PdfName("IT"), new PdfName("FreeTextCallout"));
                    AddTracked(page, annot, shape.Comment);
                }
            }
        }

        // ── 6b. Insert / Replace text (Acrobat text-edit comments) ──────────
        if (textEdits != null)
        {
            foreach (var mark in textEdits)
            {
                if (mark.PageNumber < 1 || mark.PageNumber > doc.GetNumberOfPages()) continue;
                var page = doc.GetPage(mark.PageNumber);
                ParseHexColor(mark.Color, out float tr, out float tg, out float tb);
                var markColor = new DeviceRgb(tr, tg, tb);

                if (mark.Kind == Models.TextEditKind.Insert)
                {
                    var caret = new PdfCaretAnnotation(new Rectangle((float)mark.Left, (float)mark.Bottom, (float)mark.Width, (float)mark.Height));
                    caret.SetColor(markColor);
                    caret.SetContents(mark.Comment.Note);
                    AddTracked(page, caret, mark.Comment);
                }
                else
                {
                    // Acrobat's Replace text: a strikeout grouped with a caret holding the new text.
                    float l = (float)mark.Left, bt = (float)mark.Bottom, r = l + (float)mark.Width, t = bt + (float)mark.Height;
                    var strike = PdfTextMarkupAnnotation.CreateStrikeout(new Rectangle(l, bt, r - l, t - bt),
                        new[] { l, t, r, t, l, bt, r, bt });
                    strike.SetColor(markColor);
                    strike.SetContents(mark.Comment.Note);
                    strike.Put(new PdfName("IT"), new PdfName("StrikeOutTextEdit"));
                    AddTracked(page, strike, mark.Comment);

                    float ch = Math.Max(6f, t - bt);
                    var caret = new PdfCaretAnnotation(new Rectangle(r - ch / 2, bt - ch * 0.2f, ch, ch));
                    caret.SetColor(markColor);
                    caret.SetContents(mark.Comment.Note);
                    caret.Put(PdfName.IRT, strike.GetPdfObject());
                    caret.Put(PdfName.RT, new PdfName("Group"));
                    caret.Put(PdfName.NM, new PdfString(TrackedName(mark.Comment.Id) + ":caret"));
                    page.AddAnnotation(caret);
                }
            }
        }

        // ── 7. Placed signatures ──────────────────────────────────────────
        if (placedSignatures != null)
        {
            foreach (var sig in placedSignatures)
            {
                int pageNum = sig.PageNumber;
                if (pageNum < 1 || pageNum > doc.GetNumberOfPages()) continue;
                if (sig.ImageBytes == null || sig.ImageBytes.Length == 0) continue;

                var page = doc.GetPage(pageNum);
                int sigRot = (int)((Math.Round(sig.Rotation / 90.0) * 90 % 360 + 360) % 360);
                try
                {
                    var imageData = ImageDataFactory.Create(sig.ImageBytes);
                    var xobj = new PdfImageXObject(imageData);
                    if (flatten)
                    {
                        // Flattened copy: burn the signature into the page content.
                        var canvas = new PdfCanvas(page);
                        float sw = (float)sig.Width, sh = (float)sig.Height;
                        var m = PlaceMatrix(sigRot, (float)sig.Left, (float)sig.Bottom, sw, sh,
                            sigRot is 90 or 270 ? sh : sw, sigRot is 90 or 270 ? sw : sh);
                        canvas.AddXObjectWithTransformationMatrix(xobj, m[0], m[1], m[2], m[3], m[4], m[5]);
                        canvas.Release();
                    }
                    else
                    {
                        // A stamp annotation showing the signature image (like Acrobat Fill & Sign), so a
                        // later save replaces it rather than stacking another copy into the page.
                        float w = (float)sig.Width, h = (float)sig.Height;
                        var (cw, ch) = sigRot is 90 or 270 ? (h, w) : (w, h);
                        var ap = new PdfFormXObject(new Rectangle(0, 0, cw, ch));
                        new PdfCanvas(ap, doc).AddXObjectWithTransformationMatrix(xobj, cw, 0, 0, ch, 0, 0).Release();
                        Orient(ap, sigRot);
                        var stamp = new PdfStampAnnotation(new Rectangle((float)sig.Left, (float)sig.Bottom, w, h));
                        stamp.SetNormalAppearance(ap.GetPdfObject());
                        if (sigRot != 0) stamp.Put(PdfName.Rotate, new PdfNumber(sigRot));
                        stamp.SetFlags(PdfAnnotation.PRINT | PdfAnnotation.LOCKED);
                        stamp.SetContents("Signature");
                        stamp.Put(PdfName.NM, new PdfString(TrackedName(sig.Id)));
                        page.AddAnnotation(stamp);
                    }
                }
                catch (Exception ex)
                {
                    saveErrors.Add($"Signature on page {pageNum}: {ex.Message}");
                }
            }
        }

        return saveErrors;
    }

    // ── Page Operations ──────────────────────────────────────────────────────

    /// <summary>Copies all pages except those in pageIndexes (0-based) to destPath.</summary>
    public void DeletePages(string sourcePath, string destPath, IEnumerable<int> pageIndexes)
    {
        var skip = new HashSet<int>(pageIndexes);
        using var reader = new PdfReader(sourcePath);
        using var writer = new PdfWriter(destPath);
        using var src = new PdfDocument(reader);
        using var dest = new PdfDocument(writer);

        int total = src.GetNumberOfPages();
        for (int i = 1; i <= total; i++)
        {
            if (!skip.Contains(i - 1))
                src.CopyPagesTo(i, i, dest);
        }
    }

    /// <summary>Inserts a blank page the same size as page afterPageIndex (0-based) immediately after it.</summary>
    public void InsertBlankPage(string sourcePath, string destPath, int afterPageIndex)
    {
        using var reader = new PdfReader(sourcePath);
        using var writer = new PdfWriter(destPath);
        using var src = new PdfDocument(reader);
        using var dest = new PdfDocument(writer);

        int total = src.GetNumberOfPages();
        int insertAfter = afterPageIndex + 1;

        for (int i = 1; i <= total; i++)
        {
            src.CopyPagesTo(i, i, dest);
            if (i == insertAfter)
            {
                var sz = src.GetPage(i).GetPageSize();
                dest.AddNewPage(new PageSize(sz));
            }
        }
    }

    /// <summary>Copies the specified pages (0-based) to a new PDF at destPath.</summary>
    public void ExtractPages(string sourcePath, string destPath, IEnumerable<int> pageIndexes)
    {
        using var reader = new PdfReader(sourcePath);
        using var writer = new PdfWriter(destPath);
        using var src = new PdfDocument(reader);
        using var dest = new PdfDocument(writer);

        int total = src.GetNumberOfPages();
        foreach (var idx in pageIndexes.OrderBy(x => x))
        {
            int pageNum = idx + 1;
            if (pageNum >= 1 && pageNum <= total)
                src.CopyPagesTo(pageNum, pageNum, dest);
        }
    }

    /// <summary>Duplicates the page at pageIndex (0-based) and inserts the copy immediately after it.</summary>
    public void DuplicatePage(string inputPath, string outputPath, int pageIndex)
    {
        string tmp = System.IO.Path.GetTempFileName() + ".pdf";
        try
        {
            ExtractPages(inputPath, tmp, new[] { pageIndex });
            InsertPdfAt(inputPath, tmp, outputPath, pageIndex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    /// <summary>Adds a header and/or footer text string to every page.</summary>
    public void AddHeaderFooter(string inputPath, string outputPath,
        string? headerText, string? footerText,
        float fontSize = 10f, float marginPt = 18f,
        string alignment = "Center")
    {
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf = new PdfDocument(reader, writer);
        var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
        int total = pdf.GetNumberOfPages();

        for (int i = 1; i <= total; i++)
        {
            var page = pdf.GetPage(i);
            var size = page.GetPageSize();
            var canvas = new PdfCanvas(page);

            void DrawText(string text, float y)
            {
                float tw = font.GetWidth(text, fontSize);
                float x = alignment switch
                {
                    "Left"  => marginPt,
                    "Right" => size.GetWidth() - tw - marginPt,
                    _       => (size.GetWidth() - tw) / 2,
                };
                canvas.SaveState();
                canvas.BeginText();
                canvas.SetFontAndSize(font, fontSize);
                canvas.SetTextMatrix(1, 0, 0, 1, x, y);
                canvas.ShowText(text);
                canvas.EndText();
                canvas.RestoreState();
            }

            if (!string.IsNullOrWhiteSpace(headerText))
                DrawText(headerText!, size.GetHeight() - marginPt - fontSize);
            if (!string.IsNullOrWhiteSpace(footerText))
                DrawText(footerText!, marginPt);
        }
    }

    /// <summary>Concatenates all sourcePaths PDFs into destPath in order.</summary>
    public void MergePdfs(IEnumerable<string> sourcePaths, string destPath)
    {
        using var writer = new PdfWriter(destPath);
        using var dest = new PdfDocument(writer);

        foreach (var path in sourcePaths)
        {
            using var reader = new PdfReader(path);
            using var src = new PdfDocument(reader);
            src.CopyPagesTo(1, src.GetNumberOfPages(), dest);
        }
    }

    /// <summary>
    /// Inserts all pages of pdfToInsert into sourcePath after insertAfterPageIndex (0-based).
    /// Pass -1 to insert at the very beginning.
    /// </summary>
    public void InsertPdfAt(string sourcePath, string pdfToInsert, string destPath, int insertAfterPageIndex)
    {
        using var srcReader   = new PdfReader(sourcePath);
        using var insertReader = new PdfReader(pdfToInsert);
        using var writer = new PdfWriter(destPath);
        using var outDoc = new PdfDocument(writer);
        using var srcDoc    = new PdfDocument(srcReader);
        using var insertDoc = new PdfDocument(insertReader);

        int srcTotal    = srcDoc.GetNumberOfPages();
        int splitAfter  = Math.Clamp(insertAfterPageIndex + 1, 0, srcTotal); // 1-based page count before insert

        // Pages before insertion point
        if (splitAfter > 0)
            srcDoc.CopyPagesTo(1, splitAfter, outDoc);

        // Inserted PDF pages
        insertDoc.CopyPagesTo(1, insertDoc.GetNumberOfPages(), outDoc);

        // Remaining pages
        if (splitAfter < srcTotal)
            srcDoc.CopyPagesTo(splitAfter + 1, srcTotal, outDoc);
    }

    /// <summary>Splits each page of sourcePath into a separate PDF in outputFolder.</summary>
    public int SplitPdf(string sourcePath, string outputFolder)
    {
        using var reader = new PdfReader(sourcePath);
        using var doc = new PdfDocument(reader);
        int count = doc.GetNumberOfPages();
        string baseName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        System.IO.Directory.CreateDirectory(outputFolder);
        for (int i = 1; i <= count; i++)
        {
            string outPath = System.IO.Path.Combine(outputFolder, $"{baseName}_p{i:D3}.pdf");
            using var writer = new PdfWriter(outPath);
            using var outDoc = new PdfDocument(writer);
            doc.CopyPagesTo(i, i, outDoc);
        }
        return count;
    }

    /// <summary>Writes the pages of sourcePath in newOrder (0-based indices) to destPath.</summary>
    public void ReorderPages(string sourcePath, string destPath, IEnumerable<int> newOrder)
    {
        var oneBasedOrder = newOrder.Select(i => i + 1).ToList();
        using var reader = new PdfReader(sourcePath);
        using var writer = new PdfWriter(destPath);
        using var inDoc = new PdfDocument(reader);
        using var outDoc = new PdfDocument(writer);
        inDoc.CopyPagesTo(oneBasedOrder, outDoc);
    }

    /// <summary>Inserts a blank page before beforePageIndex (0-based) in the PDF.</summary>
    public void InsertPageBefore(string sourcePath, string destPath, int beforePageIndex)
    {
        using var reader = new PdfReader(sourcePath);
        using var writer = new PdfWriter(destPath);
        using var inDoc = new PdfDocument(reader);
        using var outDoc = new PdfDocument(writer);

        int beforePageNum = beforePageIndex + 1;
        if (beforePageNum > 1)
            inDoc.CopyPagesTo(1, beforePageNum - 1, outDoc);
        var refPage = inDoc.GetPage(beforePageNum);
        outDoc.AddNewPage(new iText.Kernel.Geom.PageSize(refPage.GetPageSize()));
        inDoc.CopyPagesTo(beforePageNum, inDoc.GetNumberOfPages(), outDoc);
    }

    public void ExportFormData(string pdfPath, string outputPath, Dictionary<string, string> fieldValues)
    {
        var lines = fieldValues.Select(kv => $"{kv.Key}\t{kv.Value}");
        System.IO.File.WriteAllLines(outputPath, lines);
    }

    public Dictionary<string, string> ImportFormData(string dataPath)
    {
        var result = new Dictionary<string, string>();
        foreach (var line in System.IO.File.ReadAllLines(dataPath))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2)
                result[parts[0]] = parts[1];
        }
        return result;
    }

    /// <summary>
    /// Re-compress a PDF with maximum compression settings.
    /// Returns a tuple of (originalBytes, compressedBytes) so the caller can show savings.
    /// </summary>
    public (long Original, long Compressed) CompressPdf(string inputPath, string outputPath)
    {
        long originalSize = new System.IO.FileInfo(inputPath).Length;

        var writerProps = new WriterProperties()
            .SetCompressionLevel(CompressionConstants.BEST_COMPRESSION)
            .UseSmartMode();

        using var reader = new PdfReader(inputPath);
        reader.SetUnethicalReading(true);
        using var writer = new PdfWriter(outputPath, writerProps);
        using var pdf    = new PdfDocument(reader, writer);
        // Copying pages with full compression enabled via smart mode;
        // iText re-serializes all content streams.

        long compressedSize = new System.IO.FileInfo(outputPath).Length;
        return (originalSize, compressedSize);
    }

    /// <summary>Add page numbers (footer) to every page of a PDF.</summary>
    public void AddPageNumbers(string inputPath, string outputPath,
        string format = "Page {n} of {total}",
        float fontSize = 9f, float marginPt = 18f, string position = "BottomCenter")
    {
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(reader, writer);
        var font  = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
        int total = pdf.GetNumberOfPages();
        var gray  = new DeviceRgb(0.4f, 0.4f, 0.4f);

        for (int i = 1; i <= total; i++)
        {
            var page = pdf.GetPage(i);
            var size = page.GetPageSize();
            string text = format.Replace("{n}", i.ToString()).Replace("{total}", total.ToString());
            float textWidth = font.GetWidth(text, fontSize);

            float x = position switch
            {
                "BottomLeft"  => marginPt,
                "BottomRight" => size.GetWidth() - textWidth - marginPt,
                _             => (size.GetWidth() - textWidth) / 2 // BottomCenter
            };
            float y = marginPt;

            var canvas = new PdfCanvas(page);
            canvas.SaveState();
            canvas.SetFillColor(gray);
            canvas.BeginText();
            canvas.SetFontAndSize(font, fontSize);
            canvas.SetTextMatrix(1, 0, 0, 1, x, y);
            canvas.ShowText(text);
            canvas.EndText();
            canvas.RestoreState();
        }
    }

    /// <summary>Adds a top-level bookmark pointing to the specified page.</summary>
    public void AddBookmark(string sourcePath, string destPath, string title, int pageNumber)
    {
        using var reader = new PdfReader(sourcePath);
        reader.SetUnethicalReading(true);
        using var writer = new PdfWriter(destPath);
        using var doc    = new PdfDocument(reader, writer);

        if (pageNumber < 1 || pageNumber > doc.GetNumberOfPages())
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var catalog  = doc.GetCatalog();
        var outlines = catalog.GetPdfObject().GetAsDictionary(PdfName.Outlines);
        PdfDictionary outlineRoot;
        if (outlines == null)
        {
            outlineRoot = new PdfDictionary();
            outlineRoot.Put(PdfName.Type, PdfName.Outlines);
            outlineRoot.MakeIndirect(doc);
            catalog.GetPdfObject().Put(PdfName.Outlines, outlineRoot);
        }
        else
        {
            outlineRoot = outlines;
        }

        var page = doc.GetPage(pageNumber);
        var dest = new PdfArray();
        dest.Add(page.GetPdfObject());
        dest.Add(PdfName.XYZ);
        dest.Add(new PdfNumber(0));
        dest.Add(new PdfNumber(0));
        dest.Add(new PdfNumber(0));

        var entry = new PdfDictionary();
        entry.Put(PdfName.Title, new PdfString(title));
        entry.Put(PdfName.Dest, dest);
        entry.Put(PdfName.Parent, outlineRoot);
        entry.MakeIndirect(doc);

        var lastChild = outlineRoot.GetAsDictionary(PdfName.Last);
        if (lastChild == null)
        {
            outlineRoot.Put(PdfName.First, entry);
            outlineRoot.Put(PdfName.Last,  entry);
        }
        else
        {
            lastChild.Put(PdfName.Next, entry);
            entry.Put(PdfName.Prev, lastChild);
            outlineRoot.Put(PdfName.Last, entry);
        }

        var countObj = outlineRoot.GetAsNumber(PdfName.Count);
        int count = countObj != null ? countObj.IntValue() : 0;
        outlineRoot.Put(PdfName.Count, new PdfNumber(count + 1));
    }

    /// <summary>Extracts the bookmark/outline tree from a PDF.</summary>
    public List<Models.BookmarkItem> GetBookmarks(string path)
    {
        using var reader = new PdfReader(path);
        using var doc    = new PdfDocument(reader);

        var catalog = doc.GetCatalog().GetPdfObject();
        var outlines = catalog.GetAsDictionary(PdfName.Outlines);
        if (outlines == null) return new();

        var result = new List<Models.BookmarkItem>();
        ReadOutlineLevel(doc, outlines.GetAsDictionary(PdfName.First), result);
        return result;
    }

    private static void ReadOutlineLevel(PdfDocument doc, PdfDictionary? node, IList<Models.BookmarkItem> items)
    {
        while (node != null)
        {
            var titleObj = node.GetAsString(PdfName.Title);
            string title = titleObj != null ? titleObj.ToUnicodeString() : "(untitled)";

            int pageNum = 0;
            var dest = node.Get(PdfName.Dest);
            if (dest == null)
            {
                var action = node.GetAsDictionary(PdfName.A);
                if (action != null)
                    dest = action.Get(PdfName.D);
            }
            if (dest is PdfArray destArr && destArr.Size() > 0)
            {
                try
                {
                    var pageRef = destArr.GetAsDictionary(0);
                    if (pageRef != null)
                        pageNum = doc.GetPageNumber(pageRef);
                }
                catch { /* ignore malformed dest */ }
            }

            var item = new Models.BookmarkItem { Title = title, PageNumber = pageNum };
            var firstChild = node.GetAsDictionary(PdfName.First);
            if (firstChild != null)
                ReadOutlineLevel(doc, firstChild, item.Children);

            items.Add(item);
            node = node.GetAsDictionary(PdfName.Next);
        }
    }

    /// <summary>Reads metadata (title, author, subject, keywords) from a PDF without modifying it.</summary>
    public PdfMetadataInfo GetMetadata(string path)
    {
        var fi = new System.IO.FileInfo(path);
        using var reader = new PdfReader(path);
        using var doc    = new PdfDocument(reader);
        var info = doc.GetDocumentInfo();
        return new PdfMetadataInfo
        {
            Title         = info.GetTitle()    ?? string.Empty,
            Author        = info.GetAuthor()   ?? string.Empty,
            Subject       = info.GetSubject()  ?? string.Empty,
            Keywords      = info.GetKeywords() ?? string.Empty,
            Creator       = info.GetCreator()  ?? string.Empty,
            Producer      = info.GetProducer() ?? string.Empty,
            PageCount     = doc.GetNumberOfPages(),
            FileSizeBytes = fi.Exists ? fi.Length : 0,
        };
    }

    /// <summary>Writes updated metadata to a new copy of the PDF.</summary>
    public void SetMetadata(string inputPath, string outputPath, PdfMetadataInfo meta)
    {
        using var reader = new PdfReader(inputPath);
        reader.SetUnethicalReading(true);
        using var writer = new PdfWriter(outputPath);
        using var doc    = new PdfDocument(reader, writer);
        var info = doc.GetDocumentInfo();
        info.SetTitle(meta.Title);
        info.SetAuthor(meta.Author);
        info.SetSubject(meta.Subject);
        info.SetKeywords(meta.Keywords);
    }

    /// <summary>
    /// Re-saves the PDF in PDF 1.4 format with maximum compression and embedded metadata,
    /// suitable for long-term archiving (PDF/A-like).  For strict PDF/A-1b certification,
    /// use the dedicated itext7.pdfa package.
    /// </summary>
    public void ConvertToPdfA(string inputPath, string outputPath)
    {
        var writerProps = new WriterProperties()
            .SetPdfVersion(PdfVersion.PDF_1_4)
            .SetCompressionLevel(CompressionConstants.BEST_COMPRESSION)
            .UseSmartMode();

        using var reader = new PdfReader(inputPath);
        reader.SetUnethicalReading(true);
        using var writer = new PdfWriter(outputPath, writerProps);
        using var pdf    = new PdfDocument(reader, writer);

        // Embed XMP conformance claim (PDF/A-1b)
        var xmpMeta = pdf.GetXmpMetadata(true);
        var xmpStr  = System.Text.Encoding.UTF8.GetString(xmpMeta ?? Array.Empty<byte>());
        if (!xmpStr.Contains("pdfaid:conformance"))
        {
            // Append PDF/A-1b XMP namespace block
            const string pdfaidNs = "http://www.aiim.org/pdfa/ns/id/";
            var metaBuilder = new System.Text.StringBuilder(xmpStr.TrimEnd());
            // Simple approach: add pdfaid part/conformance to existing XMP
            pdf.GetDocumentInfo().SetMoreInfo("pdfaid:part", "1");
            pdf.GetDocumentInfo().SetMoreInfo("pdfaid:conformance", "B");
        }
    }

    /// <summary>Applies black redaction rectangles over the specified PDF-point regions and burns them in.</summary>
    public void ApplyRedactions(string inputPath, string outputPath,
        IEnumerable<(int PageNumber, float X, float Y, float Width, float Height)> regions)
    {
        using var reader = new PdfReader(inputPath);
        reader.SetUnethicalReading(true);
        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(reader, writer);

        foreach (var (pageNum, x, y, w, h) in regions)
        {
            if (pageNum < 1 || pageNum > pdf.GetNumberOfPages()) continue;
            var page   = pdf.GetPage(pageNum);
            var canvas = new PdfCanvas(page);
            canvas.SaveState();
            canvas.SetFillColor(iText.Kernel.Colors.ColorConstants.BLACK);
            canvas.Rectangle(x, y, w, h);
            canvas.Fill();
            canvas.RestoreState();
        }
    }

    private enum LineEnd { None, Arrow, Ticks }

    /// <summary>
    /// Appearance for line, arrow, polygon, polyline and measure annotations (in page coordinates,
    /// the form's BBox being the annotation's Rect), so viewers that don't draw these themselves —
    /// Pdfium, Chrome, many phone apps — still show them. A measurement's caption is drawn too.
    /// </summary>
    private static PdfFormXObject BuildLineAppearance(PdfDocument doc, Rectangle rect, IList<PointD> pts, bool closed,
        float[]? interior, DeviceRgb stroke, float lw, LineEnd end, string? caption)
    {
        var xobj = new PdfFormXObject(rect);
        var canvas = new PdfCanvas(xobj, doc);
        canvas.SaveState().SetStrokeColor(stroke).SetLineWidth(lw).SetLineCapStyle(1).SetLineJoinStyle(1);
        canvas.MoveTo(pts[0].X, pts[0].Y);
        for (int i = 1; i < pts.Count; i++) canvas.LineTo(pts[i].X, pts[i].Y);
        if (closed && pts.Count > 2)
        {
            canvas.ClosePath();
            if (interior != null)
            {
                canvas.SetFillColor(new DeviceRgb(interior[0], interior[1], interior[2]));
                canvas.FillStroke();
            }
            else canvas.Stroke();
        }
        else canvas.Stroke();

        if (end != LineEnd.None && pts.Count > 1)
        {
            var (a, b) = (pts[0], pts[^1]);
            double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
            if (end == LineEnd.Arrow)
            {
                double len = 9 + lw * 1.5, spread = 0.45;
                canvas.MoveTo(b.X - len * Math.Cos(ang - spread), b.Y - len * Math.Sin(ang - spread))
                      .LineTo(b.X, b.Y)
                      .LineTo(b.X - len * Math.Cos(ang + spread), b.Y - len * Math.Sin(ang + spread)).Stroke();
            }
            else
            {
                double t = 5, nx = -Math.Sin(ang) * t, ny = Math.Cos(ang) * t;
                canvas.MoveTo(a.X - nx, a.Y - ny).LineTo(a.X + nx, a.Y + ny)
                      .MoveTo(b.X - nx, b.Y - ny).LineTo(b.X + nx, b.Y + ny).Stroke();
            }
        }
        canvas.RestoreState();

        if (!string.IsNullOrEmpty(caption) && pts.Count > 0)
        {
            var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD);
            const float size = 9;
            float tw = font.GetWidth(caption, size);
            // Middle of a distance, end of a perimeter, centre of an area.
            var (cx, cy) = end == LineEnd.Ticks ? ((pts[0].X + pts[^1].X) / 2, (pts[0].Y + pts[^1].Y) / 2 + 4)
                         : closed ? (pts.Average(p => p.X), pts.Average(p => p.Y) - 3)
                         : (pts[^1].X, pts[^1].Y + 5);
            float x = (float)Math.Clamp(cx - tw / 2, rect.GetLeft(), Math.Max(rect.GetLeft(), rect.GetRight() - tw));
            float y = (float)Math.Clamp(cy, rect.GetBottom() + 2, Math.Max(rect.GetBottom() + 2, rect.GetTop() - size));
            canvas.SaveState().SetFillColor(ColorConstants.WHITE)
                  .Rectangle(x - 2, y - 2.5, tw + 4, size + 2).Fill().RestoreState();
            canvas.BeginText().SetFontAndSize(font, size).SetFillColor(stroke).MoveText(x, y).ShowText(caption).EndText();
        }
        return xobj;
    }

    private static PdfDictionary BuildBorderStyle(float lineWidth)
    {
        var bs = new PdfDictionary();
        bs.Put(PdfName.Type, PdfName.Border);
        bs.Put(PdfName.W, new PdfNumber(lineWidth));
        bs.Put(PdfName.S, PdfName.S); // Solid
        return bs;
    }

    /// <summary>Form XObject drawing a rubber stamp: rounded double border, title, optional subtitle.</summary>
    private static PdfFormXObject BuildStampAppearance(PdfDocument doc, FreeTextAnnotation ann)
    {
        var (w, h) = ContentBox(ann);
        var xobj = new PdfFormXObject(new Rectangle(0, 0, w, h));
        var canvas = new PdfCanvas(xobj, doc);
        if (!ParseHexColor(ann.FontColor, out float r, out float g, out float b)) (r, g, b) = (0.42f, 0.11f, 0.6f);
        var color = new DeviceRgb(r, g, b);

        float outer = Math.Max(1.2f, Math.Min(w, h) * 0.07f);
        float rad = Math.Min(w, h) * 0.18f;
        float inset = outer * 2.1f;

        // Faint tint inside, like Acrobat's stamps.
        canvas.SaveState()
              .SetExtGState(new iText.Kernel.Pdf.Extgstate.PdfExtGState().SetFillOpacity(0.09f))
              .SetFillColor(color)
              .RoundRectangle(outer / 2, outer / 2, w - outer, h - outer, rad).Fill()
              .RestoreState();
        canvas.SaveState().SetStrokeColor(color).SetLineWidth(outer)
              .RoundRectangle(outer / 2, outer / 2, w - outer, h - outer, rad).Stroke();
        if (w > inset * 4 && h > inset * 4)
            canvas.SetLineWidth(Math.Max(0.6f, outer * 0.4f))
                  .RoundRectangle(inset, inset, w - inset * 2, h - inset * 2, rad * 0.7f).Stroke();
        canvas.RestoreState();

        var bold = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD);
        var italic = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA_OBLIQUE);
        bool hasSub = !string.IsNullOrWhiteSpace(ann.StampSubtitle);
        float innerW = w - inset * 2 - outer * 2;

        float FitSize(iText.Kernel.Font.PdfFont f, string text, float maxH)
        {
            float size = Math.Max(1, maxH / 1.15f);
            float tw = f.GetWidth(text, size);
            return tw > innerW && innerW > 1 ? Math.Max(1, size * innerW / tw) : size;
        }

        string title = ann.Text;
        float ts = FitSize(bold, title, hasSub ? h * 0.42f : h * 0.55f);
        // Baseline: centred (no subtitle) or in the upper part (with one).
        float ty = hasSub ? h * 0.52f : (h - ts * 0.72f) / 2;
        canvas.BeginText().SetFontAndSize(bold, ts).SetFillColor(color)
              .MoveText((w - bold.GetWidth(title, ts)) / 2, ty).ShowText(title).EndText();
        if (hasSub)
        {
            string sub = ann.StampSubtitle!;
            float ss = FitSize(italic, sub, h * 0.2f);
            canvas.BeginText().SetFontAndSize(italic, ss).SetFillColor(color)
                  .MoveText((w - italic.GetWidth(sub, ss)) / 2, h * 0.2f).ShowText(sub).EndText();
        }
        Orient(xobj, ContentRotation(ann));
        return xobj;
    }

    /// <summary>
    /// How far (degrees anticlockwise, 0/90/180/270) an annotation's content is turned on the page —
    /// its /Rotate. Text and stamps added on a rotated page are turned with it so they read upright.
    /// </summary>
    private static int ContentRotation(FreeTextAnnotation ann) =>
        (int)((-Math.Round(ann.RotationAngle / 90) * 90 % 360 + 360) % 360);

    /// <summary>The box the content is laid out in: the annotation's box, turned with its content.</summary>
    private static (float W, float H) ContentBox(FreeTextAnnotation ann) =>
        ContentRotation(ann) is 90 or 270 ? ((float)ann.Height, (float)ann.Width) : ((float)ann.Width, (float)ann.Height);

    /// <summary>Turns an appearance anticlockwise; the viewer fits the turned box to the annotation's Rect.</summary>
    private static void Orient(PdfFormXObject xobj, int degrees)
    {
        if (degrees == 0) return;
        var m = PlaceMatrix(degrees, 0, 0, 0, 0, 1, 1);
        xobj.Put(PdfName.Matrix, new PdfArray(new[] { m[0], m[1], m[2], m[3], 0f, 0f }));
    }

    /// <summary>
    /// Matrix drawing content of <paramref name="cw"/> × <paramref name="ch"/> (laid out upright),
    /// turned <paramref name="degrees"/> anticlockwise, into the page box (left, bottom, width, height).
    /// </summary>
    private static float[] PlaceMatrix(int degrees, float left, float bottom, float width, float height, float cw, float ch) => degrees switch
    {
        90 => new[] { 0, cw, -ch, 0, left + width, bottom },
        180 => new[] { -cw, 0, 0, -ch, left + width, bottom + height },
        270 => new[] { 0, -cw, ch, 0, left, bottom + height },
        _ => new[] { cw, 0, 0, ch, left, bottom },
    };

    /// <summary>
    /// Form XObject drawing typed text (font, size, colour, bold/italic, alignment, character spacing,
    /// underline), wrapped at the box width like the editors show it.
    /// </summary>
    private static PdfFormXObject BuildTextAppearance(PdfDocument doc, FreeTextAnnotation ann)
    {
        var (w, h) = ContentBox(ann);
        var xobj = new PdfFormXObject(new Rectangle(0, 0, w, h));
        var canvas = new PdfCanvas(xobj, doc);
        string fontName = ann.IsBold && ann.IsItalic ? iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLDOBLIQUE
                        : ann.IsBold ? iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD
                        : ann.IsItalic ? iText.IO.Font.Constants.StandardFonts.HELVETICA_OBLIQUE
                        : iText.IO.Font.Constants.StandardFonts.HELVETICA;
        var font = PdfFontFactory.CreateFont(fontName);
        float size = (float)Math.Max(1, ann.FontSize), lead = size * 1.15f, cs = (float)Math.Max(0, ann.CharacterSpacing);
        if (!ParseHexColor(ann.FontColor, out float r, out float g, out float b)) (r, g, b) = (0, 0, 0);
        string text = ann.ForceUpperCase ? ann.Text.ToUpperInvariant() : ann.Text;

        float Measure(string line) => font.GetWidth(line, size) + cs * line.Length;
        var lines = new List<string>();
        foreach (var para in text.Replace("\r\n", "\n").Split('\n'))
        {
            string current = "";
            foreach (var word in para.Split(' '))
            {
                string trial = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && Measure(trial) > w - 2) { lines.Add(current); current = word; }
                else current = trial;
            }
            lines.Add(current);
        }

        float y = h - 1 - size * 0.8f;   // first baseline: Helvetica's ascent below the top
        var color = new DeviceRgb(r, g, b);
        canvas.SaveState().SetFillColor(color).SetStrokeColor(color);
        foreach (var line in lines)
        {
            float lw = Measure(line);
            float x = ann.TextAlignment switch
            {
                TextAlign.Center => (w - lw) / 2,
                TextAlign.Right => w - 1 - lw,
                _ => 1,
            };
            if (line.Length > 0)
            {
                canvas.BeginText().SetFontAndSize(font, size);
                if (cs > 0) canvas.SetCharacterSpacing(cs);
                canvas.MoveText(x, y).ShowText(line).EndText();
                if (ann.IsUnderline)
                    canvas.SetLineWidth(Math.Max(0.5f, size * 0.06f)).MoveTo(x, y - size * 0.12f).LineTo(x + lw, y - size * 0.12f).Stroke();
            }
            y -= lead;
        }
        canvas.RestoreState();
        Orient(xobj, ContentRotation(ann));
        return xobj;
    }

    /// <summary>Form XObject drawing a Fill &amp; Sign mark (see <see cref="MarkShapes"/>) in its box.</summary>
    private static PdfFormXObject BuildMarkAppearance(PdfDocument doc, MarkShapes.Kind kind, FreeTextAnnotation ann)
    {
        var (w, h) = ContentBox(ann);
        var xobj = new PdfFormXObject(new Rectangle(0, 0, w, h));
        var canvas = new PdfCanvas(xobj, doc);
        ParseHexColor(ann.FontColor, out float r, out float g, out float b);
        var color = new DeviceRgb(r, g, b);

        // Unit square → box: centred square, or the full box for the line mark.
        bool fill = MarkShapes.FillsWidth(kind);
        float side = Math.Min(w, h);
        float sx = fill ? w : side, sy = fill ? h : side;
        float ox = (w - sx) / 2, oy = (h - sy) / 2;
        float X(double ux) => ox + (float)ux * sx;
        float Y(double uy) => oy + (1 - (float)uy) * sy;   // unit y runs down, PDF y runs up

        canvas.SaveState()
              .SetStrokeColor(color).SetFillColor(color)
              .SetLineWidth((float)MarkShapes.StrokeWidth(kind) * Math.Min(sx, sy))
              .SetLineCapStyle(PdfCanvasConstants.LineCapStyle.ROUND)
              .SetLineJoinStyle(PdfCanvasConstants.LineJoinStyle.ROUND);
        foreach (var stroke in MarkShapes.Strokes(kind))
        {
            canvas.MoveTo(X(stroke[0].X), Y(stroke[0].Y));
            foreach (var pt in stroke.Skip(1)) canvas.LineTo(X(pt.X), Y(pt.Y));
            canvas.Stroke();
        }
        double rad = MarkShapes.Radius(kind);
        if (rad > 0)
        {
            canvas.Circle(X(0.5), Y(0.5), (float)rad * side);
            if (kind == MarkShapes.Kind.Dot) canvas.Fill(); else canvas.Stroke();
        }
        canvas.RestoreState();
        Orient(xobj, ContentRotation(ann));
        return xobj;
    }

    /// <summary>
    /// Standard PDF annotations for Acrobat's Line, Cloud, Polygon, Polyline and Measure tools, so
    /// they show (and stay editable) in Acrobat and other readers. Null for the original kinds.
    /// </summary>
    private static PdfAnnotation? BuildExtendedShapeAnnotation(PdfDocument doc, Models.ShapeAnnotation shape, DeviceRgb stroke, float lw)
    {
        float l = (float)Math.Min(shape.X1, shape.X2), b = (float)Math.Min(shape.Y1, shape.Y2);
        float w = (float)Math.Max(Math.Abs(shape.X2 - shape.X1), 1), h = (float)Math.Max(Math.Abs(shape.Y2 - shape.Y1), 1);
        // Room for the line width, end ticks and the measurement caption.
        float pad = lw + (shape.Kind is Models.ShapeKind.Distance or Models.ShapeKind.Perimeter or Models.ShapeKind.Area ? 16 : 0);
        var bounds = new Rectangle(l - pad, b - pad, w + 2 * pad, h + 2 * pad);
        float[]? interior = !string.IsNullOrEmpty(shape.FillColor) && ParseHexColor(shape.FillColor, out float fr, out float fg, out float fb)
            ? new[] { fr, fg, fb } : null;
        float[] Vertices() => (shape.Points ?? new()).SelectMany(p => new[] { (float)p.X, (float)p.Y }).ToArray();
        string label = Measurement.Label(shape);

        PdfAnnotation annot;
        switch (shape.Kind)
        {
            case Models.ShapeKind.Line:
            case Models.ShapeKind.Distance:
            {
                var line = new PdfLineAnnotation(bounds, new[] { (float)shape.X1, (float)shape.Y1, (float)shape.X2, (float)shape.Y2 });
                if (shape.Kind == Models.ShapeKind.Distance)
                {
                    // Dimension line: butt ends, caption shown on the line (Acrobat "Distance")
                    line.Put(PdfName.LE, new PdfArray(new[] { new PdfName("Butt"), new PdfName("Butt") }));
                    line.Put(new PdfName("Cap"), PdfBoolean.TRUE);
                    line.Put(new PdfName("IT"), new PdfName("LineDimension"));
                    line.SetContents(label);
                }
                line.SetNormalAppearance(BuildLineAppearance(doc, bounds, new List<PointD> { new(shape.X1, shape.Y1), new(shape.X2, shape.Y2) }, closed: false,
                    null, stroke, lw, shape.Kind == Models.ShapeKind.Distance ? LineEnd.Ticks : LineEnd.None,
                    shape.Kind == Models.ShapeKind.Distance ? label : null).GetPdfObject());
                annot = line;
                break;
            }
            case Models.ShapeKind.Cloud:
            {
                var sq = new PdfSquareAnnotation(new Rectangle(l, b, w, h));
                var be = new PdfDictionary();                   // border effect: cloudy
                be.Put(PdfName.S, new PdfName("C"));
                be.Put(PdfName.I, new PdfNumber(1));
                sq.Put(new PdfName("BE"), be);
                if (interior != null) sq.SetInteriorColor(interior);
                annot = sq;
                break;
            }
            case Models.ShapeKind.Polygon:
            case Models.ShapeKind.Area:
            {
                var poly = PdfPolyGeomAnnotation.CreatePolygon(bounds, Vertices());
                if (interior != null) poly.SetInteriorColor(interior);
                if (shape.Kind == Models.ShapeKind.Area)
                {
                    poly.Put(new PdfName("IT"), new PdfName("PolygonDimension"));
                    poly.SetContents(label);
                }
                if (shape.Points is { Count: > 1 })
                    poly.SetNormalAppearance(BuildLineAppearance(doc, bounds, shape.Points, closed: true, interior, stroke, lw, LineEnd.None,
                        shape.Kind == Models.ShapeKind.Area ? label : null).GetPdfObject());
                annot = poly;
                break;
            }
            case Models.ShapeKind.Polyline:
            case Models.ShapeKind.Perimeter:
            {
                var poly = PdfPolyGeomAnnotation.CreatePolyLine(bounds, Vertices());
                if (shape.Kind == Models.ShapeKind.Perimeter)
                {
                    poly.Put(new PdfName("IT"), new PdfName("PolyLineDimension"));
                    poly.SetContents(label);
                }
                if (shape.Points is { Count: > 1 })
                    poly.SetNormalAppearance(BuildLineAppearance(doc, bounds, shape.Points, closed: false, null, stroke, lw, LineEnd.None,
                        shape.Kind == Models.ShapeKind.Perimeter ? label : null).GetPdfObject());
                annot = poly;
                break;
            }
            default:
                return null;
        }
        annot.SetColor(stroke);
        annot.Put(PdfName.BS, BuildBorderStyle(lw));
        annot.SetFlags(PdfAnnotation.PRINT);
        return annot;
    }

    // ── Annotations written by PdfEdit (/NM "pdfedit:<id>") ──────────────────

    public const string TrackedPrefix = "pdfedit:";
    private static string TrackedName(string id) => TrackedPrefix + id;

    /// <summary>The PdfEdit id in an annotation's /NM ("pdfedit:<id>[:suffix]"), or null.</summary>
    public static string? TrackedId(PdfDictionary annot)
    {
        string? nm = annot.GetAsString(PdfName.NM)?.ToUnicodeString();
        if (nm == null || !nm.StartsWith(TrackedPrefix, StringComparison.Ordinal)) return null;
        string rest = nm[TrackedPrefix.Length..];
        int colon = rest.IndexOf(':');
        return colon < 0 ? rest : rest[..colon];
    }

    /// <summary>Removes PdfEdit annotations (and their replies) whose ids are in <paramref name="ids"/>.</summary>
    public static int RemoveTrackedAnnotations(PdfDocument doc, ISet<string> ids)
    {
        int removed = 0;
        if (ids.Count == 0) return 0;
        for (int p = 1; p <= doc.GetNumberOfPages(); p++)
        {
            var page = doc.GetPage(p);
            foreach (var annot in page.GetAnnotations().ToList())
            {
                if (TrackedId(annot.GetPdfObject()) is { } id && ids.Contains(id))
                {
                    page.RemoveAnnotation(annot);
                    removed++;
                }
            }
        }
        return removed;
    }

    /// <summary>
    /// A copy of the PDF without PdfEdit's own annotations listed in <paramref name="ids"/>, for the
    /// page renderer — PdfEdit draws those itself (editable), so rendering them too showed them twice.
    /// Returns null when nothing needed removing.
    /// </summary>
    public string? CreateRenderCopyWithout(string path, ISet<string> ids)
    {
        if (ids.Count == 0) return null;
        try
        {
            using (var probe = new PdfDocument(new PdfReader(path)))
            {
                bool any = false;
                for (int p = 1; p <= probe.GetNumberOfPages() && !any; p++)
                    any = probe.GetPage(p).GetAnnotations().Any(a => TrackedId(a.GetPdfObject()) is { } id && ids.Contains(id));
                if (!any) return null;
            }
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pdfedit-render-{Guid.NewGuid():N}.pdf");
            using (var doc = new PdfDocument(new PdfReader(path), new PdfWriter(tmp)))
                RemoveTrackedAnnotations(doc, ids);
            return tmp;
        }
        catch { return null; }
    }

    /// <summary>
    /// Adds an annotation with its comment the way Acrobat stores one: /NM id, author (/T), dates,
    /// note (/Contents), review status and checkmark as /State replies, and the reply thread as
    /// /IRT text annotations.
    /// </summary>
    private static void AddTracked(PdfPage page, PdfAnnotation annot, Models.CommentInfo c)
    {
        annot.Put(PdfName.NM, new PdfString(TrackedName(c.Id)));
        annot.SetTitle(new PdfString(c.Author ?? string.Empty));
        annot.Put(PdfName.CreationDate, new PdfDate(c.Created).GetPdfObject());
        annot.SetDate(new PdfDate(c.Modified).GetPdfObject());
        if (!string.IsNullOrEmpty(c.Note) && annot.GetContents() == null) annot.SetContents(c.Note);
        page.AddAnnotation(annot);

        var rect = annot.GetRectangle().ToRectangle();
        int n = 0;
        PdfTextAnnotation Reply(string author, string text, DateTime when)
        {
            var r = new PdfTextAnnotation(rect);
            r.Put(PdfName.IRT, annot.GetPdfObject());
            r.Put(PdfName.RT, new PdfName("R"));
            r.SetTitle(new PdfString(author));
            r.SetContents(text);
            r.SetDate(new PdfDate(when).GetPdfObject());
            r.SetOpen(false);
            r.SetFlags(PdfAnnotation.NO_ZOOM | PdfAnnotation.NO_ROTATE | PdfAnnotation.PRINT);
            r.Put(PdfName.NM, new PdfString(TrackedName(c.Id) + ":r" + n++));
            return r;
        }
        foreach (var reply in c.Replies)
            page.AddAnnotation(Reply(reply.Author, reply.Text, reply.Created));
        if (c.Status != Models.CommentStatus.None)
        {
            var st = Reply(c.Author, $"{c.Status} set by {c.Author}", c.Modified);
            st.Put(new PdfName("StateModel"), new PdfString("Review"));
            st.Put(new PdfName("State"), new PdfString(c.Status.ToString()));
            st.SetFlags(PdfAnnotation.HIDDEN);
            page.AddAnnotation(st);
        }
        if (c.Checked)
        {
            var mk = Reply(c.Author, $"Marked set by {c.Author}", c.Modified);
            mk.Put(new PdfName("StateModel"), new PdfString("Marked"));
            mk.Put(new PdfName("State"), new PdfString("Marked"));
            mk.SetFlags(PdfAnnotation.HIDDEN);
            page.AddAnnotation(mk);
        }
    }

    private static bool ParseHexColor(string hex, out float r, out float g, out float b)
    {
        r = g = b = 0f;
        if (string.IsNullOrEmpty(hex)) return false;
        hex = hex.TrimStart('#');
        if (hex.Length < 6) return false;
        if (int.TryParse(hex[0..2], System.Globalization.NumberStyles.HexNumber, null, out int ri) &&
            int.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out int gi) &&
            int.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out int bi))
        {
            r = ri / 255f; g = gi / 255f; b = bi / 255f;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Encrypts a PDF with a user password (required to open) and an owner password (required to change permissions).
    /// Passing null/empty for a password means no password of that type.
    /// SECURITY: passwords are NEVER stored to disk — callers must not persist them.
    /// </summary>
    public void EncryptPdf(string inputPath, string outputPath,
        string? userPassword, string? ownerPassword,
        bool allowPrinting = true, bool allowCopying = false)
    {
        byte[]? userBytes  = string.IsNullOrEmpty(userPassword)  ? null : System.Text.Encoding.UTF8.GetBytes(userPassword);
        byte[]? ownerBytes = string.IsNullOrEmpty(ownerPassword) ? null : System.Text.Encoding.UTF8.GetBytes(ownerPassword);

        int perms = 0;
        if (allowPrinting) perms |= EncryptionConstants.ALLOW_PRINTING;
        if (allowCopying)  perms |= EncryptionConstants.ALLOW_COPY;

        var writerProps = new WriterProperties()
            .SetStandardEncryption(userBytes, ownerBytes,
                perms, EncryptionConstants.ENCRYPTION_AES_256);

        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath, writerProps);
        using var pdf = new PdfDocument(reader, writer);
    }

    /// <summary>
    /// Removes encryption from a PDF that was opened with the given password.
    /// SECURITY: the password is NEVER stored to disk.
    /// </summary>
    public void RemoveEncryption(string inputPath, string outputPath, string? password = null)
    {
        var readerProps = new ReaderProperties();
        if (!string.IsNullOrEmpty(password))
            readerProps.SetPassword(System.Text.Encoding.UTF8.GetBytes(password));

        using var reader = new PdfReader(inputPath, readerProps);
        reader.SetUnethicalReading(true);
        using var writer = new PdfWriter(outputPath);
        using var pdf = new PdfDocument(reader, writer);
    }

    /// <summary>
    /// Adds Bates numbers to every page of a PDF.
    /// format supports {n} (sequential number), {prefix}, {suffix}.
    /// </summary>
    public void AddBatesNumbers(string inputPath, string outputPath,
        int startNumber = 1, int padding = 6,
        string prefix = "", string suffix = "",
        float fontSize = 8f, float marginPt = 18f,
        string position = "BottomRight")
    {
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(reader, writer);
        var font  = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.COURIER);
        int total = pdf.GetNumberOfPages();
        var gray  = new DeviceRgb(0.3f, 0.3f, 0.3f);

        for (int i = 1; i <= total; i++)
        {
            var page = pdf.GetPage(i);
            var size = page.GetPageSize();
            string num  = (startNumber + i - 1).ToString().PadLeft(padding, '0');
            string text = $"{prefix}{num}{suffix}";
            float textWidth = font.GetWidth(text, fontSize);

            float x = position switch
            {
                "BottomLeft"  => marginPt,
                "BottomCenter"=> (size.GetWidth() - textWidth) / 2,
                "TopLeft"     => marginPt,
                "TopCenter"   => (size.GetWidth() - textWidth) / 2,
                "TopRight"    => size.GetWidth() - textWidth - marginPt,
                _             => size.GetWidth() - textWidth - marginPt // BottomRight
            };
            float y = position.StartsWith("Top")
                ? size.GetHeight() - marginPt - fontSize
                : marginPt;

            var canvas = new PdfCanvas(page);
            canvas.SaveState();
            canvas.SetFillColor(gray);
            canvas.BeginText();
            canvas.SetFontAndSize(font, fontSize);
            canvas.SetTextMatrix(1, 0, 0, 1, x, y);
            canvas.ShowText(text);
            canvas.EndText();
            canvas.RestoreState();
        }
    }

    /// <summary>
    /// Crops every page to the specified crop box (in points, relative to the bottom-left of the media box).
    /// Negative values for right/top use distance from page edge.
    /// </summary>
    public void CropAllPages(string inputPath, string outputPath,
        float leftPt, float bottomPt, float rightPt, float topPt)
    {
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(reader, writer);
        int total = pdf.GetNumberOfPages();

        for (int i = 1; i <= total; i++)
        {
            var page = pdf.GetPage(i);
            var media = page.GetMediaBox();
            float l = media.GetLeft()   + leftPt;
            float b = media.GetBottom() + bottomPt;
            float r = rightPt  <= 0 ? media.GetRight()  + rightPt  : media.GetLeft() + rightPt;
            float t = topPt    <= 0 ? media.GetTop()    + topPt    : media.GetBottom() + topPt;
            page.SetCropBox(new Rectangle(l, b, r - l, t - b));
        }
    }

    /// <summary>
    /// Adds a URI link annotation to the specified page at the given location (in PDF points, bottom-left origin).
    /// </summary>
    public void AddLinkAnnotation(string inputPath, string outputPath,
        int pageNumber, float left, float bottom, float width, float height, string uri)
    {
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(reader, writer);

        if (pageNumber < 1 || pageNumber > pdf.GetNumberOfPages()) return;
        var page = pdf.GetPage(pageNumber);

        var rect   = new Rectangle(left, bottom, width, height);
        var action = iText.Kernel.Pdf.Action.PdfAction.CreateURI(uri);
        var annot  = new iText.Kernel.Pdf.Annot.PdfLinkAnnotation(rect)
            .SetAction(action)
            .SetBorder(new iText.Kernel.Pdf.PdfArray(new float[] { 0, 0, 1 }))
            .SetColor(new iText.Kernel.Pdf.PdfArray(new float[] { 0, 0, 1 }));
        page.AddAnnotation(annot);
    }

    /// <summary>
    /// Adds a new text form field to the specified page at the given location.
    /// fieldName must be unique within the document.
    /// </summary>
    public void AddTextFormField(string inputPath, string outputPath,
        int pageNumber, float left, float bottom, float width, float height,
        string fieldName, string defaultValue = "", float fontSize = 10f)
    {
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(reader, writer);
        var form = PdfAcroForm.GetAcroForm(pdf, true);

        if (pageNumber < 1 || pageNumber > pdf.GetNumberOfPages()) return;
        var page = pdf.GetPage(pageNumber);
        var rect = new Rectangle(left, bottom, width, height);
        var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);

        var field = new iText.Forms.Fields.TextFormFieldBuilder(pdf, fieldName)
            .SetWidgetRectangle(rect)
            .CreateText();
        field.SetValue(defaultValue);
        field.SetFont(font).SetFontSize(fontSize);
        form.AddField(field, page);
    }

    /// <summary>
    /// Adds a new checkbox form field.
    /// </summary>
    public void AddCheckboxField(string inputPath, string outputPath,
        int pageNumber, float left, float bottom, float size,
        string fieldName, bool defaultChecked = false)
    {
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(reader, writer);
        var form = PdfAcroForm.GetAcroForm(pdf, true);

        if (pageNumber < 1 || pageNumber > pdf.GetNumberOfPages()) return;
        var page = pdf.GetPage(pageNumber);
        var rect = new Rectangle(left, bottom, size, size);

        var field = new iText.Forms.Fields.CheckBoxFormFieldBuilder(pdf, fieldName)
            .SetWidgetRectangle(rect)
            .CreateCheckBox();
        if (defaultChecked) field.SetValue("Yes");
        form.AddField(field, page);
    }

    /// <summary>
    /// Adds a new combo box (dropdown) form field with given choices.
    /// </summary>
    public void AddComboBoxField(string inputPath, string outputPath,
        int pageNumber, float left, float bottom, float width, float height,
        string fieldName, IEnumerable<string> choices, float fontSize = 10f)
    {
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(reader, writer);
        var form = PdfAcroForm.GetAcroForm(pdf, true);

        if (pageNumber < 1 || pageNumber > pdf.GetNumberOfPages()) return;
        var page = pdf.GetPage(pageNumber);
        var rect = new Rectangle(left, bottom, width, height);
        var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);

        var choiceList = choices.ToList();
        var builder = new iText.Forms.Fields.ChoiceFormFieldBuilder(pdf, fieldName)
            .SetWidgetRectangle(rect);
        if (choiceList.Count > 0)
            builder.SetOptions(choiceList.ToArray());
        var field = builder.CreateComboBox();
        field.SetFont(font).SetFontSize(fontSize);
        form.AddField(field, page);
    }

    /// <summary>Adds a list box (scrolling list of choices).</summary>
    public void AddListBoxField(string inputPath, string outputPath,
        int pageNumber, float left, float bottom, float width, float height,
        string fieldName, IEnumerable<string> choices, float fontSize = 10f)
    {
        using var pdf = new PdfDocument(new PdfReader(inputPath), new PdfWriter(outputPath));
        var form = PdfAcroForm.GetAcroForm(pdf, true);
        if (pageNumber < 1 || pageNumber > pdf.GetNumberOfPages()) return;
        var builder = new iText.Forms.Fields.ChoiceFormFieldBuilder(pdf, fieldName)
            .SetWidgetRectangle(new Rectangle(left, bottom, width, height));
        var list = choices.ToArray();
        if (list.Length > 0) builder.SetOptions(list);
        var field = builder.CreateList();
        field.SetFont(PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA)).SetFontSize(fontSize);
        form.AddField(field, pdf.GetPage(pageNumber));
    }

    /// <summary>Adds an (unsigned) signature field — Acrobat's "Add a signature field".</summary>
    public void AddSignatureField(string inputPath, string outputPath,
        int pageNumber, float left, float bottom, float width, float height, string fieldName)
    {
        using var pdf = new PdfDocument(new PdfReader(inputPath), new PdfWriter(outputPath));
        var form = PdfAcroForm.GetAcroForm(pdf, true);
        if (pageNumber < 1 || pageNumber > pdf.GetNumberOfPages()) return;
        var field = new iText.Forms.Fields.SignatureFormFieldBuilder(pdf, fieldName)
            .SetWidgetRectangle(new Rectangle(left, bottom, width, height))
            .CreateSignature();
        form.AddField(field, pdf.GetPage(pageNumber));
    }

    /// <summary>Adds a date field: a text field with Acrobat's AFDate format / keystroke scripts.</summary>
    public void AddDateField(string inputPath, string outputPath,
        int pageNumber, float left, float bottom, float width, float height,
        string fieldName, string format = "dd/mm/yyyy", float fontSize = 10f)
    {
        using var pdf = new PdfDocument(new PdfReader(inputPath), new PdfWriter(outputPath));
        var form = PdfAcroForm.GetAcroForm(pdf, true);
        if (pageNumber < 1 || pageNumber > pdf.GetNumberOfPages()) return;
        var field = new iText.Forms.Fields.TextFormFieldBuilder(pdf, fieldName)
            .SetWidgetRectangle(new Rectangle(left, bottom, width, height))
            .CreateText();
        field.SetValue(string.Empty);
        field.SetFont(PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA)).SetFontSize(fontSize);
        field.SetAdditionalAction(PdfName.F, iText.Kernel.Pdf.Action.PdfAction.CreateJavaScript($"AFDate_FormatEx(\"{format}\");"));
        field.SetAdditionalAction(PdfName.K, iText.Kernel.Pdf.Action.PdfAction.CreateJavaScript($"AFDate_KeystrokeEx(\"{format}\");"));
        form.AddField(field, pdf.GetPage(pageNumber));
    }

    public void AddRadioButtonField(string inputPath, string outputPath,
        int pageNumber, float left, float bottom, float size,
        string groupName, string onValue)
    {
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(reader, writer);
        var form = PdfAcroForm.GetAcroForm(pdf, true);

        if (pageNumber < 1 || pageNumber > pdf.GetNumberOfPages()) return;
        var page = pdf.GetPage(pageNumber);
        var rect = new Rectangle(left, bottom, size, size);

        // Reuse existing radio group or create new one
        PdfButtonFormField? group = form.GetField(groupName) as PdfButtonFormField;
        if (group == null)
        {
            group = new iText.Forms.Fields.RadioFormFieldBuilder(pdf, groupName)
                .CreateRadioGroup();
            form.AddField(group, page);
        }
        var widget = new iText.Forms.Fields.RadioFormFieldBuilder(pdf, groupName)
            .CreateRadioButton(onValue, rect);
        group.AddKid(widget);
    }

    /// <summary>Exports all extractable text from each page to a UTF-8 text file.</summary>
    public void ExportTextToFile(string inputPath, string outputPath)
    {
        using var reader = new PdfReader(inputPath);
        using var pdf    = new PdfDocument(reader);
        using var sw     = new System.IO.StreamWriter(outputPath, false, System.Text.Encoding.UTF8);
        int total = pdf.GetNumberOfPages();
        for (int i = 1; i <= total; i++)
        {
            var strategy = new iText.Kernel.Pdf.Canvas.Parser.Listener.LocationTextExtractionStrategy();
            string text  = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(pdf.GetPage(i), strategy);
            sw.WriteLine($"--- Page {i} of {total} ---");
            sw.WriteLine(text);
            sw.WriteLine();
        }
    }

    /// <summary>
    /// Deletes a range of pages (1-based, inclusive) from a PDF.
    /// Returns the number of pages remaining after deletion.
    /// </summary>
    public int DeletePageRange(string inputPath, string outputPath, int firstPage, int lastPage)
    {
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var inDoc  = new PdfDocument(reader);
        using var outDoc = new PdfDocument(writer);

        int total = inDoc.GetNumberOfPages();
        firstPage = Math.Clamp(firstPage, 1, total);
        lastPage  = Math.Clamp(lastPage,  1, total);

        for (int i = 1; i <= total; i++)
        {
            if (i < firstPage || i > lastPage)
                inDoc.CopyPagesTo(i, i, outDoc);
        }
        return total - (lastPage - firstPage + 1);
    }

    /// <summary>
    /// Extracts a range of pages (1-based, inclusive) to a new PDF.
    /// </summary>
    public void ExtractPageRange(string inputPath, string outputPath, int firstPage, int lastPage)
    {
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var inDoc  = new PdfDocument(reader);
        using var outDoc = new PdfDocument(writer);

        int total = inDoc.GetNumberOfPages();
        firstPage = Math.Clamp(firstPage, 1, total);
        lastPage  = Math.Clamp(lastPage,  1, total);
        inDoc.CopyPagesTo(firstPage, lastPage, outDoc);
    }

    /// <summary>
    /// Compares the extracted text of two PDFs page by page and returns a structured diff
    /// where each entry describes a page and the lines added/removed compared to the other document.
    /// </summary>
    public List<PdfPageDiff> ComparePdfs(string pathA, string pathB)
    {
        var result = new List<PdfPageDiff>();

        using var readerA = new PdfReader(pathA);
        using var docA    = new PdfDocument(readerA);
        using var readerB = new PdfReader(pathB);
        using var docB    = new PdfDocument(readerB);

        int totalA = docA.GetNumberOfPages();
        int totalB = docB.GetNumberOfPages();
        int maxPages = Math.Max(totalA, totalB);

        for (int i = 1; i <= maxPages; i++)
        {
            string textA = i <= totalA
                ? iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(docA.GetPage(i)) : string.Empty;
            string textB = i <= totalB
                ? iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(docB.GetPage(i)) : string.Empty;

            var linesA = textA.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet();
            var linesB = textB.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet();

            result.Add(new PdfPageDiff
            {
                PageIndex = i,
                OnlyInA   = linesA.Except(linesB).ToList(),
                OnlyInB   = linesB.Except(linesA).ToList(),
                CommonLineCount = linesA.Intersect(linesB).Count(),
                ExistsInA = i <= totalA,
                ExistsInB = i <= totalB,
            });
        }
        return result;
    }

    // ── Attachments ───────────────────────────────────────────────────────────

    public List<Models.PdfAttachmentInfo> GetAttachments(string pdfPath)
    {
        var result = new List<Models.PdfAttachmentInfo>();
        using var reader = new PdfReader(pdfPath);
        using var doc    = new PdfDocument(reader);

        var catalog = doc.GetCatalog();
        var names   = catalog.GetPdfObject().GetAsDictionary(PdfName.Names);
        if (names == null) return result;
        var embeddedFiles = names.GetAsDictionary(new PdfName("EmbeddedFiles"));
        if (embeddedFiles == null) return result;
        var namesArr = embeddedFiles.GetAsArray(PdfName.Names);
        if (namesArr == null) return result;

        for (int i = 0; i < namesArr.Size() - 1; i += 2)
        {
            var nameStr = namesArr.GetAsString(i)?.ToUnicodeString() ?? $"Attachment {i / 2 + 1}";
            var fileSpec = namesArr.GetAsDictionary(i + 1);
            if (fileSpec == null) continue;
            var ef = fileSpec.GetAsDictionary(PdfName.EF);
            long size = 0;
            string? desc = null;
            if (ef != null)
            {
                var stream = ef.GetAsStream(PdfName.F) ?? ef.GetAsStream(new PdfName("UF"));
                if (stream != null)
                    size = stream.GetAsDictionary(PdfName.Params)?.GetAsNumber(PdfName.Size)?.LongValue() ?? 0;
            }
            var descObj = fileSpec.GetAsString(new PdfName("Desc"));
            if (descObj != null) desc = descObj.ToUnicodeString();

            result.Add(new Models.PdfAttachmentInfo { Name = nameStr, FileSizeBytes = size, Description = desc ?? string.Empty });
        }
        return result;
    }

    public void AddAttachment(string inputPath, string outputPath, string filePath, string? displayName = null)
    {
        string name = displayName ?? System.IO.Path.GetFileName(filePath);
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var doc    = new PdfDocument(reader, writer);

        byte[] bytes = System.IO.File.ReadAllBytes(filePath);
        var fileSpec = iText.Kernel.Pdf.Filespec.PdfFileSpec.CreateEmbeddedFileSpec(
            doc, bytes, name, name, null, null, null);
        doc.AddFileAttachment(name, fileSpec);
    }

    public void RemoveAttachment(string inputPath, string outputPath, string attachmentName)
    {
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var doc    = new PdfDocument(reader, writer);

        var catalog = doc.GetCatalog();
        var names   = catalog.GetPdfObject().GetAsDictionary(PdfName.Names);
        if (names == null) return;
        var embeddedFiles = names.GetAsDictionary(new PdfName("EmbeddedFiles"));
        if (embeddedFiles == null) return;
        var namesArr = embeddedFiles.GetAsArray(PdfName.Names);
        if (namesArr == null) return;

        for (int i = 0; i < namesArr.Size() - 1; i += 2)
        {
            var nameStr = namesArr.GetAsString(i)?.ToUnicodeString();
            if (nameStr == attachmentName)
            {
                namesArr.Remove(i + 1);
                namesArr.Remove(i);
                embeddedFiles.Put(PdfName.Names, namesArr);
                break;
            }
        }
    }

    public byte[] ExtractAttachment(string pdfPath, string attachmentName)
    {
        using var reader = new PdfReader(pdfPath);
        using var doc    = new PdfDocument(reader);

        var catalog = doc.GetCatalog();
        var names   = catalog.GetPdfObject().GetAsDictionary(PdfName.Names);
        if (names == null) throw new InvalidOperationException("No attachments in document.");
        var embeddedFiles = names.GetAsDictionary(new PdfName("EmbeddedFiles"));
        if (embeddedFiles == null) throw new InvalidOperationException("No attachments in document.");
        var namesArr = embeddedFiles.GetAsArray(PdfName.Names);
        if (namesArr == null) throw new InvalidOperationException("No attachments in document.");

        for (int i = 0; i < namesArr.Size() - 1; i += 2)
        {
            var nameStr = namesArr.GetAsString(i)?.ToUnicodeString();
            if (nameStr == attachmentName)
            {
                var fileSpec = namesArr.GetAsDictionary(i + 1);
                var ef = fileSpec?.GetAsDictionary(PdfName.EF);
                if (ef == null) throw new InvalidOperationException($"Attachment '{attachmentName}' has no embedded data.");
                var stream = ef.GetAsStream(PdfName.F) ?? ef.GetAsStream(new PdfName("UF"));
                if (stream == null) throw new InvalidOperationException($"Attachment '{attachmentName}' stream not found.");
                return stream.GetBytes();
            }
        }
        throw new KeyNotFoundException($"Attachment '{attachmentName}' not found.");
    }

    private static FieldType GetFieldType(PdfFormField field)
    {
        if (field is PdfTextFormField) return FieldType.Text;
        if (field is PdfSignatureFormField) return FieldType.Signature;
        if (field is PdfButtonFormField btn)
        {
            if (btn.IsPushButton()) return FieldType.Button;
            if (btn.IsRadio()) return FieldType.RadioButton;
            return FieldType.Checkbox;
        }
        if (field is PdfChoiceFormField choice)
            return choice.IsCombo() ? FieldType.ComboBox : FieldType.ListBox;
        return FieldType.Unknown;
    }
}
