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

    // Index of this widget within its field's /Kids (a radio group or a field repeated on several
    // pages has several widgets). Used to write moved/resized geometry back to the right widget.
    public int WidgetIndex { get; set; }

    // Text / choice field appearance (PDF /Q quadding and the /DA font size; 0 = auto-size).
    public FieldAlignment Alignment { get; set; } = FieldAlignment.Left;
    public double FontSize { get; set; }
    // The PDF font from /DA (e.g. "Helvetica", "HelveticaLTStd-Bold"), shown with PdfFontMap.
    public string? FontName { get; set; }

    // A rename made in the Properties panel. Name stays the PDF's current name (it is the key used
    // for values, bounds and deletions) until the document is saved; the rename is applied last.
    public string? PendingName { get; set; }
    public string DisplayName => string.IsNullOrEmpty(PendingName) ? Name : PendingName;

    // ── "Field Properties" (Appearance / Options tabs) ──────────────────
    public string? BorderColor { get; set; }        // "#RRGGBB"; null = no border
    public string? FillColor { get; set; }          // "#RRGGBB"; null = transparent
    public string TextColor { get; set; } = "#000000";
    public int MaxLength { get; set; }              // text fields: 0 = unlimited
    public bool IsComb { get; set; }                // text fields: spread MaxLength chars evenly
    public bool IsEditable { get; set; }            // combo boxes: allow typing a custom value
    public string? DateFormat { get; set; }         // e.g. "dd/mm/yyyy" → a date field (AFDate_FormatEx)
    public bool IsDateField => !string.IsNullOrEmpty(DateFormat);

    // Format tab: Number, Currency, Percent, Zip, Zip+4, Phone, SSN (null = none). The
    // value is stored as a plain number; the formatted text is only what's shown.
    public string? NumberFormat { get; set; }
    public int Decimals { get; set; } = 2;
    public string CurrencySymbol { get; set; } = "£";
    public bool HasNumberFormat => !string.IsNullOrEmpty(NumberFormat);

    // Calculate tab: SUM / PRD / AVG / MIN / MAX of other fields (null = not calculated).
    public string? CalcOp { get; set; }
    public List<string> CalcFields { get; set; } = new();
}

/// <summary>Horizontal text alignment inside a form field (PDF /Q: 0 left, 1 centre, 2 right).</summary>
public enum FieldAlignment { Left, Center, Right }

/// <summary>A widget rectangle in PDF points (Y from bottom-left).</summary>
public readonly record struct FieldBounds(double Left, double Bottom, double Width, double Height);
