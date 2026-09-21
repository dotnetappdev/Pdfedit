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
            var size = doc.GetPage(i).GetPageSize();
            info.PageSizes.Add((size.GetWidth(), size.GetHeight()));
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
                        var appearance = widget.GetAppearanceDictionary();
                        if (appearance != null)
                        {
                            var normalAp = appearance.GetAsDictionary(PdfName.N);
                            if (normalAp != null)
                            {
                                foreach (var key in normalAp.KeySet())
                                {
                                    if (!key.Equals(new PdfName("Off")))
                                        fieldInfo.Value = key.GetValue();
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

    public void SaveWithFormData(string sourcePath, string destPath,
        Dictionary<string, string> fieldValues, bool flatten = false)
    {
        SaveFull(sourcePath, destPath, fieldValues,
            new Dictionary<int, int>(),
            new ObservableCollection<FreeTextAnnotation>(),
            new ObservableCollection<PlacedSignature>(),
            flatten);
    }

    /// <summary>
    /// Saves the PDF with filled form fields, page rotations, free-text annotations, and placed signatures.
    /// </summary>
    public void SaveFull(string sourcePath, string destPath,
        Dictionary<string, string> fieldValues,
        Dictionary<int, int> pageRotations,
        IEnumerable<FreeTextAnnotation> freeTextAnnotations,
        IEnumerable<PlacedSignature>? placedSignatures = null,
        bool flatten = false,
        IEnumerable<string>? deletedFieldNames = null)
    {
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

                if (field is PdfButtonFormField btn && !btn.IsPushButton() && !btn.IsRadio())
                    field.SetValue(value is "Yes" or "true" or "On" or "1" ? "Yes" : "Off");
                else
                    field.SetValue(value);
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

        // ── 4. Placed signatures ──────────────────────────────────────────
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
                catch
                {
                    // Skip invalid/corrupt image bytes
                }
            }
        }
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
