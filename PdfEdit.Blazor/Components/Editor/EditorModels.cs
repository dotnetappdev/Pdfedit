namespace PdfEdit.Blazor.Components.Editor;

public enum RibbonTab { Home, FillSign, Edit, View, Tools, AI, Design, Help }

public enum RightTab { Properties, Fields, Comments, Bookmarks, Search, AI }

public enum Backstage { Info, New, Open, SaveAs, Export, Close }

/// <summary>What a click (or drag) on the page does.</summary>
public enum Tool
{
    Select, Text, Date, Check, Cross, Dot, Signature, Note, Highlight, Redact, Rectangle, Ellipse,
    // Stamps, freehand ink, lines and the Measure tools
    Stamp, Ink, Line, Arrow, Distance, Perimeter, Area,
    // Prepare Form: add a form field
    FieldText, FieldCheckbox, FieldRadio, FieldCombo, FieldList, FieldDate, FieldSignature,
}

public enum DialogKind
{
    None, Password, Properties, Watermark, PageNumbers, HeaderFooter, Bates, Protect, Sanitize,
    Resize, NUp, Signature, Note, Merge, InsertPdf, Combine, ExtractRange, DeleteRange, ImportData,
    Statistics, Shortcuts, About, AiSettings, DetectFields, OpenDesign, DesignPicture, Ocr, ScanCamera, CompareUpload, Compare, CertSign, Signatures, Cloud, Batch, BulkFill,
}

/// <summary>What a click (or drag) on the design page does.</summary>
public enum DesignTool
{
    Select, Text, Rectangle, Ellipse, Line, Arrow, Table, Check, Cross,
    TextField, Memo, Checkbox, Radio, ComboBox, Signature,
}

public enum ExportFormat { Word, Excel, PowerPoint, Html, Markdown, Epub, Text, Images, Pictures }

public enum ItemKind
{
    Text, Mark, Signature, Note, Highlight, Redact, Rectangle, Ellipse,
    Stamp, Ink, Line, Arrow, Distance, Perimeter, Area,
}

/// <summary>
/// Something added on a page that isn't in the PDF yet (Apply Changes, Save and every page tool
/// write them in). Position and size are in PDF points from the page's top-left corner.
/// </summary>
public sealed class PageItem
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public ItemKind Kind { get; init; }
    public int Page { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 12;
    public string Color { get; set; } = "#000000";
    public byte[]? Image { get; set; }
    public string? ImageUrl { get; set; }
    /// <summary>Second line of a dynamic stamp ("By … at …").</summary>
    public string? Subtitle { get; set; }
    /// <summary>Ink, line and measure points, in points from the page's top-left (inside the box).</summary>
    public List<PdfEdit.Models.PointD>? Points { get; set; }
    public double LineWidth { get; set; } = 2;
    /// <summary>Unit a measurement is shown in: in, mm, cm or pt.</summary>
    public string Unit { get; set; } = "in";

    public bool IsSketch => Points != null;

    /// <summary>The measurement shown on a Distance, Perimeter or Area item.</summary>
    public string MeasureLabel()
    {
        var p = Points ?? [];
        return Kind switch
        {
            ItemKind.Distance when p.Count > 1 => PdfEdit.Services.Measurement.FormatLength(PdfEdit.Services.Measurement.Distance(p[0], p[^1]), Unit),
            ItemKind.Perimeter => PdfEdit.Services.Measurement.FormatLength(PdfEdit.Services.Measurement.PolyLength(p, closed: false), Unit),
            ItemKind.Area => PdfEdit.Services.Measurement.FormatArea(PdfEdit.Services.Measurement.PolyArea(p), Unit),
            _ => "",
        };
    }

    public string Describe() => Kind switch
    {
        ItemKind.Text => string.IsNullOrWhiteSpace(Text) ? "Text (empty)" : $"Text: {Text}",
        ItemKind.Mark => $"Mark {Text}",
        ItemKind.Signature => "Signature",
        ItemKind.Note => string.IsNullOrWhiteSpace(Text) ? "Sticky note" : $"Note: {Text}",
        ItemKind.Highlight => "Highlight",
        ItemKind.Redact => "Redaction (not applied yet)",
        ItemKind.Rectangle => "Rectangle",
        ItemKind.Ellipse => "Ellipse",
        ItemKind.Stamp => $"Stamp {Text}",
        ItemKind.Ink => "Drawing",
        ItemKind.Line => "Line",
        ItemKind.Arrow => "Arrow",
        ItemKind.Distance or ItemKind.Perimeter or ItemKind.Area => $"{Kind} {MeasureLabel()}",
        _ => Kind.ToString(),
    };
}
