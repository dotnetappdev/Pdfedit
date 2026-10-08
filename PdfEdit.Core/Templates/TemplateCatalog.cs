using PdfEdit.Models;

namespace PdfEdit.Templates;

/// <summary>
/// A ready-made page to start from: built as a Design-canvas page (<see cref="DesignDocument"/>), so it
/// can be customised on the canvas or turned straight into a PDF — with real, fillable form fields where
/// the template has them. Shared by the Windows app and the web version.
/// </summary>
public sealed record PdfTemplate(string Id, string Title, string Category, string Description, Func<DesignDocument> Build)
{
    /// <summary>Extra words the chooser's search matches.</summary>
    public string[] Tags { get; init; } = [];
    /// <summary>Has form fields to fill in.</summary>
    public bool Fillable { get; init; }

    public DesignDocument Create() => Build();
}

/// <summary>
/// Every template, grouped by category like Adobe's and Office's template galleries: business documents,
/// forms and agreements, letters, resumes, marketing and events, certificates and cards, education and
/// planning. Each template is a one-page design.
/// </summary>
public static partial class TemplateCatalog
{
    public const string Business = "Business", Forms = "Forms", Agreements = "Agreements", Letters = "Letters & office",
        Resumes = "Resumes", Marketing = "Marketing & events", Cards = "Certificates & cards",
        Education = "Education", Planning = "Planning & personal";

    /// <summary>The categories in the order the chooser shows them.</summary>
    public static IReadOnlyList<string> Categories { get; } =
        [Business, Forms, Agreements, Letters, Resumes, Marketing, Cards, Education, Planning];

    private static List<PdfTemplate>? _all;

    public static IReadOnlyList<PdfTemplate> All => _all ??= [.. BusinessTemplates(), .. FormTemplates(), .. AgreementTemplates(),
        .. LetterTemplates(), .. ResumeTemplates(), .. MarketingTemplates(), .. CardTemplates(), .. EducationTemplates(),
        .. PlanningTemplates()];

    public static PdfTemplate? Find(string id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>A new design laid out as the template (an empty A4 page for an unknown id).</summary>
    public static DesignDocument Create(string id) =>
        Find(id)?.Create() ?? new DesignDocument { PageSize = "A4", BgColor = Kit.White, Elements = new() };

    /// <summary>Templates in a category (all of them for null), filtered by words in the title, description or tags.</summary>
    public static IEnumerable<PdfTemplate> Search(string? category, string? query)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return All.Where(t => (category == null || t.Category == category)
            && words.All(w => t.Title.Contains(w, StringComparison.OrdinalIgnoreCase)
                           || t.Description.Contains(w, StringComparison.OrdinalIgnoreCase)
                           || t.Category.Contains(w, StringComparison.OrdinalIgnoreCase)
                           || t.Tags.Any(g => g.Contains(w, StringComparison.OrdinalIgnoreCase))));
    }

    private static PdfTemplate T(string id, string title, string category, string description, Action<Kit> build,
        string size = "A4", bool landscape = false, string[]? tags = null, bool fillable = false) =>
        new(id, title, category, description, () => Kit.Build(size, landscape, build)) { Tags = tags ?? [], Fillable = fillable };
}

/// <summary>
/// Lays out a template page: text, boxes, rules, form fields and grids, in points from the top-left.
/// </summary>
public sealed class Kit
{
    public const string Black = "#FF000000", White = "#FFFFFFFF", Clear = "#00FFFFFF";
    public const string Ink = "#FF1F2937", Body = "#FF374151", Muted = "#FF6B7280", Faint = "#FF9CA3AF",
        Rule = "#FFD1D5DB", Fill = "#FFF3F4F6", FieldFill = "#FFF8FAFC";

    public DesignDocument Doc { get; }
    public double W { get; }
    public double H { get; }
    /// <summary>The page margin most templates use.</summary>
    public double M { get; set; } = 48;
    /// <summary>The template's main colour (headings, bars).</summary>
    public string Accent { get; set; } = "#FF1E4E8C";
    public string Font { get; set; } = "Segoe UI";

