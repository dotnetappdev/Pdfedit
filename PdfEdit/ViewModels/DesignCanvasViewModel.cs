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
    private string _fontFamily = "Segoe UI";
    private double _fontSize = 14;
    private bool _bold, _italic, _underline;
    private Color _textColor = Colors.Black;
    private TextAlignment _textAlignment = TextAlignment.Left;
    private Color _penColor = Colors.Black;
    private double _penThickness = 2;

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
        if (_selectedElement == null) return;
        SaveUndo();
        Elements.Remove(_selectedElement);
        SelectedElement = null;
    }

    public void SelectAll()
    {
        foreach (var e in Elements) e.IsSelected = true;
        SelectedElement = Elements.LastOrDefault();
    }

    public void ClearSelection()
    {
        foreach (var e in Elements) e.IsSelected = false;
        SelectedElement = null;
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
        SelectedElement = null;
        Elements.Clear();
        foreach (var e in state) Elements.Add(e);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private static DesignElement CloneElement(DesignElement src) => src switch
    {
        TextDesignElement t    => CloneText(t),
        ShapeDesignElement sh  => CloneShape(sh),
        ImageDesignElement im  => CloneImage(im),
        FreehandDesignElement f=> CloneFreehand(f),
        _                      => src
    };

    private static TextDesignElement CloneText(TextDesignElement t) => new()
    {
        X = t.X, Y = t.Y, Width = t.Width, Height = t.Height, ZOrder = t.ZOrder,
        Text = t.Text, FontFamily = t.FontFamily, FontSize = t.FontSize,
        Bold = t.Bold, Italic = t.Italic, Underline = t.Underline,
        Color = t.Color, BgColor = t.BgColor, Alignment = t.Alignment
    };

    private static ShapeDesignElement CloneShape(ShapeDesignElement sh) => new(sh.ElementType)
    {
        X = sh.X, Y = sh.Y, Width = sh.Width, Height = sh.Height, ZOrder = sh.ZOrder,
        FillColor = sh.FillColor, StrokeColor = sh.StrokeColor,
        StrokeThickness = sh.StrokeThickness, CornerRadius = sh.CornerRadius
    };

    private static ImageDesignElement CloneImage(ImageDesignElement im) => new()
    {
        X = im.X, Y = im.Y, Width = im.Width, Height = im.Height, ZOrder = im.ZOrder,
        Bitmap = im.Bitmap, FilePath = im.FilePath
    };

    private static FreehandDesignElement CloneFreehand(FreehandDesignElement f) => new()
    {
        X = f.X, Y = f.Y, Width = f.Width, Height = f.Height, ZOrder = f.ZOrder,
        Color = f.Color, Thickness = f.Thickness,
        Strokes = f.Strokes.Select(s => s.ToList()).ToList()
    };

    // ── Sync format from selected element ────────────────────────────────────

    private void SyncFormatFromSelection()
    {
        if (_selectedElement is TextDesignElement t)
        {
            _fontFamily = t.FontFamily;   OnPropertyChanged(nameof(FontFamily));
            _fontSize   = t.FontSize;     OnPropertyChanged(nameof(FontSize));
            _bold       = t.Bold;         OnPropertyChanged(nameof(Bold));
            _italic     = t.Italic;       OnPropertyChanged(nameof(Italic));
            _underline  = t.Underline;    OnPropertyChanged(nameof(Underline));
            _textColor  = t.Color;        OnPropertyChanged(nameof(TextColor));
            _textAlignment = t.Alignment; OnPropertyChanged(nameof(TextAlignment));
        }
        else if (_selectedElement is ShapeDesignElement sh)
        {
            _fillColor        = sh.FillColor;       OnPropertyChanged(nameof(FillColor));
            _strokeColor      = sh.StrokeColor;     OnPropertyChanged(nameof(StrokeColor));
            _strokeThickness  = sh.StrokeThickness; OnPropertyChanged(nameof(StrokeThickness));
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

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
