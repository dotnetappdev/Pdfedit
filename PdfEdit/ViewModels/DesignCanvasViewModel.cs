using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using PdfEdit.Models;

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
            NotifyPositionProperties();
            SyncFormatFromSelection();
        }
    }

    public bool HasSelection  => _selectedElement != null;
    public bool SelectedIsText  => _selectedElement is TextDesignElement;
    public bool SelectedIsShape => _selectedElement is ShapeDesignElement;

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
        SelectedElement = element;
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

    public void LoadDesign(string path)
    {
        var (elems, ps, cw, ch, bg) = Services.DesignSerializerService.Load(path);
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

    public void AlignLeft()
    {
        if (_selectedElement == null) return;
        SaveUndo();
        _selectedElement.X = 0;
    }

    public void AlignRight()
    {
        if (_selectedElement == null) return;
        SaveUndo();
        _selectedElement.X = PageWidth - _selectedElement.Width;
    }

    public void AlignCenterH()
    {
        if (_selectedElement == null) return;
        SaveUndo();
        _selectedElement.X = (PageWidth - _selectedElement.Width) / 2;
    }

    public void AlignTop()
    {
        if (_selectedElement == null) return;
        SaveUndo();
        _selectedElement.Y = 0;
    }

    public void AlignBottom()
    {
        if (_selectedElement == null) return;
        SaveUndo();
        _selectedElement.Y = PageHeight - _selectedElement.Height;
    }

    public void AlignCenterV()
    {
        if (_selectedElement == null) return;
        SaveUndo();
        _selectedElement.Y = (PageHeight - _selectedElement.Height) / 2;
    }

    // ── Templates ─────────────────────────────────────────────────────────────

    public void LoadTemplate(string templateName)
    {
        SaveUndo();
        Elements.Clear();
        SelectedElement = null;

        switch (templateName)
        {
            case "Invoice":      LoadInvoiceTemplate(); break;
            case "Letter":       LoadLetterTemplate();  break;
            case "BusinessCard": LoadBusinessCardTemplate(); break;
            case "Certificate":  LoadCertificateTemplate(); break;
            case "Form":         LoadFormTemplate(); break;
            case "Resume":       LoadResumeTemplate(); break;
            case "Flyer":        LoadFlyerTemplate(); break;
        }
    }

    private void LoadInvoiceTemplate()
    {
        PageSize = DesignPageSize.A4;
        double w = PageWidth;

        Elements.Add(new TextDesignElement { X = 30, Y = 30, Width = w - 60, Height = 50, Text = "INVOICE", FontSize = 36, Bold = true, Color = Color.FromRgb(30, 80, 160), Alignment = TextAlignment.Left });
        Elements.Add(new TextDesignElement { X = 30, Y = 90, Width = 250, Height = 20, Text = "Your Company Name", FontSize = 13, Bold = true, Color = Color.FromRgb(60, 60, 60) });
        Elements.Add(new TextDesignElement { X = 30, Y = 112, Width = 250, Height = 18, Text = "123 Business Street, City, Country", FontSize = 10, Color = Color.FromRgb(100, 100, 100) });
        Elements.Add(new TextDesignElement { X = w - 200, Y = 90, Width = 170, Height = 20, Text = $"Invoice #: 001", FontSize = 11, Bold = true, Alignment = TextAlignment.Right, Color = Color.FromRgb(60, 60, 60) });
        Elements.Add(new TextDesignElement { X = w - 200, Y = 112, Width = 170, Height = 18, Text = $"Date: {DateTime.Now:dd MMM yyyy}", FontSize = 10, Alignment = TextAlignment.Right, Color = Color.FromRgb(100, 100, 100) });
        Elements.Add(new ShapeDesignElement(DesignElementType.Line) { X = 30, Y = 145, Width = w - 60, Height = 2, StrokeColor = Color.FromRgb(30, 80, 160), StrokeThickness = 2 });

        var table = new TableDesignElement { X = 30, Y = 165, Width = w - 60, Height = 200, Rows = 6, Columns = 4 };
        table.SetCell(0, 0, "Description"); table.SetCell(0, 1, "Qty"); table.SetCell(0, 2, "Unit Price"); table.SetCell(0, 3, "Total");
        table.SetCell(1, 0, "Service / Product 1"); table.SetCell(1, 1, "1"); table.SetCell(1, 2, "$100.00"); table.SetCell(1, 3, "$100.00");
        table.SetCell(2, 0, "Service / Product 2"); table.SetCell(2, 1, "2"); table.SetCell(2, 2, "$50.00"); table.SetCell(2, 3, "$100.00");
        Elements.Add(table);

        Elements.Add(new TextDesignElement { X = w - 200, Y = 385, Width = 170, Height = 22, Text = "TOTAL: $200.00", FontSize = 14, Bold = true, Alignment = TextAlignment.Right, Color = Color.FromRgb(30, 80, 160) });
        Elements.Add(new TextDesignElement { X = 30, Y = 440, Width = w - 60, Height = 18, Text = "Payment due within 30 days. Thank you for your business!", FontSize = 10, Color = Color.FromRgb(100, 100, 100) });

        foreach (var e in Elements) e.ZOrder = Elements.IndexOf(e);
    }

    private void LoadLetterTemplate()
    {
        PageSize = DesignPageSize.A4;
        double w = PageWidth;
        Elements.Add(new TextDesignElement { X = 30, Y = 30, Width = 200, Height = 25, Text = "Your Name", FontSize = 14, Bold = true });
        Elements.Add(new TextDesignElement { X = 30, Y = 56, Width = 200, Height = 18, Text = "Your Address", FontSize = 10, Color = Color.FromRgb(100,100,100) });
        Elements.Add(new TextDesignElement { X = 30, Y = 74, Width = 200, Height = 18, Text = $"{DateTime.Now:dd MMMM yyyy}", FontSize = 10 });
        Elements.Add(new TextDesignElement { X = 30, Y = 120, Width = 300, Height = 20, Text = "Recipient Name", FontSize = 12, Bold = true });
        Elements.Add(new TextDesignElement { X = 30, Y = 142, Width = 300, Height = 18, Text = "Company Name", FontSize = 10 });
        Elements.Add(new TextDesignElement { X = 30, Y = 185, Width = w - 60, Height = 20, Text = "Dear [Recipient Name],", FontSize = 12 });
        Elements.Add(new TextDesignElement { X = 30, Y = 215, Width = w - 60, Height = 80, Text = "I am writing to you regarding...\n\nPlease feel free to contact me should you have any questions.", FontSize = 11 });
        Elements.Add(new TextDesignElement { X = 30, Y = 320, Width = 200, Height = 18, Text = "Yours sincerely,", FontSize = 11 });
        Elements.Add(new TextDesignElement { X = 30, Y = 360, Width = 200, Height = 20, Text = "Your Name", FontSize = 12, Bold = true });
        foreach (var e in Elements) e.ZOrder = Elements.IndexOf(e);
    }

    private void LoadBusinessCardTemplate()
    {
        PageSize = DesignPageSize.Custom;
        CustomPageWidth = 252; CustomPageHeight = 144;
        double w = CustomPageWidth; double h = CustomPageHeight;

        Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = 0, Y = 0, Width = w, Height = h, FillColor = Color.FromRgb(25, 65, 140), StrokeColor = Colors.Transparent });
        Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = 0, Y = 0, Width = 8, Height = h, FillColor = Color.FromRgb(255, 180, 0), StrokeColor = Colors.Transparent });
        Elements.Add(new TextDesignElement { X = 24, Y = 28, Width = w - 32, Height = 28, Text = "John Smith", FontSize = 20, Bold = true, Color = Colors.White });
        Elements.Add(new TextDesignElement { X = 24, Y = 58, Width = w - 32, Height = 18, Text = "Senior Designer", FontSize = 11, Color = Color.FromArgb(200, 255, 255, 255) });
        Elements.Add(new ShapeDesignElement(DesignElementType.Line) { X = 24, Y = 82, Width = w - 48, Height = 1, StrokeColor = Color.FromArgb(80, 255, 255, 255), StrokeThickness = 1 });
        Elements.Add(new TextDesignElement { X = 24, Y = 90, Width = w - 32, Height = 16, Text = "john@example.com  |  +1 555 000 0000", FontSize = 9, Color = Color.FromArgb(180, 255, 255, 255) });
        Elements.Add(new TextDesignElement { X = 24, Y = 108, Width = w - 32, Height = 16, Text = "www.yourwebsite.com", FontSize = 9, Color = Color.FromArgb(180, 255, 255, 255) });
        foreach (var e in Elements) e.ZOrder = Elements.IndexOf(e);
    }

    private void LoadCertificateTemplate()
    {
        PageSize = DesignPageSize.Letter;
        double w = PageWidth; double h = PageHeight;

        Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = 0, Y = 0, Width = w, Height = h, FillColor = Color.FromRgb(253, 248, 230), StrokeColor = Colors.Transparent });
        Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = 20, Y = 20, Width = w - 40, Height = h - 40, FillColor = Colors.Transparent, StrokeColor = Color.FromRgb(180, 140, 60), StrokeThickness = 3 });
        Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = 26, Y = 26, Width = w - 52, Height = h - 52, FillColor = Colors.Transparent, StrokeColor = Color.FromRgb(180, 140, 60), StrokeThickness = 1 });
        Elements.Add(new TextDesignElement { X = 40, Y = 60, Width = w - 80, Height = 30, Text = "Certificate of Achievement", FontSize = 11, Color = Color.FromRgb(130, 100, 40), Alignment = TextAlignment.Center });
        Elements.Add(new TextDesignElement { X = 40, Y = 120, Width = w - 80, Height = 55, Text = "Certificate of Excellence", FontSize = 40, Bold = true, Color = Color.FromRgb(100, 70, 20), Alignment = TextAlignment.Center });
        Elements.Add(new TextDesignElement { X = 40, Y = 210, Width = w - 80, Height = 25, Text = "This is to certify that", FontSize = 14, Color = Color.FromRgb(80, 60, 30), Alignment = TextAlignment.Center });
        Elements.Add(new TextDesignElement { X = 40, Y = 248, Width = w - 80, Height = 40, Text = "Recipient Name", FontSize = 28, Bold = true, Italic = true, Color = Color.FromRgb(30, 80, 160), Alignment = TextAlignment.Center });
        Elements.Add(new ShapeDesignElement(DesignElementType.Line) { X = 120, Y = 298, Width = w - 240, Height = 1, StrokeColor = Color.FromRgb(180, 140, 60), StrokeThickness = 1.5 });
        Elements.Add(new TextDesignElement { X = 40, Y = 310, Width = w - 80, Height = 25, Text = "has successfully completed the requirements for", FontSize = 12, Color = Color.FromRgb(80, 60, 30), Alignment = TextAlignment.Center });
        Elements.Add(new TextDesignElement { X = 40, Y = 345, Width = w - 80, Height = 30, Text = "Outstanding Performance Award", FontSize = 16, Bold = true, Color = Color.FromRgb(30, 80, 160), Alignment = TextAlignment.Center });
        Elements.Add(new TextDesignElement { X = 80, Y = 660, Width = 180, Height = 20, Text = "Authorized Signature", FontSize = 10, Alignment = TextAlignment.Center, Color = Color.FromRgb(80, 60, 30) });
        Elements.Add(new TextDesignElement { X = w - 260, Y = 660, Width = 180, Height = 20, Text = $"Date: {DateTime.Now:dd MMMM yyyy}", FontSize = 10, Alignment = TextAlignment.Center, Color = Color.FromRgb(80, 60, 30) });
        foreach (var e in Elements) e.ZOrder = Elements.IndexOf(e);
    }

    private void LoadFormTemplate()
    {
        PageSize = DesignPageSize.A4;
        double w = PageWidth;
        Elements.Add(new TextDesignElement { X = 30, Y = 30, Width = w - 60, Height = 35, Text = "Application Form", FontSize = 24, Bold = true, Alignment = TextAlignment.Center, Color = Color.FromRgb(30, 80, 160) });
        Elements.Add(new ShapeDesignElement(DesignElementType.Line) { X = 30, Y = 72, Width = w - 60, Height = 2, StrokeColor = Color.FromRgb(30, 80, 160), StrokeThickness = 2 });

        var fields = new[] { ("Full Name", 100), ("Email Address", 150), ("Phone Number", 200), ("Address", 250), ("Date of Birth", 300) };
        foreach (var (label, y) in fields)
        {
            Elements.Add(new TextDesignElement { X = 30, Y = y, Width = 200, Height = 20, Text = label + ":", FontSize = 11, Bold = true, Color = Color.FromRgb(60, 60, 60) });
            Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = 240, Y = y, Width = w - 270, Height = 22, FillColor = Colors.White, StrokeColor = Color.FromRgb(180, 180, 180), StrokeThickness = 1 });
        }

        Elements.Add(new TextDesignElement { X = 30, Y = 365, Width = 200, Height = 20, Text = "Comments:", FontSize = 11, Bold = true, Color = Color.FromRgb(60, 60, 60) });
        Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = 30, Y = 390, Width = w - 60, Height = 80, FillColor = Colors.White, StrokeColor = Color.FromRgb(180, 180, 180), StrokeThickness = 1 });
        foreach (var e in Elements) e.ZOrder = Elements.IndexOf(e);
    }

    private void LoadResumeTemplate()
    {
        PageSize = DesignPageSize.A4;
        double w = PageWidth; double h = PageHeight;

        // Left sidebar
        Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = 0, Y = 0, Width = 175, Height = h, FillColor = Color.FromRgb(35, 55, 90), StrokeColor = Colors.Transparent });
        // Photo placeholder
        Elements.Add(new ShapeDesignElement(DesignElementType.Ellipse) { X = 37, Y = 30, Width = 100, Height = 100, FillColor = Color.FromArgb(60, 255, 255, 255), StrokeColor = Color.FromArgb(120, 255, 255, 255), StrokeThickness = 2 });
        Elements.Add(new TextDesignElement { X = 10, Y = 145, Width = 155, Height = 28, Text = "Your Name", FontSize = 18, Bold = true, Color = Colors.White, Alignment = TextAlignment.Center });
        Elements.Add(new TextDesignElement { X = 10, Y = 175, Width = 155, Height = 18, Text = "UX / Product Designer", FontSize = 10, Color = Color.FromArgb(180, 255, 255, 255), Alignment = TextAlignment.Center });
        Elements.Add(new ShapeDesignElement(DesignElementType.Line) { X = 20, Y = 204, Width = 135, Height = 1, StrokeColor = Color.FromArgb(80, 255, 255, 255), StrokeThickness = 1 });

        // Sidebar section headers
        foreach (var (text, y) in new[] { ("CONTACT", 218), ("SKILLS", 340), ("LANGUAGES", 480) })
            Elements.Add(new TextDesignElement { X = 14, Y = y, Width = 148, Height = 16, Text = text, FontSize = 8, Bold = true, Color = Color.FromRgb(200, 180, 100), Alignment = TextAlignment.Left });

        Elements.Add(new TextDesignElement { X = 14, Y = 240, Width = 148, Height = 72, Text = "📧 you@example.com\n📞 +1 555 000 0000\n🌐 linkedin.com/in/you\n📍 City, Country", FontSize = 9, Color = Color.FromArgb(200, 255, 255, 255) });

        var skills = new[] { "Figma", "Adobe XD", "Prototyping", "User Research" };
        for (int i = 0; i < skills.Length; i++)
        {
            Elements.Add(new TextDesignElement { X = 14, Y = 360 + i * 24, Width = 100, Height = 18, Text = skills[i], FontSize = 9, Color = Color.FromArgb(220, 255, 255, 255) });
            Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = 14, Y = 375 + i * 24, Width = 148, Height = 5, FillColor = Color.FromArgb(40, 255, 255, 255), StrokeColor = Colors.Transparent });
            Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = 14, Y = 375 + i * 24, Width = (float)(148 * (0.95 - i * 0.12)), Height = 5, FillColor = Color.FromRgb(200, 180, 100), StrokeColor = Colors.Transparent });
        }

        // Main content area
        Elements.Add(new TextDesignElement { X = 195, Y = 30, Width = w - 215, Height = 30, Text = "PROFESSIONAL SUMMARY", FontSize = 10, Bold = true, Color = Color.FromRgb(35, 55, 90) });
        Elements.Add(new ShapeDesignElement(DesignElementType.Line) { X = 195, Y = 62, Width = w - 215, Height = 1, StrokeColor = Color.FromRgb(35, 55, 90), StrokeThickness = 1.5 });
        Elements.Add(new TextDesignElement { X = 195, Y = 70, Width = w - 215, Height = 55, Text = "Passionate designer with 5+ years of experience crafting intuitive digital products. Focused on user-centered design and delivering measurable results.", FontSize = 10, Color = Color.FromRgb(60, 60, 60) });

        Elements.Add(new TextDesignElement { X = 195, Y = 145, Width = w - 215, Height = 24, Text = "EXPERIENCE", FontSize = 10, Bold = true, Color = Color.FromRgb(35, 55, 90) });
        Elements.Add(new ShapeDesignElement(DesignElementType.Line) { X = 195, Y = 170, Width = w - 215, Height = 1, StrokeColor = Color.FromRgb(35, 55, 90), StrokeThickness = 1.5 });
        foreach (var (company, role, dates, y2) in new[] {
            ("Acme Corp", "Lead UX Designer", "2021–Present", 180),
            ("Beta Studio", "Product Designer", "2018–2021", 250) })
        {
            Elements.Add(new TextDesignElement { X = 195, Y = y2, Width = w - 215, Height = 18, Text = role, FontSize = 11, Bold = true, Color = Color.FromRgb(40, 40, 40) });
            Elements.Add(new TextDesignElement { X = 195, Y = y2 + 18, Width = 200, Height = 16, Text = company, FontSize = 9, Color = Color.FromRgb(35, 55, 90) });
            Elements.Add(new TextDesignElement { X = w - 215 - 80, Y = y2 + 18, Width = 80, Height = 16, Text = dates, FontSize = 9, Color = Color.FromRgb(120, 120, 120), Alignment = TextAlignment.Right });
            Elements.Add(new TextDesignElement { X = 195, Y = y2 + 36, Width = w - 215, Height = 36, Text = "• Designed and iterated on key product features\n• Led cross-functional design sprints", FontSize = 9, Color = Color.FromRgb(80, 80, 80) });
        }

        foreach (var e in Elements) e.ZOrder = Elements.IndexOf(e);
    }

    private void LoadFlyerTemplate()
    {
        PageSize = DesignPageSize.A4;
        double w = PageWidth; double h = PageHeight;

        Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = 0, Y = 0, Width = w, Height = h, FillColor = Color.FromRgb(15, 15, 35), StrokeColor = Colors.Transparent });
        // Accent circles
        Elements.Add(new ShapeDesignElement(DesignElementType.Ellipse) { X = -60, Y = -60, Width = 240, Height = 240, FillColor = Color.FromArgb(60, 0, 180, 255), StrokeColor = Colors.Transparent });
        Elements.Add(new ShapeDesignElement(DesignElementType.Ellipse) { X = w - 120, Y = h - 160, Width = 200, Height = 200, FillColor = Color.FromArgb(50, 255, 80, 180), StrokeColor = Colors.Transparent });

        Elements.Add(new TextDesignElement { X = 30, Y = 80, Width = w - 60, Height = 30, Text = "SPECIAL EVENT", FontSize = 12, Bold = true, Color = Color.FromRgb(0, 200, 255), Alignment = TextAlignment.Center });
        Elements.Add(new TextDesignElement { X = 30, Y = 120, Width = w - 60, Height = 80, Text = "AMAZING\nCONFERENCE\n2026", FontSize = 42, Bold = true, Color = Colors.White, Alignment = TextAlignment.Center });
        Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = (w - 60) / 2 - 25, Y = 240, Width = 90, Height = 4, FillColor = Color.FromRgb(0, 200, 255), StrokeColor = Colors.Transparent });

        Elements.Add(new TextDesignElement { X = 30, Y = 270, Width = w - 60, Height = 30, Text = "The Future of Technology & Innovation", FontSize = 14, Italic = true, Color = Color.FromArgb(200, 255, 255, 255), Alignment = TextAlignment.Center });
        Elements.Add(new TextDesignElement { X = 30, Y = 330, Width = w - 60, Height = 25, Text = "📅 15–17 June 2026   📍 Convention Center, New York", FontSize = 12, Color = Color.FromArgb(200, 255, 255, 255), Alignment = TextAlignment.Center });

        Elements.Add(new ShapeDesignElement(DesignElementType.Rectangle) { X = w / 2 - 90, Y = 400, Width = 180, Height = 44, FillColor = Color.FromRgb(0, 200, 255), StrokeColor = Colors.Transparent, CornerRadius = 22 });
        Elements.Add(new TextDesignElement { X = w / 2 - 90, Y = 412, Width = 180, Height = 24, Text = "REGISTER NOW", FontSize = 13, Bold = true, Color = Color.FromRgb(15, 15, 35), Alignment = TextAlignment.Center });

        Elements.Add(new TextDesignElement { X = 30, Y = h - 80, Width = w - 60, Height = 20, Text = "www.amazingconf.example.com", FontSize = 11, Color = Color.FromArgb(160, 255, 255, 255), Alignment = TextAlignment.Center });

        foreach (var e in Elements) e.ZOrder = Elements.IndexOf(e);
    }

    public void ZoomIn()  => Zoom = Math.Min(5.0, _zoom + 0.1);
    public void ZoomOut() => Zoom = Math.Max(0.1, _zoom - 0.1);
    public void ZoomFit(double availableWidth, double availableHeight)
        => Zoom = Math.Min(availableWidth / PageWidth, availableHeight / PageHeight) * 0.9;

    // ── Undo / Redo ───────────────────────────────────────────────────────────

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
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(HasMultiSelection));
    }

    private static DesignElement CloneElement(DesignElement src) => src switch
    {
        TextDesignElement t     => CloneText(t),
        ShapeDesignElement sh   => CloneShape(sh),
        ImageDesignElement im   => CloneImage(im),
        FreehandDesignElement f => CloneFreehand(f),
        TableDesignElement tb   => CloneTable(tb),
        _                       => src
    };

    private static TextDesignElement CloneText(TextDesignElement t) => new()
    {
        X = t.X, Y = t.Y, Width = t.Width, Height = t.Height, ZOrder = t.ZOrder, Opacity = t.Opacity,
        Text = t.Text, FontFamily = t.FontFamily, FontSize = t.FontSize,
        Bold = t.Bold, Italic = t.Italic, Underline = t.Underline,
        Color = t.Color, BgColor = t.BgColor, Alignment = t.Alignment
    };

    private static ShapeDesignElement CloneShape(ShapeDesignElement sh) => new(sh.ElementType)
    {
        X = sh.X, Y = sh.Y, Width = sh.Width, Height = sh.Height, ZOrder = sh.ZOrder, Opacity = sh.Opacity,
        FillColor = sh.FillColor, StrokeColor = sh.StrokeColor,
        StrokeThickness = sh.StrokeThickness, CornerRadius = sh.CornerRadius
    };

    private static ImageDesignElement CloneImage(ImageDesignElement im) => new()
    {
        X = im.X, Y = im.Y, Width = im.Width, Height = im.Height, ZOrder = im.ZOrder, Opacity = im.Opacity,
        Bitmap = im.Bitmap, FilePath = im.FilePath
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
        }
        else if (_selectedElement is ShapeDesignElement sh)
        {
            _fillColor        = sh.FillColor;       OnPropertyChanged(nameof(FillColor));
            _strokeColor      = sh.StrokeColor;     OnPropertyChanged(nameof(StrokeColor));
            _strokeThickness  = sh.StrokeThickness; OnPropertyChanged(nameof(StrokeThickness));
            _cornerRadius     = sh.CornerRadius;    OnPropertyChanged(nameof(CornerRadius));
        }
    }

    // ── New text / shape factories (used by DesignCanvas mouse handler) ───────

    public TextDesignElement CreateTextElement(double x, double y) => new()
    {
        X = x, Y = y, Width = 200, Height = 40,
        FontFamily = _fontFamily, FontSize = _fontSize,
        Bold = _bold, Italic = _italic, Underline = _underline,
        Color = _textColor, Alignment = _textAlignment,
        Text = "Text"
    };

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

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
