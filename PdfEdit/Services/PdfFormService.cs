using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;
using PdfEdit.Models;

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
                        DefaultValue = field.GetDefaultValueAsString(),
                        Tooltip = field.GetDisplayName(),
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
                            // The on-value is the key of the normal appearance dictionary
                            var normalAp = appearance.GetAsDictionary(PdfName.N);
                            if (normalAp != null)
                            {
                                foreach (var key in normalAp.KeySet())
                                {
                                    if (!key.Equals(PdfName.Off))
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

    public void SaveWithFormData(string sourcePath, string destPath, Dictionary<string, string> fieldValues, bool flatten = false)
    {
        using var reader = new PdfReader(sourcePath);
        using var writer = new PdfWriter(destPath);
        using var doc = new PdfDocument(reader, writer);

        var form = PdfAcroForm.GetAcroForm(doc, false);
        if (form == null) return;

        foreach (var (name, value) in fieldValues)
        {
            var field = form.GetField(name);
            if (field == null) continue;

            if (field is PdfButtonFormField btn)
            {
                if (btn.IsCheckBox())
                    field.SetValue(value == "Yes" || value == "true" || value == "On" ? "Yes" : "Off");
                else
                    field.SetValue(value);
            }
            else
            {
                field.SetValue(value);
            }
        }

        if (flatten)
            form.FlattenFields();
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
        {
            return choice.IsCombo() ? FieldType.ComboBox : FieldType.ListBox;
        }
        return FieldType.Unknown;
    }
}
