namespace PdfEdit.Blazor.Components.Editor;

public enum RibbonTab { Home, FillSign, Edit, View, Tools, Forms, AI, Design, Help }

public enum RightTab { Properties, Fields, Comments, Bookmarks, Search, AI, AllTools }

public enum Backstage { Info, New, Open, SaveAs, Export, Close }

/// <summary>What a click (or drag) on the page does.</summary>
public enum Tool
{
    Select, Text, Date, Check, Cross, Dot, Signature, Note, Highlight, Redact, Rectangle, Ellipse,
    // Stamps, freehand ink, lines and the Measure tools
    Stamp, Ink, Line, Arrow, Distance, Perimeter, Area,
    // The floating toolbox's extra tools (as in the Windows app's quick-tools rail)
    Hand, Zoom, Callout, InsertText, ReplaceText, Underline, Squiggly, Strikeout, Cloud, Polygon, Polyline,
    Eraser, Circle, LineMark, VerticalText, Initials,
    // Tools tab: links, select text, edit the PDF's pictures, snapshot
    Link, SelectText, EditImages, Snapshot,
    // Prepare Form: add a form field
    FieldText, FieldCheckbox, FieldRadio, FieldCombo, FieldList, FieldDate, FieldSignature,
}

public enum DialogKind
{
    None, Password, Properties, Watermark, PageNumbers, HeaderFooter, Bates, Protect, Sanitize,
    Resize, NUp, Signature, Note, Merge, InsertPdf, Combine, ExtractRange, DeleteRange, ImportData,
    Statistics, Shortcuts, About, AiSettings, DetectFields, OpenDesign, DesignPicture, Ocr, ScanCamera, CompareUpload, Compare, CertSign, Signatures, Cloud, Batch, BulkFill, Translate,
    GoogleLink, Settings, Crop, Stamps, FindHighlight, FindReplace, AddBookmark, ImportXfdf, SearchFolder,
    AskAcross, DesignAi, Profiles, Attachments, Accessibility, Cleanup, MindMap, Tip, Snapshot, ImageEdit, Link, SelectedText,
}

/// <summary>What a click (or drag) on the design page does.</summary>
public enum DesignTool
{
    Select, Text, Rectangle, Ellipse, Line, Arrow, Table, Check, Cross,
    TextField, Memo, Checkbox, Radio, ComboBox, Signature,
    Pen,
}

public enum ExportFormat { Word, Excel, PowerPoint, Html, Markdown, Epub, Text, Images, Pictures }

public enum ItemKind
{
    Text, Mark, Signature, Note, Highlight, Redact, Rectangle, Ellipse,
    Stamp, Ink, Line, Arrow, Distance, Perimeter, Area,
    Callout, InsertText, ReplaceText, Cloud, Polygon, Polyline,
    /// <summary>A picture pasted or added on the page (saved like a signature).</summary>
    Picture,
}

/// <summary>
/// Something added on a page that isn't in the PDF yet (Apply Changes, Save and every page tool
/// write them in). Position and size are in PDF points from the page's top-left corner.
/// </summary>
/// <summary>Auto-size the box to the text, wrap and grow taller, or keep the box as sized.</summary>
public enum TextFit { Auto, Wrap, Fixed }

