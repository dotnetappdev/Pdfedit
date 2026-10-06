using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;

namespace PdfEdit.Services;

/// <summary>A field to add (PDF points, bottom-left origin).</summary>
public sealed class NewField
{
    public int Page { get; set; }
    public string Name { get; set; } = "";
    public bool IsCheckBox { get; set; }
    public bool Multiline { get; set; }
    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Include { get; set; } = true;

    // Richer fields (from imported Word forms)
    /// <summary>Choices for a dropdown (combo box).</summary>
    public List<string>? Choices { get; set; }
    /// <summary>Option button: the group it belongs to, and this button's value.</summary>
    public string? RadioGroup { get; set; }
    public string? RadioValue { get; set; }
    /// <summary>Date field with this Acrobat date format (e.g. "dd/mm/yyyy").</summary>
    public string? DateFormat { get; set; }
    /// <summary>Starting value ("Yes" ticks a check box).</summary>
    public string? Value { get; set; }
    /// <summary>Description shown as the tooltip and read by screen readers.</summary>
    public string? Tooltip { get; set; }
    public bool Required { get; set; }
    public float FontSize { get; set; }

    public string Kind => RadioGroup != null ? "Option button" : Choices != null ? "Dropdown" : DateFormat != null ? "Date"
        : IsCheckBox ? "Check box" : Multiline ? "Text (multi-line)" : "Text";
}

/// <summary>Adds many fields to a PDF in one go (the result of field detection).</summary>
public static class DetectFieldsService
{
    public static int AddFields(string inputPath, string outputPath, IEnumerable<NewField> fields)
    {
        using var pdf = new PdfDocument(new PdfReader(inputPath), new PdfWriter(outputPath));
        var form = PdfAcroForm.GetAcroForm(pdf, true);
        var existing = new HashSet<string>(form.GetAllFormFields().Keys, StringComparer.OrdinalIgnoreCase);
        var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
        int added = 0;
        var groups = new Dictionary<string, PdfButtonFormField>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fields)
        {
            if (f.Page < 1 || f.Page > pdf.GetNumberOfPages()) continue;
            var rect = new Rectangle((float)f.Left, (float)f.Bottom, (float)f.Width, (float)f.Height);
            var page = pdf.GetPage(f.Page);

            // Option buttons: one group per question, one button per choice.
            if (f.RadioGroup != null)
            {
                string value = string.IsNullOrWhiteSpace(f.RadioValue) ? $"Choice{added + 1}" : f.RadioValue!;
                if (!groups.TryGetValue(f.RadioGroup, out var group))
                {
                    string groupName = UniqueName(f.RadioGroup, existing);
                    group = new RadioFormFieldBuilder(pdf, groupName).CreateRadioGroup();
                    if (!string.IsNullOrWhiteSpace(f.Tooltip)) group.SetAlternativeName(f.Tooltip);
                    if (f.Required) group.SetRequired(true);
                    form.AddField(group, page);
                    groups[f.RadioGroup] = group;
                }
                var button = new RadioFormFieldBuilder(pdf, group.GetFieldName().ToUnicodeString()).CreateRadioButton(value, rect);
                group.AddKid(button);
                added++;
                continue;
            }

            string name = UniqueName(string.IsNullOrWhiteSpace(f.Name) ? (f.IsCheckBox ? "Check Box" : "Text") : f.Name.Trim(), existing);
            PdfFormField field;
            if (f.IsCheckBox)
            {
                field = new CheckBoxFormFieldBuilder(pdf, name).SetWidgetRectangle(rect).CreateCheckBox();
                if (f.Value is "Yes" or "On" or "1" or "true") field.SetValue("Yes");
            }
            else if (f.Choices != null)
            {
                var builder = new ChoiceFormFieldBuilder(pdf, name).SetWidgetRectangle(rect);
                if (f.Choices.Count > 0) builder.SetOptions(f.Choices.ToArray());
                var combo = builder.CreateComboBox();
                combo.SetFont(font).SetFontSize(f.FontSize > 0 ? f.FontSize : 10f);
                if (!string.IsNullOrEmpty(f.Value) && f.Choices.Contains(f.Value)) combo.SetValue(f.Value);
                field = combo;
            }
            else
            {
                var t = new TextFormFieldBuilder(pdf, name).SetWidgetRectangle(rect).CreateText();
                t.SetMultiline(f.Multiline);
                t.SetFont(font).SetFontSize(f.FontSize > 0 ? f.FontSize : f.Multiline ? 10f : 0f); // 0 = auto size to the box
                if (!string.IsNullOrEmpty(f.Value)) t.SetValue(f.Value);
                if (f.DateFormat != null)
                {
                    t.SetAdditionalAction(PdfName.F, iText.Kernel.Pdf.Action.PdfAction.CreateJavaScript($"AFDate_FormatEx(\"{f.DateFormat}\");"));
                    t.SetAdditionalAction(PdfName.K, iText.Kernel.Pdf.Action.PdfAction.CreateJavaScript($"AFDate_KeystrokeEx(\"{f.DateFormat}\");"));
                }
                field = t;
            }
            if (!string.IsNullOrWhiteSpace(f.Tooltip)) field.SetAlternativeName(f.Tooltip);
            if (f.Required) field.SetRequired(true);
            form.AddField(field, page);
            added++;
        }
        return added;
    }

    private static string UniqueName(string name, HashSet<string> taken)
    {
        name = name.Replace(".", " ");
        string n = name;
        for (int i = 2; !taken.Add(n); i++) n = $"{name} {i}";
        return n;
    }
}
