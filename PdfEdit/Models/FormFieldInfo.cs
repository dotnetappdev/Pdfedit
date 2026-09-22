namespace PdfEdit.Models;

public class FormFieldInfo
{
    public string Name { get; set; } = string.Empty;
    public FieldType FieldType { get; set; }
    public int PageNumber { get; set; }

    // All in PDF points (72 pts = 1 inch), Y from bottom-left
    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public string Value { get; set; } = string.Empty;
    public string? DefaultValue { get; set; }
    public List<string> Options { get; set; } = new();

    // For radio buttons and checkboxes: the PDF export value this widget represents.
    // For radio buttons Value holds the group's CURRENT selection; ExportValue identifies THIS button.
    public string ExportValue { get; set; } = "Yes";

    public bool IsReadOnly { get; set; }
    public bool IsRequired { get; set; }
    public bool IsMultiline { get; set; }
    public bool IsPassword { get; set; }
    public string? Tooltip { get; set; }
    public string? RadioGroup { get; set; }
}