    private Kit(DesignDocument doc)
    {
        Doc = doc;
        (W, H) = doc.Size;
    }

    public static DesignDocument Build(string size, bool landscape, Action<Kit> build)
    {
        var doc = new DesignDocument { PageSize = size, BgColor = White, Elements = new() };
        if (landscape)
        {
            var (w, h) = doc.Size;
            doc.PageSize = "Custom";
            (doc.CustomWidth, doc.CustomHeight) = (h, w);
        }
        build(new Kit(doc));
        return doc;
    }

    /// <summary>A custom page size (cards, tickets, badges).</summary>
    public static DesignDocument Custom(double w, double h, Action<Kit> build)
    {
        var doc = new DesignDocument { PageSize = "Custom", CustomWidth = w, CustomHeight = h, BgColor = White, Elements = new() };
        build(new Kit(doc));
        return doc;
    }

    public static string Rgb(byte r, byte g, byte b) => DesignColor.ToHex(255, r, g, b);
    public static string Argb(byte a, byte r, byte g, byte b) => DesignColor.ToHex(a, r, g, b);
    /// <summary>The colour with a new alpha (0–255).</summary>
    public static string Alpha(string hex, byte a) { var c = DesignColor.Parse(hex); return DesignColor.ToHex(a, c.R, c.G, c.B); }
    /// <summary>The colour mixed toward white (0 = as is, 1 = white): light tints for fills.</summary>
    public static string Tint(string hex, double toWhite)
    {
        var c = DesignColor.Parse(hex);
        byte Mix(byte v) => (byte)Math.Round(v + (255 - v) * toWhite);
        return DesignColor.ToHex(255, Mix(c.R), Mix(c.G), Mix(c.B));
    }

    private DesignItem Add(DesignItem item) { Doc.Elements!.Add(item); return item; }

    public void Background(string color) => Doc.BgColor = color;

    // ── Text ─────────────────────────────────────────────────────────────────

    public DesignItem Text(double x, double y, double w, double h, string text, double size, string? color = null,
        bool bold = false, bool italic = false, string align = "Left", bool underline = false) => Add(new DesignItem
    {
        Type = "text", X = x, Y = y, W = w, H = h, Text = text, FontFamily = Font, FontSize = Math.Max(5, size),
        Bold = bold, Italic = italic, Underline = underline, Color = color ?? Ink, BgColor = Clear, Alignment = align, Wrap = true,
    });

    /// <summary>A paragraph: its box is tall enough for the lines it'll wrap to (roughly).</summary>
    public DesignItem Para(double x, double y, double w, string text, double size = 10, string? color = null, string align = "Left",
        bool italic = false)
    {
        double charsPerLine = Math.Max(10, w / (size * 0.5));
        int lines = text.Split('\n').Sum(l => Math.Max(1, (int)Math.Ceiling(l.Length / charsPerLine)));
        return Text(x, y, w, lines * size * 1.4 + 4, text, size, color ?? Body, align: align, italic: italic);
    }

    /// <summary>A small capitals label (field captions, section labels).</summary>
    public DesignItem Label(double x, double y, double w, string text, string? color = null, double size = 7.5) =>
        Text(x, y, w, size * 1.6, text.ToUpperInvariant(), size, color ?? Muted, bold: true);

    // ── Shapes ───────────────────────────────────────────────────────────────

    public DesignItem Box(double x, double y, double w, double h, string fill, string? stroke = null, double thick = 1, double radius = 0) =>
        Shape("Rectangle", x, y, w, h, fill, stroke, thick, radius);

    public DesignItem Circle(double x, double y, double w, double h, string fill, string? stroke = null, double thick = 1) =>
        Shape("Ellipse", x, y, w, h, fill, stroke, thick, 0);

