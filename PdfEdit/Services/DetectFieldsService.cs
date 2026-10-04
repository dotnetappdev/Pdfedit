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
    public string Kind => IsCheckBox ? "Check box" : Multiline ? "Text (multi-line)" : "Text";
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
        foreach (var f in fields)
        {
            if (f.Page < 1 || f.Page > pdf.GetNumberOfPages()) continue;
            string name = UniqueName(string.IsNullOrWhiteSpace(f.Name) ? (f.IsCheckBox ? "Check Box" : "Text") : f.Name.Trim(), existing);
            var rect = new Rectangle((float)f.Left, (float)f.Bottom, (float)f.Width, (float)f.Height);
            PdfFormField field;
            if (f.IsCheckBox)
                field = new CheckBoxFormFieldBuilder(pdf, name).SetWidgetRectangle(rect).CreateCheckBox();
            else
            {
                var t = new TextFormFieldBuilder(pdf, name).SetWidgetRectangle(rect).CreateText();
                t.SetMultiline(f.Multiline);
                t.SetFont(font).SetFontSize(f.Multiline ? 10f : 0f); // 0 = auto size to the box
                field = t;
            }
            form.AddField(field, pdf.GetPage(f.Page));
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
