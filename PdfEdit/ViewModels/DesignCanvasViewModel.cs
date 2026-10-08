using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

public class DesignCanvasViewModel : INotifyPropertyChanged
{
    private DesignTool _activeTool = DesignTool.Select;
    private DesignPageSize _pageSize = DesignPageSize.A4;
    private double _customPageWidth = 595;
    private double _customPageHeight = 842;
    private double _zoom = 1.0;
    private bool _showGrid;
    private bool _showRulers;
    private DesignElement? _selectedElement;

    // Format state (applied to new elements and editing selection)
    private Color _fillColor = Colors.Transparent;
    private Color _strokeColor = Colors.Black;
    private double _strokeThickness = 2;
    private double _cornerRadius;
    private string _fontFamily = "Segoe UI";
    private double _fontSize = 14;
    private bool _bold, _italic, _underline;
    private Color _textColor = Colors.Black;
    private Color _textBgColor = Colors.Transparent;
    private TextAlignment _textAlignment = TextAlignment.Left;
    private Color _penColor = Colors.Black;
    private double _penThickness = 2;
    private bool _wrap = true;

    // Form field format state
    private string _fieldName = "Field";
    private string _fieldLabel = "Label";
    private FieldLabelPosition _fieldLabelPosition = FieldLabelPosition.Left;
    private double _fieldLabelOffset = 6;
    private bool _fieldRequired;
    private string _fieldOptionsCsv = "Option 1, Option 2, Option 3";

    private double _elementOpacity = 1.0;
    private bool _elementLocked;
    private DesignElement? _clipboard;

    private bool _snapToGrid;
    private double _gridSize = 20;
    private Color _pageBackground = Colors.White;

    // Multi-selection
    private readonly HashSet<DesignElement> _multiSelection = new();

    private readonly Stack<List<DesignElement>> _undoStack = new();
    private readonly Stack<List<DesignElement>> _redoStack = new();

