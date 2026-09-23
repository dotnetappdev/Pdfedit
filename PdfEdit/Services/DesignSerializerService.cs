using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using PdfEdit.Models;

namespace PdfEdit.Services;

/// <summary>Serializes/deserializes a design canvas to/from a .pdfdesign JSON file.</summary>
public static class DesignSerializerService
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented  = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Public API ────────────────────────────────────────────────────────────

    public static void Save(
        IEnumerable<DesignElement> elements,
        DesignPageSize pageSize,
        double customW, double customH,
        Color bgColor,
        string path)
    {
        var doc = new DesignDoc
        {
            PageSize      = pageSize.ToString(),
            CustomWidth   = customW,
            CustomHeight  = customH,
            BgColor       = ColorToHex(bgColor),
            Elements      = elements.OrderBy(e => e.ZOrder).Select(ToDto).ToList()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(doc, _opts));
    }

    public static (List<DesignElement> Elements, DesignPageSize PageSize, double CustomW, double CustomH, Color BgColor) Load(string path)
    {
        var json = File.ReadAllText(path);
        var doc  = JsonSerializer.Deserialize<DesignDoc>(json, _opts)
                   ?? throw new InvalidDataException("Invalid design file.");

        var pageSize = Enum.TryParse<DesignPageSize>(doc.PageSize, out var ps) ? ps : DesignPageSize.A4;
        var bgColor  = ParseColor(doc.BgColor);

        var elements = new List<DesignElement>();
        int z = 0;
        foreach (var dto in doc.Elements ?? [])
        {
            var elem = FromDto(dto);
            if (elem == null) continue;
            elem.ZOrder = z++;
            elements.Add(elem);
        }

        return (elements, pageSize, doc.CustomWidth, doc.CustomHeight, bgColor);
    }

    // ── DTO ───────────────────────────────────────────────────────────────────

    private class DesignDoc
    {
        public string  PageSize     { get; set; } = "A4";
        public double  CustomWidth  { get; set; } = 595;
        public double  CustomHeight { get; set; } = 842;
        public string? BgColor      { get; set; }
        public List<ElementDto>? Elements { get; set; }
    }

    private class ElementDto
    {
        public string   Type         { get; set; } = "";
        // base
        public double   X            { get; set; }
        public double   Y            { get; set; }
        public double   W            { get; set; }
        public double   H            { get; set; }
        public double   Opacity      { get; set; } = 1.0;
        public bool     IsLocked     { get; set; }
        // text
        public string?  Text         { get; set; }
        public string?  FontFamily   { get; set; }
        public double   FontSize     { get; set; }
        public bool     Bold         { get; set; }
        public bool     Italic       { get; set; }
        public bool     Underline    { get; set; }
        public string?  Color        { get; set; }
        public string?  BgColor      { get; set; }
        public string?  Alignment    { get; set; }
        // shape
        public string?  ShapeType    { get; set; }
        public string?  FillColor    { get; set; }
        public string?  StrokeColor  { get; set; }
        public double   StrokeThick  { get; set; }
        public double   CornerRadius { get; set; }
        // image
        public string?  FilePath     { get; set; }
        // freehand
        public double   Thickness    { get; set; }
        public List<List<PointDto>>? Strokes { get; set; }
        // table
        public int     Rows          { get; set; }
        public int     Columns       { get; set; }
        public string? BorderColor   { get; set; }
        public string? HeaderBgColor { get; set; }
        public string? CellBgColor   { get; set; }
        public double  BorderThick   { get; set; }
        public List<List<string>>? Cells { get; set; }
        // form field
        public string? FieldKind        { get; set; }
        public string? FieldName        { get; set; }
        public string? Label            { get; set; }
        public string? LabelPosition    { get; set; }
        public double  LabelOffset      { get; set; }
        public bool    Required         { get; set; }
        public bool    Wrap             { get; set; } = true;
        public string? OptionsCsv       { get; set; }
    }

    private class PointDto { public double X { get; set; } public double Y { get; set; } }

    // ── Serialization helpers ─────────────────────────────────────────────────

    private static ElementDto ToDto(DesignElement e)
    {
        var dto = new ElementDto
        {
            X = e.X, Y = e.Y, W = e.Width, H = e.Height,
            Opacity = e.Opacity, IsLocked = e.IsLocked
        };

        switch (e)
        {
            case TextDesignElement t:
                dto.Type       = "text";
                dto.Text       = t.Text;
                dto.FontFamily = t.FontFamily;
                dto.FontSize   = t.FontSize;
                dto.Bold       = t.Bold;
                dto.Italic     = t.Italic;
                dto.Underline  = t.Underline;
                dto.Color      = ColorToHex(t.Color);
                dto.BgColor    = ColorToHex(t.BgColor);
                dto.Alignment  = t.Alignment.ToString();
                dto.Wrap       = t.Wrap;
                break;

            case FormFieldDesignElement f:
                dto.Type          = "field";
                dto.FieldKind     = f.FieldKind.ToString();
                dto.FieldName     = f.FieldName;
                dto.Label         = f.Label;
                dto.LabelPosition = f.LabelPosition.ToString();
                dto.LabelOffset   = f.LabelOffset;
                dto.Required      = f.Required;
                dto.Wrap          = f.Wrap;
                dto.OptionsCsv    = f.OptionsCsv;
                break;

            case ShapeDesignElement s:
                dto.Type        = "shape";
                dto.ShapeType   = s.ElementType.ToString();
                dto.FillColor   = ColorToHex(s.FillColor);
                dto.StrokeColor = ColorToHex(s.StrokeColor);
                dto.StrokeThick = s.StrokeThickness;
                dto.CornerRadius= s.CornerRadius;
                break;

            case ImageDesignElement im:
                dto.Type     = "image";
                dto.FilePath = im.FilePath;
                break;

            case FreehandDesignElement fh:
                dto.Type      = "freehand";
                dto.Color     = ColorToHex(fh.Color);
                dto.Thickness = fh.Thickness;
                dto.Strokes   = fh.Strokes.Select(s => s.Select(p => new PointDto { X = p.X, Y = p.Y }).ToList()).ToList();
                break;

            case TableDesignElement tb:
                dto.Type         = "table";
                dto.Rows         = tb.Rows;
                dto.Columns      = tb.Columns;
                dto.BorderColor  = ColorToHex(tb.BorderColor);
                dto.HeaderBgColor= ColorToHex(tb.HeaderBgColor);
                dto.CellBgColor  = ColorToHex(tb.CellBgColor);
                dto.BorderThick  = tb.BorderThickness;
                dto.Cells        = tb.Cells.Select(r => r.ToList()).ToList();
                break;
        }

        return dto;
    }

    private static DesignElement? FromDto(ElementDto dto) => dto.Type switch
    {
        "text"     => new TextDesignElement
        {
            X = dto.X, Y = dto.Y, Width = dto.W, Height = dto.H,
            Opacity = dto.Opacity, IsLocked = dto.IsLocked,
            Text       = dto.Text ?? "",
            FontFamily = dto.FontFamily ?? "Segoe UI",
            FontSize   = dto.FontSize > 0 ? dto.FontSize : 14,
            Bold       = dto.Bold, Italic = dto.Italic, Underline = dto.Underline,
            Color      = ParseColor(dto.Color),
            BgColor    = ParseColor(dto.BgColor),
            Alignment  = Enum.TryParse<System.Windows.TextAlignment>(dto.Alignment, out var ta) ? ta : System.Windows.TextAlignment.Left,
            Wrap       = dto.Wrap
        },

        "field" when Enum.TryParse<FormFieldKind>(dto.FieldKind, out var fk) =>
            new FormFieldDesignElement(fk)
            {
                X = dto.X, Y = dto.Y, Width = dto.W, Height = dto.H,
                Opacity = dto.Opacity, IsLocked = dto.IsLocked,
                FieldName     = dto.FieldName ?? "Field",
                Label         = dto.Label ?? "Label",
                LabelPosition = Enum.TryParse<FieldLabelPosition>(dto.LabelPosition, out var lp) ? lp : FieldLabelPosition.Left,
                LabelOffset   = dto.LabelOffset,
                Required      = dto.Required,
                Wrap          = dto.Wrap,
                OptionsCsv    = dto.OptionsCsv ?? ""
            },

        "shape" when Enum.TryParse<DesignElementType>(dto.ShapeType, out var st) =>
            new ShapeDesignElement(st)
            {
                X = dto.X, Y = dto.Y, Width = dto.W, Height = dto.H,
                Opacity = dto.Opacity, IsLocked = dto.IsLocked,
                FillColor        = ParseColor(dto.FillColor),
                StrokeColor      = ParseColor(dto.StrokeColor, Colors.Black),
                StrokeThickness  = dto.StrokeThick > 0 ? dto.StrokeThick : 2,
                CornerRadius     = dto.CornerRadius
            },

        "image" => new ImageDesignElement
        {
            X = dto.X, Y = dto.Y, Width = dto.W, Height = dto.H,
            Opacity = dto.Opacity, IsLocked = dto.IsLocked,
            FilePath = dto.FilePath ?? "",
            Bitmap = LoadBitmapSafe(dto.FilePath)
        },

        "freehand" => new FreehandDesignElement
        {
            X = dto.X, Y = dto.Y, Width = dto.W, Height = dto.H,
            Opacity = dto.Opacity, IsLocked = dto.IsLocked,
            Color     = ParseColor(dto.Color),
            Thickness = dto.Thickness > 0 ? dto.Thickness : 2,
            Strokes   = dto.Strokes?.Select(s => s.Select(p => new Point(p.X, p.Y)).ToList()).ToList() ?? new()
        },

        "table" => BuildTable(dto),

        _ => null
    };

    private static TableDesignElement BuildTable(ElementDto dto)
    {
        var tb = new TableDesignElement
        {
            X = dto.X, Y = dto.Y, Width = dto.W, Height = dto.H,
            Opacity = dto.Opacity, IsLocked = dto.IsLocked,
            Rows            = Math.Max(1, dto.Rows),
            Columns         = Math.Max(1, dto.Columns),
            BorderColor     = ParseColor(dto.BorderColor, Colors.Black),
            HeaderBgColor   = ParseColor(dto.HeaderBgColor, Color.FromArgb(255, 220, 230, 245)),
            CellBgColor     = ParseColor(dto.CellBgColor, Colors.White),
            BorderThickness = dto.BorderThick > 0 ? dto.BorderThick : 1
        };
        if (dto.Cells != null)
            tb.Cells = dto.Cells.Select(r => r.ToList()).ToList();
        return tb;
    }

    private static System.Windows.Media.Imaging.BitmapImage? LoadBitmapSafe(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try { return new System.Windows.Media.Imaging.BitmapImage(new Uri(path)); }
        catch { return null; }
    }

    private static string ColorToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color ParseColor(string? hex, Color fallback = default)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        try
        {
            if (hex.StartsWith('#')) hex = hex[1..];
            if (hex.Length == 6)  return Color.FromRgb(H(hex,0), H(hex,2), H(hex,4));
            if (hex.Length == 8)  return Color.FromArgb(H(hex,0), H(hex,2), H(hex,4), H(hex,6));
        }
        catch { }
        return fallback;
    }

    private static byte H(string s, int i) => Convert.ToByte(s.Substring(i, 2), 16);
}
