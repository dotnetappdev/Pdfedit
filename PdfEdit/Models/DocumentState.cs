namespace PdfEdit.Models;

public class DocumentState
{
    public int LastPageIndex { get; set; }
    public double LastZoom { get; set; } = 1.0;
    public DateTime LastOpened { get; set; } = DateTime.Now;

    // Persisted annotations and signatures so they survive app restarts
    public List<FreeTextAnnotation> Annotations { get; set; } = new();
    public List<PlacedSignature> Signatures { get; set; } = new();
    // Comments (highlights, notes, shapes, text edits) — kept until saved, like the text above
    public List<HighlightAnnotation> Highlights { get; set; } = new();
    public List<StickyNoteAnnotation> StickyNotes { get; set; } = new();
    public List<ShapeAnnotation> Shapes { get; set; } = new();
    public List<TextEditMark> TextEdits { get; set; } = new();
    // Persisted field values (filled-in form data)
    public Dictionary<string, string> FieldValues { get; set; } = new();

    // Unsaved form-layout work (Edit Fields / Properties panel / Design), kept like Acrobat keeps an
    // edited document open: re-applied on reload and when the file is reopened, until it is saved.
    public List<FieldLayoutState> FieldLayouts { get; set; } = new();
    public List<FieldEditState> FieldEdits { get; set; } = new();
    public List<string> DeletedFields { get; set; } = new();
}

/// <summary>A moved / resized form-field widget (PDF points).</summary>
public class FieldLayoutState
{
    public string Name { get; set; } = string.Empty;
    public int WidgetIndex { get; set; }
    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>Form-field properties edited in the Properties panel.</summary>
public class FieldEditState
{
    public string Name { get; set; } = string.Empty;
    public string? PendingName { get; set; }
    public string? Tooltip { get; set; }
    public bool IsRequired { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsMultiline { get; set; }
    public FieldAlignment Alignment { get; set; }
    public double FontSize { get; set; }
    public string? BorderColor { get; set; }
    public string? FillColor { get; set; }
    public string TextColor { get; set; } = "#000000";
    public int MaxLength { get; set; }
    public bool IsComb { get; set; }
    public bool IsEditable { get; set; }
    public string? DateFormat { get; set; }
    public string? DefaultValue { get; set; }
    public List<string> Options { get; set; } = new();
}

/// <summary>A recent-file entry exposing filename separately so XAML needs no converter.</summary>
public class RecentFileEntry
{
    public string FullPath { get; }
    public string FileName { get; }
    public string Directory { get; }

    public RecentFileEntry(string path)
    {
        FullPath = path;
        FileName = System.IO.Path.GetFileName(path);
        Directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
    }
}
