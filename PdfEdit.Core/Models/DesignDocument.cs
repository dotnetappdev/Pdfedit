using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfEdit.Models;

/// <summary>
/// A design-canvas page as stored in a .pdfdesign file (JSON). Plain values only (colours are
/// "#AARRGGBB" or "#RRGGBB" text, positions in PDF points from the page's top-left corner), so the
/// Windows app and the web version read and write the same files.
/// </summary>
public sealed class DesignDocument
{
    public string PageSize { get; set; } = "A4";        // A4, Letter, A3 or Custom
    public double CustomWidth { get; set; } = 595;
    public double CustomHeight { get; set; } = 842;
    public string? BgColor { get; set; }
    public List<DesignItem>? Elements { get; set; }

    /// <summary>The page size in points.</summary>
    [JsonIgnore]
    public (double Width, double Height) Size => PageSize switch
    {
        "Letter" => (612, 792),
        "A3" => (842, 1191),
        "Custom" => (Math.Max(72, CustomWidth), Math.Max(72, CustomHeight)),
        _ => (595, 842),
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static DesignDocument FromJson(string json) =>
        JsonSerializer.Deserialize<DesignDocument>(json, Options) ?? throw new InvalidDataException("Invalid design file.");
}

/// <summary>
/// One element on a design page. Type is "text", "shape", "image", "freehand", "table" or "field";
/// the other properties used depend on it (the .pdfdesign format).
/// </summary>
public sealed class DesignItem
{
    /// <summary>Identifies the element while it's being edited (not saved).</summary>
    [JsonIgnore] public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];

    public string Type { get; set; } = "";
    // base
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double Opacity { get; set; } = 1.0;
    public bool IsLocked { get; set; }
    // text
    public string? Text { get; set; }
    public string? FontFamily { get; set; }
    public double FontSize { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string? Color { get; set; }
    public string? BgColor { get; set; }
    public string? Alignment { get; set; }      // Left, Center, Right, Justify
    // shape
    public string? ShapeType { get; set; }      // Rectangle, Ellipse, Line, Arrow
    public string? FillColor { get; set; }
    public string? StrokeColor { get; set; }
    public double StrokeThick { get; set; }
    public double CornerRadius { get; set; }
    // image
    public string? FilePath { get; set; }
    // freehand
    public double Thickness { get; set; }
    public List<List<DesignPoint>>? Strokes { get; set; }
    // line / arrow direction
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    // table
    public int Rows { get; set; }
    public int Columns { get; set; }
    public string? BorderColor { get; set; }
    public string? HeaderBgColor { get; set; }
    public string? CellBgColor { get; set; }
    public double BorderThick { get; set; }
    public List<List<string>>? Cells { get; set; }
    // form field
    public string? FieldKind { get; set; }      // Text, Memo, Checkbox, Radio, ComboBox, Signature
    public string? FieldName { get; set; }
    public string? Label { get; set; }
    public string? LabelPosition { get; set; }  // None, Left, Right, Placeholder
    public double LabelOffset { get; set; }
    public bool Required { get; set; }
    public bool Wrap { get; set; } = true;
    public string? OptionsCsv { get; set; }
    public string? Value { get; set; }
    public string? ExportValue { get; set; }
    /// <summary>A picture's PNG/JPEG bytes (base64) — signatures, and pictures added in the web version.</summary>
    public string? Signature { get; set; }

    [JsonIgnore]
    public IReadOnlyList<string> Options =>
        (OptionsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The cell text (empty when the table has no such cell).</summary>
    public string Cell(int row, int col) =>
        Cells != null && row < Cells.Count && col < Cells[row].Count ? Cells[row][col] : "";

    /// <summary>Makes the cell grid match Rows × Columns, keeping what's there.</summary>
    public void FitCells()
    {
        Cells ??= new();
        while (Cells.Count < Rows) Cells.Add(new());
        if (Cells.Count > Rows) Cells.RemoveRange(Rows, Cells.Count - Rows);
        foreach (var row in Cells)
        {
            while (row.Count < Columns) row.Add("");
            if (row.Count > Columns) row.RemoveRange(Columns, row.Count - Columns);
        }
    }

    public DesignItem Clone()
    {
        var json = JsonSerializer.Serialize(this);
        var copy = JsonSerializer.Deserialize<DesignItem>(json)!;
        return copy;
    }
}

public sealed class DesignPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>"#AARRGGBB" / "#RRGGBB" colours used by design files.</summary>
public static class DesignColor
{
    /// <summary>(alpha, red, green, blue), or the fallback when the text isn't a colour.</summary>
    public static (byte A, byte R, byte G, byte B) Parse(string? hex, (byte A, byte R, byte G, byte B) fallback = default)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        try
        {
            var h = hex.TrimStart('#');
            byte P(int i) => Convert.ToByte(h.Substring(i, 2), 16);
            if (h.Length == 6) return (255, P(0), P(2), P(4));
            if (h.Length == 8) return (P(0), P(2), P(4), P(6));
        }
        catch { }
        return fallback;
    }

    public static string ToHex(byte a, byte r, byte g, byte b) => $"#{a:X2}{r:X2}{g:X2}{b:X2}";

    /// <summary>CSS colour for the web: rgba(r,g,b,a).</summary>
    public static string ToCss(string? hex, string fallback = "transparent")
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        var (a, r, g, b) = Parse(hex, (0, 0, 0, 0));
        return $"rgba({r},{g},{b},{(a / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})";
    }

    /// <summary>"#RRGGBB" for an HTML colour picker (the alpha is dropped).</summary>
    public static string ToRgb(string? hex, string fallback = "#000000")
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        var (_, r, g, b) = Parse(hex, (255, 0, 0, 0));
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    public static bool IsVisible(string? hex) => !string.IsNullOrEmpty(hex) && Parse(hex).A > 0;
}
