using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PdfEdit.Models;

public enum DesignTool
{
    Select, Text, Rectangle, Ellipse, Line, Arrow, Pen, Image, Table, Checkmark, XMark,
    TextField, Memo, Checkbox, Radio, ComboBox, Signature,
    // Fill & Sign on the Design canvas: Fill types into / ticks form fields, Sign places a signature.
    Fill, Sign
}

public enum DesignElementType
{
    Text, Rectangle, Ellipse, Line, Arrow, Image, Freehand, Table, FormField
}

/// <summary>The kind of AcroForm field a placed FormFieldDesignElement will become on export.</summary>
public enum FormFieldKind
{
    Text, Memo, Checkbox, Radio, ComboBox, Signature
}

/// <summary>Where a form field's caption label is drawn relative to the field box.</summary>
public enum FieldLabelPosition
{
    None, Left, Right, Placeholder
}

public abstract class DesignElement : INotifyPropertyChanged
{
    private double _x, _y, _width = 120, _height = 40;
    private bool _isSelected;
    private int _zOrder;

    public Guid Id { get; } = Guid.NewGuid();
    public abstract DesignElementType ElementType { get; }

    public double X { get => _x; set { _x = value; OnPropertyChanged(); } }
    public double Y { get => _y; set { _y = value; OnPropertyChanged(); } }
    public double Width { get => _width; set { _width = Math.Max(4, value); OnPropertyChanged(); } }
    public double Height { get => _height; set { _height = Math.Max(4, value); OnPropertyChanged(); } }
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public int ZOrder { get => _zOrder; set { _zOrder = value; OnPropertyChanged(); } }

    private double _opacity = 1.0;
    private bool _isLocked;
    public double Opacity  { get => _opacity;   set { _opacity   = Math.Clamp(value, 0.0, 1.0); OnPropertyChanged(); } }
    public bool   IsLocked { get => _isLocked;  set { _isLocked  = value; OnPropertyChanged(); } }

    /// <summary>
    /// Reconstructed from the PDF page's own content (text lines, images, the artwork backdrop) when
    /// it was imported into Design. Those are the page itself, so they are not added to Live View.
    /// </summary>
    public bool IsFromPage { get; set; }