    // A see-through fill becomes the shape's opacity: PDF fills are drawn without the colour's alpha.
    private DesignItem Shape(string type, double x, double y, double w, double h, string fill, string? stroke, double thick, double radius)
    {
        var f = DesignColor.Parse(fill);
        var s = DesignColor.Parse(stroke ?? Clear);
        double opacity = 1;
        if (f.A is > 0 and < 255 && s.A == 0)
        {
            opacity = f.A / 255.0;
            fill = DesignColor.ToHex(255, f.R, f.G, f.B);
        }
        var item = new DesignItem
        {
            Type = "shape", ShapeType = type, X = x, Y = y, W = w, H = h, Opacity = opacity,
            FillColor = fill, StrokeColor = stroke ?? Clear, StrokeThick = Math.Max(0.5, thick), CornerRadius = radius,
        };
        // Nothing to draw (no fill, no outline): leave it out.
        return f.A == 0 && s.A == 0 ? item : Add(item);
    }

    /// <summary>A horizontal rule (a thin filled box, so it's straight however it's resized).</summary>
    public DesignItem HRule(double x, double y, double w, string? color = null, double thick = 0.75) => Box(x, y, w, thick, color ?? Rule);

    public DesignItem VRule(double x, double y, double h, string? color = null, double thick = 0.75) => Box(x, y, thick, h, color ?? Rule);

    // ── Form fields ──────────────────────────────────────────────────────────

    public DesignItem Field(string kind, string name, double x, double y, double w, double h, string? label = null,
        string labelPos = "Placeholder", string? options = null, bool required = false, double fontSize = 10, string? value = null) =>
        Add(new DesignItem
        {
            Type = "field", FieldKind = kind, FieldName = name, X = x, Y = y, W = w, H = h,
            Label = label ?? name, LabelPosition = labelPos, LabelOffset = 6, Required = required, OptionsCsv = options,
            FontSize = fontSize, Value = value, Color = Ink, BgColor = Clear, StrokeColor = Rule,
        });

    /// <summary>A caption above a boxed fill-in field (Adobe's form style).</summary>
    public void Input(string label, string name, double x, double y, double w, double h = 20, bool required = false, string kind = "Text",
        string? options = null)
    {
        Label(x, y, w, label + (required ? " *" : ""));
        Box(x, y + 12, w, h, FieldFill, Rule, 0.75, 2);
        Field(kind, name, x + 2, y + 13, w - 4, h - 2, label, options: options, required: required);
    }

    /// <summary>A caption beside a fill-in line (letter-style forms).</summary>
    public void LineInput(string label, string name, double x, double y, double w, double labelW = 0, string kind = "Text")
    {
        double lw = labelW > 0 ? labelW : Math.Min(w * 0.45, label.Length * 5.2 + 8);
        Text(x, y + 3, lw, 14, label, 9.5, Body);
        HRule(x + lw, y + 17, w - lw, Faint, 0.75);
        Field(kind, name, x + lw + 2, y + 1, w - lw - 4, 16, label);
    }

    /// <summary>A tick box with its caption to the right.</summary>
    public void Check(string label, string name, double x, double y, double w = 160, double size = 11)
    {
        Box(x, y, size, size, White, Faint, 0.75, 1.5);
        Field("Checkbox", name, x, y, size, size, label, labelPos: "None");
        if (label.Length > 0) Text(x + size + 5, y - 1, w - size - 5, size + 4, label, 9.5, Body);
    }

    /// <summary>A signature box with a caption under its line.</summary>
    public void SignatureLine(string label, string name, double x, double y, double w, double h = 34)
    {
        Field("Signature", name, x, y, w, h, label, labelPos: "None");
        HRule(x, y + h, w, Ink, 0.75);
        Text(x, y + h + 3, w, 12, label, 8, Muted);
    }

    // ── Blocks ───────────────────────────────────────────────────────────────

    /// <summary>A coloured section bar with white text.</summary>
    public void SectionBar(double x, double y, double w, string text, string? color = null, double h = 20)
    {
        Box(x, y, w, h, color ?? Accent);
        Text(x + 8, y + (h - 12) / 2 - 1, w - 16, 14, text.ToUpperInvariant(), 9, White, bold: true);
    }