public sealed class PageItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>
    /// For an item read back from the PDF (one of PdfEdit's own annotations): how it was when read, so
    /// only changed items are written again. Null for something added since.
    /// </summary>
    public string? Baseline { get; set; }
    /// <summary>Can't be moved, resized, edited or deleted until unlocked (text is saved with the PDF's Locked flag).</summary>
    public bool Locked { get; set; }
    /// <summary>Who added it (kept when an annotation from the PDF is written again).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Author { get => Comment.Author is { Length: > 0 } a ? a : null; set => Comment.Author = value ?? ""; }
    /// <summary>The review thread (Comments panel): note, replies, status, checkmark, dates. Its Id is the item's.</summary>
    public PdfEdit.Models.CommentInfo Comment { get; set; } = new() { Author = "" };
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
    /// <summary>The picture as a data: URL for the page (made again from <see cref="Image"/> when a draft is restored).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ImageUrl { get; set; }
    /// <summary>Second line of a dynamic stamp ("By … at …").</summary>
    public string? Subtitle { get; set; }
    /// <summary>Ink, line and measure points, in points from the page's top-left (inside the box).</summary>
    public List<PdfEdit.Models.PointD>? Points { get; set; }
    public double LineWidth { get; set; } = 2;
    /// <summary>Unit a measurement is shown in: in, mm, cm or pt.</summary>
    public string Unit { get; set; } = "in";

    public bool IsSketch => Points != null && Kind != ItemKind.Callout;
    /// <summary>Highlight, underline, squiggly or strikethrough (a Highlight item).</summary>
    public PdfEdit.Models.HighlightKind Markup { get; set; } = PdfEdit.Models.HighlightKind.Highlight;
    /// <summary>Turn of text, marks and stamps on the page: 0, 90, 180 or 270 degrees clockwise.</summary>
    public int Rotation { get; set; }
    /// <summary>Text that reads upwards (the toolbox's Vertical text): a 270° turn.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Vertical { get => Rotation == 270; set => Rotation = value ? 270 : 0; }
    public bool QuarterTurn => Rotation is 90 or 270;
    /// <summary>A placed date: the date and the format its text is written in (the Date rows in Properties).</summary>
    public DateTime? DateValue { get; set; }
    public string? DateFormat { get; set; }
    /// <summary>Extra space between letters, in points (the toolbar's VA).</summary>
    public double CharSpacing { get; set; }
    /// <summary>How a text box follows its text (the toolbar's Fit menu).</summary>
    public TextFit Fit { get; set; } = TextFit.Auto;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Upper { get; set; }
    public PdfEdit.Models.TextAlign Align { get; set; } = PdfEdit.Models.TextAlign.Left;
    /// <summary>Highlight opacity (0.1–1).</summary>
    public double Opacity { get; set; } = 0.4;

    /// <summary>A copy (new Id) for Copy / Paste.</summary>
    public PageItem Copy() => new()
    {
        Kind = Kind, Page = Page, Left = Left, Top = Top, Width = Width, Height = Height, Text = Text, FontSize = FontSize,
        Color = Color, Image = Image, ImageUrl = ImageUrl, Subtitle = Subtitle, LineWidth = LineWidth, Unit = Unit,
        Points = Points?.Select(p => new PdfEdit.Models.PointD(p.X, p.Y)).ToList(), Markup = Markup, Rotation = Rotation, CharSpacing = CharSpacing, Fit = Fit, DateValue = DateValue, DateFormat = DateFormat,
        Locked = Locked, Comment = new() { Author = Comment.Author, Note = Comment.Note },
        Bold = Bold, Italic = Italic, Underline = Underline, Upper = Upper, Align = Align, Opacity = Opacity,
    };

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
        ItemKind.Picture => "Picture",
        ItemKind.Note => string.IsNullOrWhiteSpace(Text) ? "Sticky note" : $"Note: {Text}",
        ItemKind.Highlight => Markup switch
        {
            PdfEdit.Models.HighlightKind.Underline => "Underline",
            PdfEdit.Models.HighlightKind.Squiggly => "Squiggly underline",
            PdfEdit.Models.HighlightKind.Strikethrough => "Strikethrough",
            _ => "Highlight",
        },
        ItemKind.Callout => string.IsNullOrWhiteSpace(Text) ? "Callout" : $"Callout: {Text}",
        ItemKind.InsertText => string.IsNullOrWhiteSpace(Text) ? "Insert text" : $"Insert “{Text}”",
        ItemKind.ReplaceText => string.IsNullOrWhiteSpace(Text) ? "Replace text" : $"Replace with “{Text}”",
        ItemKind.Cloud => "Cloud",
        ItemKind.Polygon => "Polygon",
        ItemKind.Polyline => "Polyline",
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