    /// <summary>
    /// The Live View annotation(s) this element stands for (their CommentInfo ids, comma-separated),
    /// so switching between Design and Live updates them instead of adding copies.
    /// </summary>
    public string? LiveId { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class TextDesignElement : DesignElement
{
    private string _text = "Double-click to edit";
    private string _fontFamily = "Segoe UI";
    private double _fontSize = 14;
    private bool _bold, _italic, _underline;
    private Color _color = Colors.Black;
    private Color _bgColor = Colors.Transparent;
    private TextAlignment _alignment = TextAlignment.Left;
    private bool _isEditing;

    public override DesignElementType ElementType => DesignElementType.Text;

    public string Text { get => _text; set { _text = value; OnPropertyChanged(); } }
    public string FontFamily { get => _fontFamily; set { _fontFamily = value; OnPropertyChanged(); } }
    public double FontSize { get => _fontSize; set { _fontSize = Math.Max(6, value); OnPropertyChanged(); } }
    public bool Bold { get => _bold; set { _bold = value; OnPropertyChanged(); } }
    public bool Italic { get => _italic; set { _italic = value; OnPropertyChanged(); } }
    public bool Underline { get => _underline; set { _underline = value; OnPropertyChanged(); } }
    public Color Color { get => _color; set { _color = value; OnPropertyChanged(); } }
    public Color BgColor { get => _bgColor; set { _bgColor = value; OnPropertyChanged(); } }
    public TextAlignment Alignment { get => _alignment; set { _alignment = value; OnPropertyChanged(); } }
    public bool IsEditing { get => _isEditing; set { _isEditing = value; OnPropertyChanged(); } }

    private bool _wrap = true;
    public bool Wrap { get => _wrap; set { _wrap = value; OnPropertyChanged(); } }
}

/// <summary>
/// A placeholder for an AcroForm field placed on the design canvas: Text, Memo (multiline),
/// Checkbox, Radio, ComboBox or Signature. Carries its own caption label, independent of the
/// field's live value, which is drawn as static (non-editable) text once exported to PDF.
/// </summary>
public class FormFieldDesignElement : DesignElement
{
    private readonly FormFieldKind _kind;
    private string _fieldName = "Field";
    private string _label = "Label";
    private FieldLabelPosition _labelPosition = FieldLabelPosition.Left;
    private double _labelOffset = 6;
    private bool _required;
    private bool _wrap = true;
    private string _optionsCsv = "Option 1, Option 2, Option 3";

    public override DesignElementType ElementType => DesignElementType.FormField;
    public FormFieldKind FieldKind => _kind;

    public FormFieldDesignElement(FormFieldKind kind)
    {
        _kind = kind;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Height)) OnPropertyChanged(nameof(ValueFontSize));
        };
    }

    public string FieldName { get => _fieldName; set { _fieldName = value; OnPropertyChanged(); } }
    public string Label { get => _label; set { _label = value; OnPropertyChanged(); } }
    public FieldLabelPosition LabelPosition { get => _labelPosition; set { _labelPosition = value; OnPropertyChanged(); } }
    public double LabelOffset { get => _labelOffset; set { _labelOffset = Math.Max(0, value); OnPropertyChanged(); } }
    public bool Required { get => _required; set { _required = value; OnPropertyChanged(); } }

    /// <summary>Text wrapping for Text/Memo field kinds — ignored by other kinds.</summary>
    public bool Wrap { get => _wrap; set { _wrap = value; OnPropertyChanged(); } }

    /// <summary>Comma-separated option list, used by ComboBox kind only.</summary>
    public string OptionsCsv { get => _optionsCsv; set { _optionsCsv = value; OnPropertyChanged(); } }

    // When imported from the open PDF: the Live View field this element stands for, so moving /
    // resizing / renaming it in Design is carried back to Live View (and vice versa).
    public string? SourceFieldName { get; set; }
    public int SourceWidgetIndex { get; set; } = -1;
    public int SourcePageNumber { get; set; }
    // Live View text annotation holding this field's caption (a new field with a Left / Right label)
    public string? LabelLiveId { get; set; }
    public IReadOnlyList<string> Options =>
        _optionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ── Filled-in value (Fill & Sign) ─────────────────────────────────────────
    private string _value = string.Empty;
    private string _exportValue = "Yes";

    /// <summary>
    /// The field's value. Text / Memo / ComboBox: the text. Checkbox: ExportValue when ticked, "Off"
    /// (or empty) when not. Radio: the group's current selection (this button is on when it equals ExportValue).
    /// </summary>
    public string Value
    {
        get => _value;
        set
        {
            _value = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasValue));
            OnPropertyChanged(nameof(IsOn));
        }
    }

    /// <summary>The value a checkbox / radio button stands for when it is on.</summary>
    public string ExportValue
    {
        get => _exportValue;
        set { _exportValue = string.IsNullOrEmpty(value) ? "Yes" : value; OnPropertyChanged(); OnPropertyChanged(nameof(IsOn)); }
    }

    public bool HasValue => _value.Length > 0;

    // ── Text appearance of the filled-in value (matches Live View) ────────────
    private double _fontSizePt;
    private TextAlignment _textAlign = TextAlignment.Left;

    /// <summary>The field's own font size in points (PDF /DA); 0 = auto.</summary>
    public double FontSizePt
    {
        get => _fontSizePt;
        set { _fontSizePt = Math.Max(0, value); OnPropertyChanged(); OnPropertyChanged(nameof(ValueFontSize)); }
    }

    public TextAlignment TextAlign { get => _textAlign; set { _textAlign = value; OnPropertyChanged(); } }

    // Font the value is shown in (the PDF field's own font when imported; Helvetica ≈ Arial otherwise).
    private string _valueFontFamily = "Arial";
    private bool _valueBold, _valueItalic;
    public string ValueFontFamily { get => _valueFontFamily; set { _valueFontFamily = value; OnPropertyChanged(); } }
    public FontWeight ValueFontWeight => _valueBold ? FontWeights.Bold : FontWeights.Normal;
    public FontStyle ValueFontStyle => _valueItalic ? FontStyles.Italic : FontStyles.Normal;
    public bool ValueBold { get => _valueBold; set { _valueBold = value; OnPropertyChanged(); OnPropertyChanged(nameof(ValueFontWeight)); } }
    public bool ValueItalic { get => _valueItalic; set { _valueItalic = value; OnPropertyChanged(); OnPropertyChanged(nameof(ValueFontStyle)); } }

    /// <summary>
    /// Font size the value is shown and typed in: the field's own size, otherwise auto like
    /// Acrobat (60% of the box height for one line, 12 pt for multi-line fields).
    /// </summary>
    public double ValueFontSize => _fontSizePt > 0 ? _fontSizePt
        : _kind == FormFieldKind.Memo ? 12
        : Math.Max(8, Height * 0.6);

    /// <summary>Stands for a field of the open PDF (shown like Live View: no caption hint).</summary>
    public bool IsImported => SourceFieldName != null;

    /// <summary>Checkbox ticked / radio button selected.</summary>
    public bool IsOn => _value.Length > 0 && (_value == _exportValue
        || (_kind == FormFieldKind.Checkbox && _value is "Yes" or "On" or "true" or "1"));
}

