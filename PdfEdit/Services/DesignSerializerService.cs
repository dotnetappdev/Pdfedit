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
    // ── Public API ────────────────────────────────────────────────────────────

    public static void Save(
        IEnumerable<DesignElement> elements,
        DesignPageSize pageSize,
        double customW, double customH,
        Color bgColor,
        string path)
    {
        var doc = new DesignDocument
        {
            PageSize      = pageSize.ToString(),
            CustomWidth   = customW,
            CustomHeight  = customH,
            BgColor       = ColorToHex(bgColor),
            Elements      = elements.OrderBy(e => e.ZOrder).Select(ToDto).ToList()
        };
        File.WriteAllText(path, doc.ToJson());
    }

    public static (List<DesignElement> Elements, DesignPageSize PageSize, double CustomW, double CustomH, Color BgColor) Load(string path)
    {
        var doc = DesignDocument.FromJson(File.ReadAllText(path));

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

    // ── File format ──────────────────────────────────────────────────────────
    // The JSON shape lives in PdfEdit.Core (DesignDocument / DesignItem), shared with the web version.

    // ── Serialization helpers ─────────────────────────────────────────────────

    private static DesignItem ToDto(DesignElement e)
    {
        var dto = new DesignItem
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
                dto.Value         = f.Value;
                dto.ExportValue   = f.ExportValue;
                dto.FontSize      = f.FontSizePt;
                dto.Alignment     = f.TextAlign.ToString();
                break;

            case ShapeDesignElement s:
                dto.Type        = "shape";
                dto.ShapeType   = s.ElementType.ToString();
                dto.FillColor   = ColorToHex(s.FillColor);
                dto.StrokeColor = ColorToHex(s.StrokeColor);
                dto.StrokeThick = s.StrokeThickness;
                dto.CornerRadius= s.CornerRadius;
                dto.FlipX       = s.FlipX;
                dto.FlipY       = s.FlipY;
                break;

            case ImageDesignElement im:
                dto.Type     = "image";
                dto.FilePath = im.FilePath;
                if (im.SignatureBytes != null) dto.Signature = Convert.ToBase64String(im.SignatureBytes);
                break;

            case FreehandDesignElement fh:
                dto.Type      = "freehand";
                dto.Color     = ColorToHex(fh.Color);
                dto.Thickness = fh.Thickness;
                dto.Strokes   = fh.Strokes.Select(s => s.Select(p => new DesignPoint { X = p.X, Y = p.Y }).ToList()).ToList();
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

    private static DesignElement? FromDto(DesignItem dto) => dto.Type switch
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
                OptionsCsv    = dto.OptionsCsv ?? "",
                ExportValue   = dto.ExportValue ?? "Yes",
                FontSizePt    = dto.FontSize,
                TextAlign     = Enum.TryParse<System.Windows.TextAlignment>(dto.Alignment, out var fa) ? fa : System.Windows.TextAlignment.Left,
                Value         = dto.Value ?? ""
            },

        "shape" when Enum.TryParse<DesignElementType>(dto.ShapeType, out var st) =>
            new ShapeDesignElement(st)
            {
                X = dto.X, Y = dto.Y, Width = dto.W, Height = dto.H,
                Opacity = dto.Opacity, IsLocked = dto.IsLocked,
                FillColor        = ParseColor(dto.FillColor),
                StrokeColor      = ParseColor(dto.StrokeColor, Colors.Black),
                StrokeThickness  = dto.StrokeThick > 0 ? dto.StrokeThick : 2,
                CornerRadius     = dto.CornerRadius,
                FlipX            = dto.FlipX,
                FlipY            = dto.FlipY
            },

        "image" when dto.Signature != null => LoadSignature(dto),

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

    private static TableDesignElement BuildTable(DesignItem dto)
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

    private static DesignElement LoadSignature(DesignItem dto)
    {
        var png = Convert.FromBase64String(dto.Signature!);
        System.Windows.Media.Imaging.BitmapImage? bmp = null;
        try
        {
            using var ms = new MemoryStream(png);
            bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
        }
        catch { bmp = null; }
        return new ImageDesignElement
        {
            X = dto.X, Y = dto.Y, Width = dto.W, Height = dto.H,
            Opacity = dto.Opacity, IsLocked = dto.IsLocked,
            Bitmap = bmp, SignatureBytes = png,
        };
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