    public ObservableCollection<DesignElement> Elements { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Page dimensions ──────────────────────────────────────────────────────

    public double PageWidth => PageSize switch
    {
        DesignPageSize.A4     => 595,
        DesignPageSize.Letter => 612,
        DesignPageSize.A3     => 842,
        DesignPageSize.Custom => _customPageWidth,
        _                     => 595
    };

    public double PageHeight => PageSize switch
    {
        DesignPageSize.A4     => 842,
        DesignPageSize.Letter => 792,
        DesignPageSize.A3     => 1191,
        DesignPageSize.Custom => _customPageHeight,
        _                     => 842
    };

    // ── Properties ───────────────────────────────────────────────────────────

    public DesignTool ActiveTool
    {
        get => _activeTool;
        set { _activeTool = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsSelectTool)); }
    }

    public bool IsSelectTool => _activeTool == DesignTool.Select;

    public DesignPageSize PageSize
    {
        get => _pageSize;
        set { _pageSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageWidth)); OnPropertyChanged(nameof(PageHeight)); }
    }

    public double CustomPageWidth  { get => _customPageWidth;  set { _customPageWidth  = value; OnPropertyChanged(); if (_pageSize == DesignPageSize.Custom) OnPropertyChanged(nameof(PageWidth)); } }
    public double CustomPageHeight { get => _customPageHeight; set { _customPageHeight = value; OnPropertyChanged(); if (_pageSize == DesignPageSize.Custom) OnPropertyChanged(nameof(PageHeight)); } }

    public double Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, 0.1, 5.0); OnPropertyChanged(); OnPropertyChanged(nameof(ZoomPercent)); }
    }
    public string ZoomPercent => $"{_zoom:P0}";

    public bool ShowGrid  { get => _showGrid;   set { _showGrid   = value; OnPropertyChanged(); } }
    public bool ShowRulers{ get => _showRulers; set { _showRulers = value; OnPropertyChanged(); } }

    public DesignElement? SelectedElement
    {
        get => _selectedElement;
        set
        {
            if (_selectedElement != null) _selectedElement.IsSelected = false;
            _selectedElement = value;
            if (_selectedElement != null) _selectedElement.IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedIsText));
            OnPropertyChanged(nameof(SelectedIsShape));
            OnPropertyChanged(nameof(SelectedHasStroke));
            OnPropertyChanged(nameof(SelectedIsTable));
            OnPropertyChanged(nameof(SelectedIsFormField));
            OnPropertyChanged(nameof(SelectedIsTextLike));
            NotifyPositionProperties();
            SyncFormatFromSelection();
        }
    }

    public bool HasSelection    => _selectedElement != null;
    public bool SelectedIsText  => _selectedElement is TextDesignElement;
    public bool SelectedIsShape => _selectedElement is ShapeDesignElement;
    /// <summary>Shapes and freehand drawings: their colour / thickness show in the Inspector.</summary>
    public bool SelectedHasStroke => _selectedElement is ShapeDesignElement or FreehandDesignElement;
    public bool SelectedIsTable => _selectedElement is TableDesignElement;
    public bool SelectedIsFormField => _selectedElement is FormFieldDesignElement;
    /// <summary>True when the selection is a text-based component (plain Text, or a Text/Memo field) — used to gate the Wrap toggle.</summary>
    public bool SelectedIsTextLike =>
        _selectedElement is TextDesignElement ||
        _selectedElement is FormFieldDesignElement { FieldKind: FormFieldKind.Text or FormFieldKind.Memo };

    // ── Format properties ─────────────────────────────────────────────────────

    public Color FillColor
    {
        get => _fillColor;
        set
        {
            _fillColor = value;
            OnPropertyChanged();
            if (_selectedElement is ShapeDesignElement s) s.FillColor = value;
        }
    }

    public Color StrokeColor
    {
        get => _strokeColor;
        set
        {
            _strokeColor = value;
            OnPropertyChanged();
            if (_selectedElement is ShapeDesignElement s) s.StrokeColor = value;
            else if (_selectedElement is FreehandDesignElement fh) fh.Color = value;   // drawings: pen colour
        }
    }

    public double StrokeThickness
    {
        get => _strokeThickness;
        set
        {
            _strokeThickness = value;
            OnPropertyChanged();
            if (_selectedElement is ShapeDesignElement s) s.StrokeThickness = value;
            else if (_selectedElement is FreehandDesignElement fh) fh.Thickness = value;
        }
    }

    public double CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(0, value);
            OnPropertyChanged();
            if (_selectedElement is ShapeDesignElement s) s.CornerRadius = _cornerRadius;
        }
    }

    public string FontFamily
    {
        get => _fontFamily;
        set
        {
            _fontFamily = value;
            OnPropertyChanged();
            if (_selectedElement is TextDesignElement t) t.FontFamily = value;
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = Math.Max(6, value);
            OnPropertyChanged();
            if (_selectedElement is TextDesignElement t) t.FontSize = value;
        }
    }

    public bool Bold
    {
        get => _bold;
        set
        {
            _bold = value;
            OnPropertyChanged();
            if (_selectedElement is TextDesignElement t) t.Bold = value;
        }
    }

    public bool Italic
    {
        get => _italic;
        set
        {
            _italic = value;
            OnPropertyChanged();
            if (_selectedElement is TextDesignElement t) t.Italic = value;
        }
    }

    public bool Underline
    {
        get => _underline;
        set
        {
            _underline = value;
            OnPropertyChanged();
            if (_selectedElement is TextDesignElement t) t.Underline = value;
        }
    }

    public Color TextColor
    {
        get => _textColor;
        set
        {
            _textColor = value;
            OnPropertyChanged();
            if (_selectedElement is TextDesignElement t) t.Color = value;
        }
    }

    /// <summary>Text wrapping toggle, shared by plain Text elements and Text/Memo form fields.</summary>
    public bool Wrap
    {
        get => _wrap;
        set
        {
            _wrap = value;
            OnPropertyChanged();
            switch (_selectedElement)
            {
                case TextDesignElement t: t.Wrap = value; break;
                case FormFieldDesignElement { FieldKind: FormFieldKind.Text or FormFieldKind.Memo } f: f.Wrap = value; break;
            }
        }
    }

    // ── Form field format properties ──────────────────────────────────────────

    public string FieldName
    {
        get => _fieldName;
        set
        {
            _fieldName = value;
            OnPropertyChanged();
            if (_selectedElement is FormFieldDesignElement f) f.FieldName = value;
        }
    }

    public string FieldLabel
    {
        get => _fieldLabel;
        set
        {
            _fieldLabel = value;
            OnPropertyChanged();
            if (_selectedElement is FormFieldDesignElement f) f.Label = value;
        }
    }

    public FieldLabelPosition FieldLabelPosition
    {
        get => _fieldLabelPosition;
        set
        {
            _fieldLabelPosition = value;
            OnPropertyChanged();
            if (_selectedElement is FormFieldDesignElement f) f.LabelPosition = value;
        }
    }

    public double FieldLabelOffset
    {
        get => _fieldLabelOffset;
        set
        {
            _fieldLabelOffset = Math.Max(0, value);
            OnPropertyChanged();
            if (_selectedElement is FormFieldDesignElement f) f.LabelOffset = _fieldLabelOffset;
        }
    }

    public bool FieldRequired
    {
        get => _fieldRequired;
        set
        {
            _fieldRequired = value;
            OnPropertyChanged();
            if (_selectedElement is FormFieldDesignElement f) f.Required = value;
        }
    }

    public string FieldOptionsCsv
    {
        get => _fieldOptionsCsv;
        set
        {
            _fieldOptionsCsv = value;
            OnPropertyChanged();
            if (_selectedElement is FormFieldDesignElement f) f.OptionsCsv = value;
        }
    }

    public Color TextBgColor
    {
        get => _textBgColor;
        set
        {
            _textBgColor = value;
            OnPropertyChanged();
            if (_selectedElement is TextDesignElement t) t.BgColor = value;
        }
    }

    public TextAlignment TextAlignment
    {
        get => _textAlignment;
        set
        {
            _textAlignment = value;
            OnPropertyChanged();
            if (_selectedElement is TextDesignElement t) t.Alignment = value;
        }
    }

    public Color PenColor     { get => _penColor;     set { _penColor     = value; OnPropertyChanged(); } }
    public double PenThickness { get => _penThickness; set { _penThickness = value; OnPropertyChanged(); } }

    public bool SnapToGrid
    {
        get => _snapToGrid;
        set { _snapToGrid = value; OnPropertyChanged(); }
    }

    public double GridSize
    {
        get => _gridSize;
        set { _gridSize = Math.Max(4, value); OnPropertyChanged(); }
    }

    public Color PageBackground
    {
        get => _pageBackground;
        set { _pageBackground = value; OnPropertyChanged(); }
    }

    // ── Multi-selection ───────────────────────────────────────────────────────

    public IReadOnlySet<DesignElement> MultiSelection => _multiSelection;
    public bool HasMultiSelection => _multiSelection.Count > 1;

    public void SetMultiSelection(IEnumerable<DesignElement> elements)
    {
        foreach (var e in _multiSelection) e.IsSelected = false;
        _multiSelection.Clear();
        foreach (var e in elements) { e.IsSelected = true; _multiSelection.Add(e); }
        SelectedElement = _multiSelection.LastOrDefault();
        OnPropertyChanged(nameof(HasMultiSelection));
    }

    public void AddToMultiSelection(DesignElement element)
    {
        if (_multiSelection.Contains(element))
        {
            element.IsSelected = false;
            _multiSelection.Remove(element);
        }
        else
        {
            element.IsSelected = true;
            _multiSelection.Add(element);
        }
        SelectedElement = _multiSelection.LastOrDefault();
        OnPropertyChanged(nameof(HasMultiSelection));
    }

    public void MoveMultiSelection(double dx, double dy)
    {
        foreach (var e in _multiSelection)
        {
            e.X = Snap(Math.Max(0, e.X + dx));
            e.Y = Snap(Math.Max(0, e.Y + dy));
        }
    }

    public void DeleteMultiSelection()
    {
        if (_multiSelection.Count == 0) return;
        SaveUndo();
        foreach (var e in _multiSelection.ToList()) Elements.Remove(e);
        _multiSelection.Clear();
        SelectedElement = null;
        OnPropertyChanged(nameof(HasMultiSelection));
    }

    /// <summary>Snap a coordinate to the nearest grid point when SnapToGrid is enabled.</summary>
    public double Snap(double value) =>
        _snapToGrid && _gridSize > 0 ? Math.Round(value / _gridSize) * _gridSize : value;

    public double ElementOpacity
    {
        get => _elementOpacity;
        set
        {
            _elementOpacity = Math.Clamp(value, 0.0, 1.0);
            OnPropertyChanged();
            if (_selectedElement != null) _selectedElement.Opacity = _elementOpacity;
        }
    }

    public bool ElementLocked
    {
        get => _elementLocked;
        set
        {
            _elementLocked = value;
            OnPropertyChanged();
            if (_selectedElement != null) { _selectedElement.IsLocked = value; OnPropertyChanged(nameof(HasUnlockedSelection)); }
        }
    }

    public bool HasUnlockedSelection => _selectedElement != null && !_selectedElement.IsLocked;

    // Precise position / size properties (bound to ribbon spinners)
    public double SelectedX
    {
        get => _selectedElement?.X ?? 0;
        set { if (_selectedElement != null && !_selectedElement.IsLocked) { SaveUndo(); _selectedElement.X = value; } }
    }
    public double SelectedY
    {
        get => _selectedElement?.Y ?? 0;
        set { if (_selectedElement != null && !_selectedElement.IsLocked) { SaveUndo(); _selectedElement.Y = value; } }
    }
    public double SelectedWidth
    {
        get => _selectedElement?.Width ?? 0;
        set { if (_selectedElement != null && !_selectedElement.IsLocked) { SaveUndo(); _selectedElement.Width = Math.Max(4, value); } }
    }
    public double SelectedHeight
    {
        get => _selectedElement?.Height ?? 0;
        set { if (_selectedElement != null && !_selectedElement.IsLocked) { SaveUndo(); _selectedElement.Height = Math.Max(4, value); } }
    }

    public void NotifyPositionProperties()
    {
        OnPropertyChanged(nameof(SelectedX));
        OnPropertyChanged(nameof(SelectedY));
        OnPropertyChanged(nameof(SelectedWidth));
        OnPropertyChanged(nameof(SelectedHeight));
    }

    // ── Element operations ────────────────────────────────────────────────────

    public void AddElement(DesignElement element)
    {
        SaveUndo();
        element.ZOrder = Elements.Count;
        Elements.Add(element);
        // Reset any previous multi-selection so the new element gets single-selection resize handles.
        SetMultiSelection([element]);
    }

    public void DeleteSelected()
    {
        if (_multiSelection.Count > 1)
        {
            DeleteMultiSelection();
            return;
        }
        if (_selectedElement == null) return;
        SaveUndo();
        Elements.Remove(_selectedElement);
        SelectedElement = null;
    }

    public void SaveDesign(string path)
        => Services.DesignSerializerService.Save(Elements, _pageSize, _customPageWidth, _customPageHeight, _pageBackground, path);

    public void LoadDesign(string path) => LoadDesignDocument(DesignDocument.FromJson(System.IO.File.ReadAllText(path)));

    /// <summary>Replaces the canvas with a design (a .pdfdesign file's contents or a template).</summary>
    public void LoadDesignDocument(DesignDocument design)
    {
        var (elems, ps, cw, ch, bg) = Services.DesignSerializerService.Load(design);
        SaveUndo();
        Elements.Clear();
        SelectedElement = null;
        _multiSelection.Clear();
        foreach (var e in elems) Elements.Add(e);
        PageSize = ps;
        CustomPageWidth = cw;
        CustomPageHeight = ch;
        PageBackground = bg;
        OnPropertyChanged(nameof(HasMultiSelection));
    }

    /// <summary>Replaces the whole canvas (one undo step), e.g. with a form the AI designed.</summary>
    public void ReplaceAll(IEnumerable<DesignElement> elements, DesignPageSize pageSize, double customWidth = 0, double customHeight = 0)
    {
        SaveUndo();
        Elements.Clear();
        SelectedElement = null;
        _multiSelection.Clear();
        if (pageSize == DesignPageSize.Custom) { CustomPageWidth = customWidth; CustomPageHeight = customHeight; }
        PageSize = pageSize;
        int z = 0;
        foreach (var e in elements) { e.ZOrder = z++; Elements.Add(e); }
        ActiveTool = DesignTool.Select;
        OnPropertyChanged(nameof(HasMultiSelection));
    }

    public void SelectAll()
    {
        SetMultiSelection(Elements);
    }

    public void ClearSelection()
    {
        foreach (var e in Elements) e.IsSelected = false;
        _multiSelection.Clear();
        SelectedElement = null;
        OnPropertyChanged(nameof(HasMultiSelection));
    }

    public void BringForward()
    {
        if (_selectedElement == null) return;
        int idx = Elements.IndexOf(_selectedElement);
        if (idx < Elements.Count - 1)
        {
            Elements.Move(idx, idx + 1);
            _selectedElement.ZOrder = idx + 1;
            Elements[idx].ZOrder = idx;
        }
    }

    public void SendBackward()
    {
        if (_selectedElement == null) return;
        int idx = Elements.IndexOf(_selectedElement);
        if (idx > 0)
        {
            Elements.Move(idx, idx - 1);
            _selectedElement.ZOrder = idx - 1;
            Elements[idx].ZOrder = idx;
        }
    }

    public void BringToFront()
    {
        if (_selectedElement == null) return;
        int idx = Elements.IndexOf(_selectedElement);
        if (idx < Elements.Count - 1)
        {
            Elements.Move(idx, Elements.Count - 1);
            for (int i = 0; i < Elements.Count; i++) Elements[i].ZOrder = i;
        }
    }

    public void SendToBack()
    {
        if (_selectedElement == null) return;
        int idx = Elements.IndexOf(_selectedElement);
        if (idx > 0)
        {
            Elements.Move(idx, 0);
            for (int i = 0; i < Elements.Count; i++) Elements[i].ZOrder = i;
        }
    }

    public void CopySelected()
    {
        if (_selectedElement == null) return;
        _clipboard = CloneElement(_selectedElement);
    }

    public void PasteClipboard()
    {
        if (_clipboard == null) return;
        var clone = CloneElement(_clipboard);
        clone.X += 20; clone.Y += 20;
        AddElement(clone);
    }

    public void DuplicateSelected()
    {
        if (_selectedElement == null) return;
        var clone = CloneElement(_selectedElement);
        clone.X += 20; clone.Y += 20;
        AddElement(clone);
    }

    // ── Alignment ─────────────────────────────────────────────────────────────
    // One element selected → align to the page. Several selected → align to the primary
    // selection (the last one clicked), like Visual Studio's Format → Align.

    public void AlignLeft()    => Arrange(ArrangeOperation.AlignLefts);
    public void AlignRight()   => Arrange(ArrangeOperation.AlignRights);
    public void AlignCenterH() => Arrange(ArrangeOperation.AlignCenters);
    public void AlignTop()     => Arrange(ArrangeOperation.AlignTops);
    public void AlignBottom()  => Arrange(ArrangeOperation.AlignBottoms);
    public void AlignCenterV() => Arrange(ArrangeOperation.AlignMiddles);

    /// <summary>Align / distribute / size the selection as one undoable step.</summary>
    public void Arrange(ArrangeOperation op)
    {
        var items = (_multiSelection.Count > 1 ? _multiSelection.ToList()
                     : _selectedElement != null ? new List<DesignElement> { _selectedElement }
                     : new List<DesignElement>())
                    .Where(e => !e.IsLocked).ToList();
        if (items.Count == 0) return;
        if (ArrangeHelper.NeedsThree(op) && items.Count < 3) return;
        if (ArrangeHelper.NeedsTwo(op) && items.Count < 2) return;

        int reference = _selectedElement != null ? Math.Max(0, items.IndexOf(_selectedElement)) : items.Count - 1;
        var rects = items.Select(e => new Rect(e.X, e.Y, e.Width, e.Height)).ToList();
        var arranged = ArrangeHelper.Arrange(rects, reference, op, new Size(PageWidth, PageHeight));

        SaveUndo();
        for (int i = 0; i < items.Count; i++)
        {
            items[i].X = arranged[i].X;
            items[i].Y = arranged[i].Y;
            items[i].Width = arranged[i].Width;
            items[i].Height = arranged[i].Height;
        }
        NotifyPositionProperties();
        ArrangeApplied?.Invoke();
    }

    /// <summary>Raised after Arrange so the canvas can redraw its selection handles.</summary>
    public event Action? ArrangeApplied;

    // Choices for the Inspector drop-downs
    public IReadOnlyList<TextAlignment> TextAlignments { get; } =
        new[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right, TextAlignment.Justify };
    public IReadOnlyList<FieldLabelPosition> FieldLabelPositions { get; } = Enum.GetValues<FieldLabelPosition>();

    // ── Templates ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts a template from the shared catalog (PdfEdit.Core's TemplateCatalog, the same templates as
    /// the web version) on the canvas, replacing what's there as one undo step.
    /// </summary>
    public void LoadTemplate(string templateId) => LoadDesignDocument(Templates.TemplateCatalog.Create(templateId));

    public void ZoomIn()  => Zoom = Math.Min(5.0, _zoom + 0.1);
    public void ZoomOut() => Zoom = Math.Max(0.1, _zoom - 0.1);
    public void ZoomFit(double availableWidth, double availableHeight)
        => Zoom = Math.Min(availableWidth / PageWidth, availableHeight / PageHeight) * 0.9;

    // ── Undo / Redo ───────────────────────────────────────────────────────────

    /// <summary>Snapshots the page for undo just before an interactive mouse move/resize begins.</summary>
    public void BeginInteractiveEdit() => SaveUndo();

    private void SaveUndo()
    {
        _undoStack.Push(Elements.Select(CloneElement).ToList());
        _redoStack.Clear();
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Undo()
    {
        if (!CanUndo) return;
        _redoStack.Push(Elements.Select(CloneElement).ToList());
        RestoreState(_undoStack.Pop());
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _undoStack.Push(Elements.Select(CloneElement).ToList());
        RestoreState(_redoStack.Pop());
    }

    private void RestoreState(List<DesignElement> state)
    {
        _multiSelection.Clear();
        SelectedElement = null;
        Elements.Clear();
        foreach (var e in state) Elements.Add(e);
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(HasMultiSelection));
    }

    private static DesignElement CloneElement(DesignElement src)
    {
        var clone = CloneElementCore(src);
        clone.IsLocked = src.IsLocked; // undo/redo snapshots must not silently unlock elements
        // Undo snapshots keep the link to the page / Live View; a pasted copy that shares a LiveId
        // is given its own annotation by MainViewModel's Design → Live sync.
        clone.IsFromPage = src.IsFromPage;
        clone.LiveId = src.LiveId;
        if (src is FormFieldDesignElement sf && clone is FormFieldDesignElement cf) cf.LabelLiveId = sf.LabelLiveId;
        return clone;
    }

    private static DesignElement CloneElementCore(DesignElement src) => src switch
    {
        TextDesignElement t     => CloneText(t),
        ShapeDesignElement sh   => CloneShape(sh),
        ImageDesignElement im   => CloneImage(im),
        FreehandDesignElement f => CloneFreehand(f),
        TableDesignElement tb   => CloneTable(tb),
        FormFieldDesignElement ff => CloneFormField(ff),
        _                       => src
    };

    private static TextDesignElement CloneText(TextDesignElement t) => new()
    {
        X = t.X, Y = t.Y, Width = t.Width, Height = t.Height, ZOrder = t.ZOrder, Opacity = t.Opacity,
        Text = t.Text, FontFamily = t.FontFamily, FontSize = t.FontSize,
        Bold = t.Bold, Italic = t.Italic, Underline = t.Underline,
        Color = t.Color, BgColor = t.BgColor, Alignment = t.Alignment, Wrap = t.Wrap
    };

    private static FormFieldDesignElement CloneFormField(FormFieldDesignElement f) => new(f.FieldKind)
    {
        X = f.X, Y = f.Y, Width = f.Width, Height = f.Height, ZOrder = f.ZOrder, Opacity = f.Opacity,
        FieldName = f.FieldName, Label = f.Label, LabelPosition = f.LabelPosition,
        LabelOffset = f.LabelOffset, Required = f.Required, Wrap = f.Wrap, OptionsCsv = f.OptionsCsv,
        SourceFieldName = f.SourceFieldName, SourceWidgetIndex = f.SourceWidgetIndex, SourcePageNumber = f.SourcePageNumber,
        ExportValue = f.ExportValue, Value = f.Value, FontSizePt = f.FontSizePt, TextAlign = f.TextAlign,
        ValueFontFamily = f.ValueFontFamily, ValueBold = f.ValueBold, ValueItalic = f.ValueItalic
    };

    private static ShapeDesignElement CloneShape(ShapeDesignElement sh) => new(sh.ElementType)
    {
        X = sh.X, Y = sh.Y, Width = sh.Width, Height = sh.Height, ZOrder = sh.ZOrder, Opacity = sh.Opacity,
        FillColor = sh.FillColor, StrokeColor = sh.StrokeColor,
        StrokeThickness = sh.StrokeThickness, CornerRadius = sh.CornerRadius,
        FlipX = sh.FlipX, FlipY = sh.FlipY
    };

    private static ImageDesignElement CloneImage(ImageDesignElement im) => new()
    {
        X = im.X, Y = im.Y, Width = im.Width, Height = im.Height, ZOrder = im.ZOrder, Opacity = im.Opacity,
        Bitmap = im.Bitmap, FilePath = im.FilePath,
        SignatureBytes = im.SignatureBytes, LinkedSignature = im.LinkedSignature
    };

    private static FreehandDesignElement CloneFreehand(FreehandDesignElement f) => new()
    {
        X = f.X, Y = f.Y, Width = f.Width, Height = f.Height, ZOrder = f.ZOrder,
        Color = f.Color, Thickness = f.Thickness, Opacity = f.Opacity,
        Strokes = f.Strokes.Select(s => s.ToList()).ToList()
    };

    private static TableDesignElement CloneTable(TableDesignElement tb)
    {
        var clone = new TableDesignElement
        {
            X = tb.X, Y = tb.Y, Width = tb.Width, Height = tb.Height, ZOrder = tb.ZOrder,
            Opacity = tb.Opacity,
            BorderColor = tb.BorderColor, HeaderBgColor = tb.HeaderBgColor,
            CellBgColor = tb.CellBgColor, BorderThickness = tb.BorderThickness,
            Rows = tb.Rows, Columns = tb.Columns
        };
        clone.Cells = tb.Cells.Select(r => r.ToList()).ToList();
        return clone;
    }

    // ── Sync format from selected element ────────────────────────────────────

    private void SyncFormatFromSelection()
    {
        if (_selectedElement != null)
        {
            _elementOpacity = _selectedElement.Opacity; OnPropertyChanged(nameof(ElementOpacity));
            _elementLocked  = _selectedElement.IsLocked; OnPropertyChanged(nameof(ElementLocked));
            OnPropertyChanged(nameof(HasUnlockedSelection));
        }
        if (_selectedElement is TextDesignElement t)
        {
            _fontFamily = t.FontFamily;   OnPropertyChanged(nameof(FontFamily));
            _fontSize   = t.FontSize;     OnPropertyChanged(nameof(FontSize));
            _bold       = t.Bold;         OnPropertyChanged(nameof(Bold));
            _italic     = t.Italic;       OnPropertyChanged(nameof(Italic));
            _underline  = t.Underline;    OnPropertyChanged(nameof(Underline));
            _textColor   = t.Color;        OnPropertyChanged(nameof(TextColor));
            _textBgColor = t.BgColor;      OnPropertyChanged(nameof(TextBgColor));
            _textAlignment = t.Alignment;  OnPropertyChanged(nameof(TextAlignment));
            _wrap       = t.Wrap;         OnPropertyChanged(nameof(Wrap));
        }
        else if (_selectedElement is FreehandDesignElement fh)
        {
            _strokeColor     = fh.Color;     OnPropertyChanged(nameof(StrokeColor));
            _strokeThickness = fh.Thickness; OnPropertyChanged(nameof(StrokeThickness));
        }
        else if (_selectedElement is ShapeDesignElement sh)
        {
            _fillColor        = sh.FillColor;       OnPropertyChanged(nameof(FillColor));
            _strokeColor      = sh.StrokeColor;     OnPropertyChanged(nameof(StrokeColor));
            _strokeThickness  = sh.StrokeThickness; OnPropertyChanged(nameof(StrokeThickness));
            _cornerRadius     = sh.CornerRadius;    OnPropertyChanged(nameof(CornerRadius));
        }
        else if (_selectedElement is FormFieldDesignElement f)
        {
            _fieldName          = f.FieldName;       OnPropertyChanged(nameof(FieldName));
            _fieldLabel          = f.Label;           OnPropertyChanged(nameof(FieldLabel));
            _fieldLabelPosition  = f.LabelPosition;   OnPropertyChanged(nameof(FieldLabelPosition));
            _fieldLabelOffset    = f.LabelOffset;     OnPropertyChanged(nameof(FieldLabelOffset));
            _fieldRequired       = f.Required;        OnPropertyChanged(nameof(FieldRequired));
            _fieldOptionsCsv     = f.OptionsCsv;       OnPropertyChanged(nameof(FieldOptionsCsv));
            if (f.FieldKind is FormFieldKind.Text or FormFieldKind.Memo)
            { _wrap = f.Wrap; OnPropertyChanged(nameof(Wrap)); }
        }
    }

    // ── New text / shape factories (used by DesignCanvas mouse handler) ───────

    public TextDesignElement CreateTextElement(double x, double y) => new()
    {
        X = x, Y = y, Width = 200, Height = 40,
        FontFamily = _fontFamily, FontSize = _fontSize,
        Bold = _bold, Italic = _italic, Underline = _underline,
        Color = _textColor, Alignment = _textAlignment, Wrap = _wrap,
        Text = "Text"
    };

    private static int _fieldCounter;

    /// <summary>Places a new form-field placeholder at a drag-defined bounding box.</summary>
    public FormFieldDesignElement CreateFormFieldElement(FormFieldKind kind, double x, double y, double w, double h)
    {
        int n = ++_fieldCounter;
        return new FormFieldDesignElement(kind)
        {
            X = x, Y = y, Width = Math.Max(8, w), Height = Math.Max(8, h),
            FieldName = $"{kind}{n}",
            Label = kind.ToString(),
            LabelPosition = _fieldLabelPosition,
            LabelOffset = _fieldLabelOffset,
            Wrap = _wrap
        };
    }

    /// <summary>Places a new form-field placeholder with kind-appropriate default sizing.</summary>
    public FormFieldDesignElement CreateFormFieldElement(FormFieldKind kind, double x, double y)
    {
        (double w, double h) = kind switch
        {
            FormFieldKind.Text      => (180.0, 24.0),
            FormFieldKind.Memo      => (220.0, 90.0),
            FormFieldKind.Checkbox  => (120.0, 20.0),
            FormFieldKind.Radio     => (120.0, 20.0),
            FormFieldKind.ComboBox  => (180.0, 24.0),
            FormFieldKind.Signature => (220.0, 60.0),
            _                       => (180.0, 24.0)
        };
        return CreateFormFieldElement(kind, x, y, w, h);
    }

    // ── Fill & Sign ───────────────────────────────────────────────────────────

    private byte[]? _pendingSignature;
    /// <summary>The signature (PNG bytes) the Sign tool places on the next click.</summary>
    public byte[]? PendingSignature
    {
        get => _pendingSignature;
        set { _pendingSignature = value; OnPropertyChanged(); }
    }

    /// <summary>Sets a form field's filled-in value as one undo step.</summary>
    public void SetFieldValue(FormFieldDesignElement field, string value)
    {
        if (field.Value == value) return;
        SaveUndo();
        if (field.FieldKind == FormFieldKind.Radio)
        {
            // A radio group shares one value: selecting this button deselects its siblings.
            foreach (var r in Elements.OfType<FormFieldDesignElement>()
                         .Where(r => r.FieldKind == FormFieldKind.Radio && IsSameField(r, field)))
                r.Value = value;
        }
        else
        {
            foreach (var f in Elements.OfType<FormFieldDesignElement>().Where(f => IsSameField(f, field)))
                f.Value = value;
        }
        OnPropertyChanged(nameof(CanUndo));
    }

    private static bool IsSameField(FormFieldDesignElement a, FormFieldDesignElement b) =>
        ReferenceEquals(a, b)
        || (a.SourceFieldName != null ? a.SourceFieldName == b.SourceFieldName
                                      : b.SourceFieldName == null && a.FieldName == b.FieldName);

    /// <summary>Ticks / unticks a checkbox, or selects a radio button.</summary>
    public void ToggleField(FormFieldDesignElement field)
    {
        if (field.FieldKind == FormFieldKind.Radio)
            SetFieldValue(field, field.ExportValue);
        else if (field.FieldKind == FormFieldKind.Checkbox)
            SetFieldValue(field, field.IsOn ? "Off" : field.ExportValue);
    }

    /// <summary>
    /// A signature image fitted inside <paramref name="box"/> (keeping its aspect ratio), or
    /// centred on <paramref name="box"/>'s top-left at a default size when the box is empty.
    /// </summary>
    public ImageDesignElement? CreateSignatureElement(byte[] png, Rect box)
    {
        BitmapSource bmp;
        try
        {
            using var ms = new System.IO.MemoryStream(png);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = ms;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            bmp = bi;
        }
        catch { return null; }

        double aspect = bmp.PixelHeight > 0 ? (double)bmp.PixelWidth / bmp.PixelHeight : 3;
        double w, h, x, y;
        if (box.Width > 4 && box.Height > 4)
        {
            // Fit inside the signature field.
            w = box.Width; h = w / aspect;
            if (h > box.Height) { h = box.Height; w = h * aspect; }
            x = box.X + (box.Width - w) / 2;
            y = box.Y + (box.Height - h) / 2;
        }
        else
        {
            h = 50; w = h * aspect;
            if (w > 200) { w = 200; h = w / aspect; }
            x = box.X - w / 2;
            y = box.Y - h / 2;
        }
        return new ImageDesignElement
        {
            X = x, Y = y, Width = w, Height = h,
            Bitmap = bmp, SignatureBytes = png,
        };
    }

    public ShapeDesignElement CreateShapeElement(DesignElementType type, double x, double y, double w, double h)
        => new(type)
        {
            X = x, Y = y, Width = Math.Abs(w), Height = Math.Abs(h),
            FillColor = _fillColor, StrokeColor = _strokeColor, StrokeThickness = _strokeThickness
        };

    public ImageDesignElement CreateImageElement(string path, BitmapSource bmp, double x, double y) => new()
    {
        X = x, Y = y, Width = Math.Min(bmp.PixelWidth, 400), Height = Math.Min(bmp.PixelHeight, 400),
        FilePath = path, Bitmap = bmp
    };

    public FreehandDesignElement CreateFreehandElement(List<List<Point>> strokes, Rect bounds) => new()
    {
        X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height,
        Color = _penColor, Thickness = _penThickness, Strokes = strokes
    };

    public TableDesignElement CreateTableElement(double x, double y, int rows = 4, int cols = 3) => new()
    {
        X = x, Y = y, Rows = rows, Columns = cols, Width = 300, Height = 120
    };

    // ── Table row / column context (set by DesignCanvas on right-click) ───────

    public int TableContextRow    { get; set; } = 0;
    public int TableContextColumn { get; set; } = 0;

    public void TableInsertRowBefore()
    {
        if (_selectedElement is not TableDesignElement t) return;
        SaveUndo();
        t.InsertRowBefore(TableContextRow);
    }

    public void TableInsertRowAfter()
    {
        if (_selectedElement is not TableDesignElement t) return;
        SaveUndo();
        t.InsertRowAfter(TableContextRow);
    }

    public void TableDeleteRow()
    {
        if (_selectedElement is not TableDesignElement t) return;
        SaveUndo();
        t.DeleteRow(TableContextRow);
    }

    public void TableInsertColumnBefore()
    {
        if (_selectedElement is not TableDesignElement t) return;
        SaveUndo();
        t.InsertColumnBefore(TableContextColumn);
    }

    public void TableInsertColumnAfter()
    {
        if (_selectedElement is not TableDesignElement t) return;
        SaveUndo();
        t.InsertColumnAfter(TableContextColumn);
    }

    public void TableDeleteColumn()
    {
        if (_selectedElement is not TableDesignElement t) return;
        SaveUndo();
        t.DeleteColumn(TableContextColumn);
    }

    // ── Palette / theme ───────────────────────────────────────────────────────

    public void ApplyPalette(DesignPalette palette)
    {
        SaveUndo();
        PageBackground = palette.Background;
        foreach (var element in Elements)
        {
            switch (element)
            {
                case TextDesignElement t:
                    t.Color = palette.TextPrimary;
                    break;
                case ShapeDesignElement sh:
                    if (sh.FillColor.A > 0)
                        sh.FillColor = Color.FromArgb(sh.FillColor.A, palette.Secondary.R, palette.Secondary.G, palette.Secondary.B);
                    sh.StrokeColor = palette.Border;
                    break;
                case TableDesignElement tb:
                    tb.HeaderBgColor = palette.Primary;
                    tb.CellBgColor   = Color.FromArgb(200, palette.Background.R, palette.Background.G, palette.Background.B);
                    tb.BorderColor   = palette.Border;
                    break;
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