public class ShapeDesignElement : DesignElement
{
    private Color _fillColor = Colors.Transparent;
    private Color _strokeColor = Colors.Black;
    private double _strokeThickness = 2;
    private double _cornerRadius;
    private readonly DesignElementType _type;

    public override DesignElementType ElementType => _type;

    public ShapeDesignElement(DesignElementType type)
    {
        _type = type;
        if (type == DesignElementType.Rectangle)
            _fillColor = Color.FromArgb(30, 0, 120, 255);
    }

    public Color FillColor { get => _fillColor; set { _fillColor = value; OnPropertyChanged(); } }
    public Color StrokeColor { get => _strokeColor; set { _strokeColor = value; OnPropertyChanged(); } }
    public double StrokeThickness { get => _strokeThickness; set { _strokeThickness = Math.Max(0.5, value); OnPropertyChanged(); } }
    public double CornerRadius { get => _cornerRadius; set { _cornerRadius = value; OnPropertyChanged(); } }

    // Line / Arrow direction inside the bounding box. By default a line runs top-left → bottom-right;
    // FlipX / FlipY mirror the start and end so lines can be drawn in any direction (the arrowhead
    // is always at the end point).
    private bool _flipX, _flipY;
    public bool FlipX { get => _flipX; set { _flipX = value; OnPropertyChanged(); } }
    public bool FlipY { get => _flipY; set { _flipY = value; OnPropertyChanged(); } }

    /// <summary>Line start / end relative to the element's top-left corner (WPF coordinates).</summary>
    public (Point Start, Point End) GetLocalEndpoints() => (
        new Point(FlipX ? Width : 0, FlipY ? Height : 0),
        new Point(FlipX ? 0 : Width, FlipY ? 0 : Height));
}

public class ImageDesignElement : DesignElement
{
    private BitmapSource? _bitmap;
    private string _filePath = string.Empty;

    public override DesignElementType ElementType => DesignElementType.Image;

    public BitmapSource? Bitmap { get => _bitmap; set { _bitmap = value; OnPropertyChanged(); } }
    public string FilePath { get => _filePath; set { _filePath = value; OnPropertyChanged(); } }

    /// <summary>
    /// Set when this image is a signature placed with Fill &amp; Sign (PNG bytes). Signatures on a
    /// design imported from the open PDF are carried over to Live View as placed signatures.
    /// </summary>
    public byte[]? SignatureBytes { get; set; }

    /// <summary>The Live View signature this element stands for (not saved with the design).</summary>
    public PlacedSignature? LinkedSignature { get; set; }
}

public class FreehandDesignElement : DesignElement
{
    private Color _color = Colors.Black;
    private double _thickness = 2;

    public override DesignElementType ElementType => DesignElementType.Freehand;

    // Each inner list is one stroke (sequence of points), in the page coordinates the strokes were
    // drawn in. The element's X/Y/Width/Height may change afterwards (move / resize), so always map
    // points through GetTransformedStrokes() rather than using them directly.
    public List<List<Point>> Strokes { get; set; } = new();

    /// <summary>Strokes mapped into the element's current bounds (page coordinates).</summary>
    public IEnumerable<IEnumerable<Point>> GetTransformedStrokes() => GetStrokes(X, Y);

    /// <summary>Strokes mapped into the element's current size, relative to its top-left corner.</summary>
    public IEnumerable<IEnumerable<Point>> GetLocalStrokes() => GetStrokes(0, 0);

    private IEnumerable<IEnumerable<Point>> GetStrokes(double originX, double originY)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var stroke in Strokes)
            foreach (var p in stroke)
            {
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
            }
        if (minX == double.MaxValue) yield break;

        double sx = maxX - minX > 0.5 ? Width / (maxX - minX) : 1;
        double sy = maxY - minY > 0.5 ? Height / (maxY - minY) : 1;
        foreach (var stroke in Strokes)
            yield return stroke.Select(p => new Point(originX + (p.X - minX) * sx, originY + (p.Y - minY) * sy)).ToList();
    }
    public Color Color { get => _color; set { _color = value; OnPropertyChanged(); } }
    public double Thickness { get => _thickness; set { _thickness = value; OnPropertyChanged(); } }
}

public class TableDesignElement : DesignElement
{
    private int _rows = 3;
    private int _columns = 3;
    private Color _borderColor = Colors.Black;
    private Color _headerBgColor = Color.FromArgb(255, 220, 230, 245);
    private Color _cellBgColor = Colors.White;
    private double _borderThickness = 1;
    private List<List<string>> _cells = new();

    public override DesignElementType ElementType => DesignElementType.Table;