    /// <summary>A heading with a short accent rule under it.</summary>
    public void Heading(double x, double y, double w, string text, double size = 12, string? color = null)
    {
        Text(x, y, w, size * 1.5, text, size, color ?? Accent, bold: true);
        HRule(x, y + size * 1.5 + 2, w, Rule, 0.75);
    }

    /// <summary>
    /// A ruled grid: a header row on a fill, then rows; each column has a width (they're scaled to fill
    /// <paramref name="w"/>). With a field prefix every body cell is a fill-in field (prefix_row_col).
    /// Returns the y below the grid.
    /// </summary>
    public double Grid(double x, double y, double w, string[] headers, double[] widths, int rows, double rowH = 20,
        string? fieldPrefix = null, string[][]? cells = null, string? headerFill = null, string? headerText = null, bool zebra = true,
        string[]? align = null)
    {
        double total = widths.Sum();
        var cw = widths.Select(c => c / total * w).ToArray();
        double hh = 22;
        Box(x, y, w, hh, headerFill ?? Accent);
        double cx = x;
        for (int c = 0; c < headers.Length; c++)
        {
            Text(cx + 6, y + 5, cw[c] - 12, 13, headers[c], 8.5, headerText ?? White, bold: true, align: align?[c] ?? "Left");
            cx += cw[c];
        }
        for (int r = 0; r < rows; r++)
        {
            double ry = y + hh + r * rowH;
            if (zebra && r % 2 == 1) Box(x, ry, w, rowH, Fill);
            HRule(x, ry + rowH, w, Rule, 0.5);
            cx = x;
            for (int c = 0; c < headers.Length; c++)
            {
                string? text = cells != null && r < cells.Length && c < cells[r].Length ? cells[r][c] : null;
                if (!string.IsNullOrEmpty(text)) Text(cx + 6, ry + (rowH - 13) / 2, cw[c] - 12, 13, text, 9, Body, align: align?[c] ?? "Left");
                else if (fieldPrefix != null)
                    Field("Text", $"{fieldPrefix}_{r + 1}_{c + 1}", cx + 3, ry + 2, cw[c] - 6, rowH - 4, $"{headers[c]} {r + 1}", fontSize: 9);
                cx += cw[c];
            }
        }
        // Column lines.
        cx = x;
        for (int c = 0; c < headers.Length - 1; c++) { cx += cw[c]; VRule(cx, y + hh, rows * rowH, Rule, 0.5); }
        Box(x, y, w, hh + rows * rowH, Clear, Rule, 0.75);
        return y + hh + rows * rowH;
    }

    /// <summary>Totals at the bottom right of a line-item grid (Subtotal / Tax / Total), as fields.</summary>
    public double Totals(double right, double y, string prefix, params string[] labels)
    {
        double lw = 110, vw = 110, x = right - lw - vw;
        for (int i = 0; i < labels.Length; i++)
        {
            bool last = i == labels.Length - 1;
            double ry = y + i * 22;
            if (last) Box(x, ry, lw + vw, 22, Tint(Accent, 0.88));
            Text(x + 8, ry + 5, lw - 8, 14, labels[i], last ? 10 : 9.5, last ? Accent : Body, bold: last);
            Field("Text", $"{prefix}_{labels[i].Replace(" ", "").Replace("%", "")}", x + lw, ry + 3, vw - 6, 16, labels[i], fontSize: last ? 11 : 10);
            if (!last) HRule(x, ry + 22, lw + vw, Rule, 0.5);
        }
        return y + labels.Length * 22;
    }

    /// <summary>Evenly spaced writing lines.</summary>
    public void Lines(double x, double y, double w, int count, double gap = 22, string? color = null)
    {
        for (int i = 0; i < count; i++) HRule(x, y + (i + 1) * gap, w, color ?? Rule, 0.6);
    }
}
