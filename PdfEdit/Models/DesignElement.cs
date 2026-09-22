using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PdfEdit.Models;

public enum DesignTool
{
    Select, Text, Rectangle, Ellipse, Line, Arrow, Pen, Image
}

public enum DesignElementType
{
    Text, Rectangle, Ellipse, Line, Arrow, Image, Freehand
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
}

public class ImageDesignElement : DesignElement
{
    private BitmapSource? _bitmap;
    private string _filePath = string.Empty;

    public override DesignElementType ElementType => DesignElementType.Image;

    public BitmapSource? Bitmap { get => _bitmap; set { _bitmap = value; OnPropertyChanged(); } }
    public string FilePath { get => _filePath; set { _filePath = value; OnPropertyChanged(); } }
}

public class FreehandDesignElement : DesignElement
{
    private Color _color = Colors.Black;
    private double _thickness = 2;

    public override DesignElementType ElementType => DesignElementType.Freehand;

    // Each inner list is one stroke (sequence of points)
    public List<List<Point>> Strokes { get; set; } = new();
    public Color Color { get => _color; set { _color = value; OnPropertyChanged(); } }
    public double Thickness { get => _thickness; set { _thickness = value; OnPropertyChanged(); } }
}

public enum DesignPageSize { A4, Letter, A3, Custom }