    public int Rows
    {
        get => _rows;
        set
        {
            _rows = Math.Max(1, value);
            ResizeCells();
            OnPropertyChanged();
        }
    }

    public int Columns
    {
        get => _columns;
        set
        {
            _columns = Math.Max(1, value);
            ResizeCells();
            OnPropertyChanged();
        }
    }

    public Color BorderColor  { get => _borderColor;   set { _borderColor   = value; OnPropertyChanged(); } }
    public Color HeaderBgColor{ get => _headerBgColor; set { _headerBgColor = value; OnPropertyChanged(); } }
    public Color CellBgColor  { get => _cellBgColor;   set { _cellBgColor   = value; OnPropertyChanged(); } }
    public double BorderThickness { get => _borderThickness; set { _borderThickness = value; OnPropertyChanged(); } }

    public List<List<string>> Cells
    {
        get => _cells;
        set { _cells = value; OnPropertyChanged(); }
    }

    public string GetCell(int row, int col)
    {
        if (row < _cells.Count && col < _cells[row].Count) return _cells[row][col];
        return string.Empty;
    }

    public void SetCell(int row, int col, string text)
    {
        ResizeCells();
        if (row < _cells.Count && col < _cells[row].Count)
        {
            _cells[row][col] = text;
            OnPropertyChanged(nameof(Cells));
        }
    }

    private void ResizeCells()
    {
        while (_cells.Count < _rows) _cells.Add(new List<string>());
        while (_cells.Count > _rows) _cells.RemoveAt(_cells.Count - 1);
        foreach (var row in _cells)
        {
            while (row.Count < _columns) row.Add(string.Empty);
            while (row.Count > _columns) row.RemoveAt(row.Count - 1);
        }
    }

    private List<string> EmptyRow()
    {
        var r = new List<string>();
        for (int i = 0; i < _columns; i++) r.Add(string.Empty);
        return r;
    }

    public void InsertRowBefore(int rowIndex)
    {
        rowIndex = Math.Clamp(rowIndex, 0, _rows);
        _cells.Insert(rowIndex, EmptyRow());
        _rows++;
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Cells));
    }

    public void InsertRowAfter(int rowIndex)
    {
        int at = Math.Clamp(rowIndex + 1, 0, _rows);
        _cells.Insert(at, EmptyRow());
        _rows++;
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Cells));
    }

    public void DeleteRow(int rowIndex)
    {
        if (_rows <= 1) return;
        rowIndex = Math.Clamp(rowIndex, 0, _rows - 1);
        _cells.RemoveAt(rowIndex);
        _rows--;
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Cells));
    }

    public void InsertColumnBefore(int colIndex)
    {
        colIndex = Math.Clamp(colIndex, 0, _columns);
        foreach (var row in _cells) row.Insert(colIndex, string.Empty);
        _columns++;
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Cells));
    }

    public void InsertColumnAfter(int colIndex)
    {
        int at = Math.Clamp(colIndex + 1, 0, _columns);
        foreach (var row in _cells) row.Insert(at, string.Empty);
        _columns++;
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Cells));
    }

    public void DeleteColumn(int colIndex)
    {
        if (_columns <= 1) return;
        colIndex = Math.Clamp(colIndex, 0, _columns - 1);
        foreach (var row in _cells) row.RemoveAt(colIndex);
        _columns--;
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Cells));
    }

    public TableDesignElement()
    {
        Width = 300;
        Height = 120;
        ResizeCells();
        // Default header labels
        for (int c = 0; c < _columns; c++) _cells[0][c] = $"Header {c + 1}";
    }
}

public enum DesignPageSize { A4, Letter, A3, Custom }

public record DesignPalette(
    string Name,
    Color Primary,
    Color Secondary,
    Color Accent,
    Color TextPrimary,
    Color Background,
    Color Border
)
{
    public static readonly IReadOnlyList<DesignPalette> BuiltIn = new[]
    {
        new DesignPalette(
            Name:        "Ocean Blue",
            Primary:     Color.FromRgb(13,  71,  161),
            Secondary:   Color.FromRgb(21,  101, 192),
            Accent:      Color.FromRgb(3,   169, 244),
            TextPrimary: Color.FromRgb(18,  18,  18),
            Background:  Color.FromRgb(227, 242, 253),
            Border:      Color.FromRgb(100, 181, 246)
        ),
        new DesignPalette(
            Name:        "Warm Earth",
            Primary:     Color.FromRgb(121, 85,  72),
            Secondary:   Color.FromRgb(161, 136, 127),
            Accent:      Color.FromRgb(255, 152, 0),
            TextPrimary: Color.FromRgb(33,  33,  33),
            Background:  Color.FromRgb(250, 245, 240),
            Border:      Color.FromRgb(188, 170, 164)
        ),
    };
}
