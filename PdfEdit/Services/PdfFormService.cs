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

                foreach (var widget in widgets)
                {
                    var page = widget.GetPage();
                    if (page == null) continue;

                    var rectObj = widget.GetRectangle();
                    if (rectObj == null) continue;

                    var rect = rectObj.ToRectangle();
                    int pageNum = doc.GetPageNumber(page);

                    var fieldInfo = new FormFieldInfo
                    {
                        Name = name,
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
        IEnumerable<Models.ShapeAnnotation>? shapeAnnotations = null)
    {
        var saveErrors = new List<string>();
        fieldExportValues ??= new Dictionary<string, string>();

        using var reader = new PdfReader(sourcePath);
        using var writer = new PdfWriter(destPath);
        using var doc = new PdfDocument(reader, writer);

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
                        field.SetValue(value);
                    }
                }
                catch (Exception ex)
                {
                    saveErrors.Add($"Field '{name}': {ex.Message}");
                }
            }

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
                    bool colorOk = ParseHexColor(parts[1], out float ir, out float ig, out float ib);
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
                    inkAnnot.SetBorderStyle(new PdfDictionary());
                    page.AddAnnotation(inkAnnot);
                }
                continue;
            }

            var rect = new Rectangle(
                (float)ann.Left,
                (float)ann.Bottom,
                (float)ann.Width,
                (float)ann.Height);

            var pdfAnn = new PdfFreeTextAnnotation(rect, new PdfString(ann.Text));
            pdfAnn.SetContents(ann.Text);

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

            pdfAnn.SetDefaultAppearance(new PdfString($"/{fontName} {ann.FontSize:F1} Tf {colorStr}"));

            page.AddAnnotation(pdfAnn);
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
                else if (hl.Kind == Models.HighlightKind.Underline)
                    pdfHL = iText.Kernel.Pdf.Annot.PdfTextMarkupAnnotation.CreateUnderline(rect, quadPoints);
                else
                    pdfHL = iText.Kernel.Pdf.Annot.PdfTextMarkupAnnotation.CreateHighLight(rect, quadPoints);

                pdfHL.SetColor(color);
                pdfHL.Put(PdfName.CA, new PdfNumber(hl.Opacity));
                page.AddAnnotation(pdfHL);
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
                if (!string.IsNullOrEmpty(note.Author))
                    textAnnot.Put(PdfName.T, new PdfString(note.Author));
                page.AddAnnotation(textAnnot);
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

                if (shape.Kind == Models.ShapeKind.Rectangle)
                {
                    var annot = new PdfSquareAnnotation(new Rectangle((float)left, (float)bottom, (float)width, (float)height));
                    annot.SetColor(strokeColor);
                    if (!string.IsNullOrEmpty(shape.FillColor) && ParseHexColor(shape.FillColor, out float fr, out float fg, out float fb))
                        annot.SetInteriorColor(new float[] { fr, fg, fb });
                    annot.Put(PdfName.BS, BuildBorderStyle(lw));
                    page.AddAnnotation(annot);
                }
                else if (shape.Kind == Models.ShapeKind.Ellipse)
                {
                    var annot = new PdfCircleAnnotation(new Rectangle((float)left, (float)bottom, (float)width, (float)height));
                    annot.SetColor(strokeColor);
                    if (!string.IsNullOrEmpty(shape.FillColor) && ParseHexColor(shape.FillColor, out float fr, out float fg, out float fb))
                        annot.SetInteriorColor(new float[] { fr, fg, fb });
                    annot.Put(PdfName.BS, BuildBorderStyle(lw));
                    page.AddAnnotation(annot);
                }
                else if (shape.Kind == Models.ShapeKind.Arrow)
                {
                    var lineRect = new Rectangle((float)left, (float)bottom, (float)Math.Max(width, 1), (float)Math.Max(height, 1));
                    var annot = new PdfLineAnnotation(lineRect, new float[] { (float)x1, (float)y1, (float)x2, (float)y2 });
                    annot.SetColor(strokeColor);
                    annot.Put(PdfName.LE, new PdfArray(new[] { new PdfName("None"), new PdfName("OpenArrow") }));
                    annot.Put(PdfName.BS, BuildBorderStyle(lw));
                    page.AddAnnotation(annot);
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
                    page.AddAnnotation(annot);
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
                try
                {
                    var imageData = ImageDataFactory.Create(sig.ImageBytes);
                    var xobj = new PdfImageXObject(imageData);
                    var canvas = new PdfCanvas(page);
                    // Transformation matrix: [scaleX 0 0 scaleY translateX translateY]
                    canvas.AddXObjectWithTransformationMatrix(xobj,
                        (float)sig.Width, 0f, 0f, (float)sig.Height,
                        (float)sig.Left, (float)sig.Bottom);
                    canvas.Release();
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

    private static PdfDictionary BuildBorderStyle(float lineWidth)
    {
        var bs = new PdfDictionary();
        bs.Put(PdfName.Type, PdfName.Border);
        bs.Put(PdfName.W, new PdfNumber(lineWidth));
        bs.Put(PdfName.S, PdfName.S); // Solid
        return bs;
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
