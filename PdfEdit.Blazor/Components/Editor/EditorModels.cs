namespace PdfEdit.Blazor.Components.Editor;

public enum RibbonTab { Home, FillSign, Edit, View, Tools, Help }

public enum RightTab { Properties, Fields, Comments, Bookmarks, Search }

public enum Backstage { Info, New, Open, SaveAs, Export, Close }

/// <summary>What a click (or drag) on the page does.</summary>
public enum Tool { Select, Text, Date, Check, Cross, Dot, Signature, Note, Highlight, Redact, Rectangle, Ellipse }

public enum DialogKind
{
    None, Password, Properties, Watermark, PageNumbers, HeaderFooter, Bates, Protect, Sanitize,
    Resize, NUp, Signature, Note, Merge, InsertPdf, Combine, ExtractRange, DeleteRange, ImportData,
    Statistics, Shortcuts, About,
}

public enum ExportFormat { Word, Excel, PowerPoint, Html, Markdown, Epub, Text, Images, Pictures }

public enum ItemKind { Text, Mark, Signature, Note, Highlight, Redact, Rectangle, Ellipse }

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
        _ => Kind.ToString(),
    };
}
