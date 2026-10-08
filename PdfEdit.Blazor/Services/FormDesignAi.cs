using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PdfEdit.Models;

namespace PdfEdit.Blazor.Services;

/// <summary>A form the AI designed: what's on it, not where (PdfEdit does the layout).</summary>
public sealed class FormSpec
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public List<FormSpecSection> Sections { get; set; } = new();

    [JsonIgnore] public int FieldCount => Sections.Sum(s => s.Fields.Count(f => f.Kind is not ("note" or "heading")));
}

public sealed class FormSpecSection
{
    public string Heading { get; set; } = "";
    public string Note { get; set; } = "";
    public List<FormSpecField> Fields { get; set; } = new();
}

public sealed class FormSpecField
{
    public string Label { get; set; } = "";
    /// <summary>text, email, phone, number, date, memo, checkbox, checkboxes, radio, dropdown, signature, drawing, note.</summary>
    public string Type { get; set; } = "text";
    /// <summary>full, half or third of the row.</summary>
    public string Width { get; set; } = "full";
    public List<string> Options { get; set; } = new();
    public bool Required { get; set; }
    /// <summary>Lines for memo boxes, or the height in lines of a drawing area.</summary>
    public int Lines { get; set; }

    [JsonIgnore] public string Kind => (Type ?? "text").Trim().ToLowerInvariant();
}

/// <summary>
/// "Design a form with AI" (the Windows app's FormDesignAiService): the AI turns a description
/// ("an application form for a dog walking job", "a car damage report") into sections and fields,
/// and <see cref="Layout"/> places real fillable controls (text boxes, check boxes, option groups,
/// drop-downs, signature boxes) with labels, section headings and a title, sized to fit the page.
/// </summary>
/// <remarks>
/// How the caller uses it (as the Windows app does): send <see cref="BuildPrompt"/> with
/// <see cref="SystemPrompt"/> to the AI, <see cref="Parse"/> the reply (null = it couldn't be read),
/// then <see cref="Layout"/> on A4 (595 × 842) or Letter (612 × 792) with one of
/// <see cref="Accents"/>. Replace the whole design with the items: when <c>Grew</c> is false keep
/// the "A4" / "Letter" page size; when it's true the form didn't fit, so set PageSize "Custom" with
/// CustomWidth = the page width and CustomHeight = the returned PageHeight. The new design stands on
/// its own (it isn't tied to the open PDF's page); Export PDF makes the fillable file.
/// </remarks>
public static class FormDesignAi
{
    public const string SystemPrompt =
        "You design clear, practical fillable PDF forms, the kind a well-run business or public office would use. You reply with JSON only.";

    /// <summary>The colours offered for the title, rule and section bands ("#AARRGGBB"); Blue is the default.</summary>
    public static readonly (string Name, string Hex)[] Accents =
    [
        ("Blue", "#FF1E50A0"),
        ("Teal", "#FF0F766E"),
        ("Green", "#FF2E7D32"),
        ("Purple", "#FF5E35B1"),
        ("Red", "#FFB71C1C"),
        ("Orange", "#FFC25E00"),
        ("Charcoal", "#FF37474F"),
    ];

    public static string BuildPrompt(string description) => $$"""
        Design a fillable form for this request:
        "{{description.Trim()}}"

        Reply with JSON only, in exactly this shape:
        {
          "title": "Form title",
          "subtitle": "One short line under the title, e.g. who fills it in or where to send it (may be empty)",
          "sections": [
            {
              "heading": "Section heading",
              "note": "Optional one-sentence instruction for this section (usually empty)",
              "fields": [
                {"label": "Full name", "type": "text", "width": "half", "required": true},
                {"label": "Date of birth", "type": "date", "width": "third"},
                {"label": "Preferred contact", "type": "radio", "options": ["Email", "Phone"], "width": "half"},
                {"label": "Comments", "type": "memo", "lines": 3}
              ]
            }
          ]
        }

        Field types:
        - text, email, phone, number, date: a one-line box
        - memo: a box for a longer answer ("lines": 2 to 6)
        - checkbox: one tick box (e.g. "I agree to the terms")
        - checkboxes: several tick boxes, any number can be ticked ("options" required)
        - radio: pick exactly one ("options" required, 2 to 5 short options)
        - dropdown: pick one from a longer list ("options" required)
        - signature: a box to sign in
        - drawing: an empty area to sketch or mark something on, e.g. "Mark the damage on the vehicle outline" ("lines": 6 to 14)
        - note: a line of instructions or small print, no box (put the text in "label")

        Rules:
        - Include every field the user asked for, plus the ones a form like this normally needs. Don't pad it out.
        - 2 to 6 sections, in the order someone would fill them in; at most 40 fields in all.
        - "width" is the share of the row: "full", "half" or "third". Put short fields side by side
          (names, dates, phone numbers, postcodes) and give addresses and long answers the full width.
        - End with a declaration or sign-off section (signature, printed name, date) when the form needs one.
        - Labels are short (1 to 5 words); no colons. Mark only truly required fields "required": true.
        - Write in the language of the request.
        """;

    /// <summary>Reads the AI's reply (tolerating code fences and stray text around the JSON).</summary>
    public static FormSpec? Parse(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        int start = reply.IndexOf('{'), end = reply.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            var spec = JsonSerializer.Deserialize<FormSpec>(reply[start..(end + 1)], new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
            });
            if (spec == null) return null;
            // Models sometimes send null for an empty list or string.
            spec.Title ??= ""; spec.Subtitle ??= "";
            spec.Sections = (spec.Sections ?? new()).Where(x => x != null).ToList();
            foreach (var sec in spec.Sections)
            {
                sec.Heading ??= ""; sec.Note ??= "";
                sec.Fields = (sec.Fields ?? new()).Where(f => f != null && !string.IsNullOrWhiteSpace(f.Label)).ToList();
                foreach (var f in sec.Fields)
                {
                    f.Type ??= "text"; f.Width ??= "full";
                    f.Options = (f.Options ?? new()).Where(o => !string.IsNullOrWhiteSpace(o)).ToList();
                }
            }
            spec.Sections.RemoveAll(x => x.Fields.Count == 0);
            return spec.Sections.Count > 0 ? spec : null;
        }
        catch (JsonException) { return null; }
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private const double Margin = 40;
    private const string Ink = "#FF222222", Muted = "#FF666666", White = "#FFFFFFFF", Transparent = "#00FFFFFF";

    /// <summary>
    /// Places the form on a page <paramref name="pageWidth"/> × <paramref name="pageHeight"/> points,
    /// in the <paramref name="accentHex"/> colour ("#RRGGBB" or "#AARRGGBB"). Shrinks the spacing and
    /// text a little to fit; if it still doesn't fit, the page is made taller (Grew, with the new
    /// PageHeight; otherwise PageHeight is the one passed in).
    /// </summary>
    public static (List<DesignItem> Items, double PageHeight, bool Grew) Layout(FormSpec spec, double pageWidth, double pageHeight, string accentHex)
    {
        var (a, r, g, b) = DesignColor.Parse(accentHex, (255, 0x1E, 0x50, 0xA0));
        string accent = DesignColor.ToHex(a, r, g, b);
        for (double scale = 1.0; ; scale -= 0.05)
        {
            var (items, bottom) = Place(spec, pageWidth, accent, scale);
            if (bottom <= pageHeight - Margin) return (items, pageHeight, false);
            if (scale <= 0.76) return (items, Math.Ceiling(bottom + Margin), true);
        }
    }

    private static (List<DesignItem> Items, double Bottom) Place(FormSpec spec, double pageW, string accent, double s)
    {
        var els = new List<DesignItem>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double contentW = pageW - Margin * 2, gap = 12, y = Margin;

        // Title, subtitle and a rule
        string title = string.IsNullOrWhiteSpace(spec.Title) ? "Form" : spec.Title.Trim();
        els.Add(Text(Margin, y, contentW, 30 * s, title, 22 * s, accent, bold: true));
        y += 32 * s;
        if (!string.IsNullOrWhiteSpace(spec.Subtitle))
        {
            els.Add(Text(Margin, y, contentW, 16 * s, spec.Subtitle.Trim(), 10 * s, Muted));
            y += 18 * s;
        }
        els.Add(new DesignItem
        {
            Type = "shape", ShapeType = "Line", X = Margin, Y = y + 2, W = contentW, H = 2,
            FillColor = Transparent, StrokeColor = accent, StrokeThick = 2,
        });
        y += 14 * s;

        foreach (var section in spec.Sections)
        {
            // Section heading: a coloured band
            y += 6 * s;
            els.Add(new DesignItem
            {
                Type = "shape", ShapeType = "Rectangle", X = Margin, Y = y, W = contentW, H = 20 * s,
                FillColor = accent, StrokeColor = Transparent, StrokeThick = 0.5,
            });
            els.Add(Text(Margin + 8, y + 2.5 * s, contentW - 16, 16 * s, section.Heading.Trim().ToUpperInvariant(), 10 * s, White, bold: true));
            y += 26 * s;
            if (!string.IsNullOrWhiteSpace(section.Note))
            {
                double h = TextHeight(section.Note, contentW, 9 * s);
                els.Add(Text(Margin, y, contentW, h, section.Note.Trim(), 9 * s, Muted, italic: true));
                y += h + 4 * s;
            }

            // Fields flow left to right in rows of six units (full = 6, half = 3, third = 2).
            var row = new List<(FormSpecField F, int Units)>();
            int used = 0;
            foreach (var f in section.Fields)
            {
                int units = Units(f);
                if (used + units > 6) { y = PlaceRow(row, y); row.Clear(); used = 0; }
                row.Add((f, units)); used += units;
            }
            if (row.Count > 0) y = PlaceRow(row, y);
        }
        return (els, y);

        double PlaceRow(List<(FormSpecField F, int Units)> row, double top)
        {
            double unitW = (contentW - gap * 5) / 6, x = Margin, rowH = 0;
            foreach (var (f, units) in row)
            {
                double w = unitW * units + gap * (units - 1);
                rowH = Math.Max(rowH, PlaceField(f, x, top, w));
                x += w + gap;
            }
            return top + rowH + 10 * s;
        }

        double PlaceField(FormSpecField f, double x, double top, double w)
        {
            string label = f.Label.Trim() + (f.Required ? " *" : "");
            double labelSize = 9.5 * s, labelH = 14 * s, boxH = 20 * s;
            switch (f.Kind)
            {
                case "note":
                {
                    double h = TextHeight(f.Label, w, 9 * s);
                    els.Add(Text(x, top, w, h, f.Label.Trim(), 9 * s, Muted));
                    return h;
                }
                case "checkbox":
                {
                    double box = 12 * s;
                    els.Add(Field("Checkbox", x, top + 2 * s, box, box, f.Label, f.Required));
                    double h = TextHeight(label, w - box - 8, labelSize);
                    els.Add(Text(x + box + 6, top, w - box - 6, Math.Max(h, 16 * s), label, labelSize, Ink));
                    return Math.Max(h, 16 * s);
                }
                case "checkboxes" or "radio":
                {
                    els.Add(Text(x, top, w, labelH, label, labelSize, Ink, bold: true));
                    double box = 11 * s, cx = x, cy = top + labelH + 3 * s, line = 17 * s;
                    var options = f.Options.Count > 0 ? f.Options : new List<string> { "Yes", "No" };
                    string group = UniqueName(f.Label);
                    foreach (var opt in options.Select(o => o.Trim()).Where(o => o.Length > 0))
                    {
                        double optW = box + 6 + EstimateWidth(opt, labelSize) + 14 * s;
                        if (cx > x && cx + optW > x + w) { cx = x; cy += line; }
                        var el = f.Kind == "radio"
                            ? Field("Radio", cx, cy + 1, box, box, opt, f.Required, group)
                            : Field("Checkbox", cx, cy + 1, box, box, opt, false, UniqueName(f.Label + " " + opt));
                        el.ExportValue = opt;
                        els.Add(el);
                        els.Add(Text(cx + box + 5, cy - 1, Math.Min(optW, x + w - cx) - box - 5, 15 * s, opt, labelSize, Ink));
                        cx += optW;
                    }
                    return cy + line - top;
                }
                default:
                {
                    var kind = f.Kind switch
                    {
                        "memo" => "Memo",
                        "dropdown" => "ComboBox",
                        "signature" => "Signature",
                        _ => "Text",
                    };
                    if (kind == "Memo") boxH = Math.Clamp(f.Lines <= 0 ? 3 : f.Lines, 2, 8) * 14 * s + 4;
                    if (kind == "Signature") boxH = 36 * s;
                    if (f.Kind == "drawing") boxH = Math.Clamp(f.Lines <= 0 ? 10 : f.Lines, 4, 20) * 14 * s;

                    els.Add(Text(x, top, w, labelH, label, labelSize, Ink, bold: true));
                    double boxTop = top + labelH + 2 * s;
                    if (f.Kind == "drawing")
                    {
                        // An empty framed area (not a field) to draw or mark on once printed or in Fill & Sign.
                        els.Add(new DesignItem
                        {
                            Type = "shape", ShapeType = "Rectangle", X = x, Y = boxTop, W = w, H = boxH,
                            FillColor = White, StrokeColor = "#FF999999", StrokeThick = 1, CornerRadius = 3,
                        });
                    }
                    else
                    {
                        var el = Field(kind, x, boxTop, w, boxH, f.Label, f.Required);
                        if (kind == "ComboBox")
                            el.OptionsCsv = string.Join(", ", (f.Options.Count > 0 ? f.Options : new List<string> { "Option 1", "Option 2" }).Select(o => o.Replace(",", " ")));
                        els.Add(el);
                    }
                    return labelH + 2 * s + boxH;
                }
            }
        }

        DesignItem Field(string kind, double x, double top, double w, double h, string label, bool required, string? name = null) => new()
        {
            Type = "field", FieldKind = kind, X = x, Y = top, W = w, H = h,
            FieldName = name ?? UniqueName(label),
            Label = label.Trim(),
            LabelPosition = "None", LabelOffset = 6,
            Required = required, Wrap = true, ExportValue = "Yes", Value = "", Alignment = "Left",
        };

        string UniqueName(string label)
        {
            string baseName = Regex.Replace(System.Globalization.CultureInfo.InvariantCulture.TextInfo
                .ToTitleCase(Regex.Replace(label, @"[^\p{L}\p{N}]+", " ").Trim().ToLowerInvariant()), @"\s+", "");
            if (baseName.Length == 0) baseName = "Field";
            if (char.IsDigit(baseName[0])) baseName = "F" + baseName;
            if (baseName.Length > 40) baseName = baseName[..40];
            string name = baseName;
            for (int i = 2; !names.Add(name); i++) name = baseName + i;
            return name;
        }
    }

    private static int Units(FormSpecField f) => f.Kind is "memo" or "drawing" or "note" or "checkboxes" ? 6 :
        (f.Width ?? "").Trim().ToLowerInvariant() switch
        {
            "half" => 3,
            "third" => 2,
            "two-thirds" or "two_thirds" => 4,
            _ => 6,
        };

    private static DesignItem Text(double x, double y, double w, double h, string text, double size, string color, bool bold = false, bool italic = false) => new()
    {
        Type = "text", X = x, Y = y, W = w, H = h, Text = text, FontSize = Math.Max(6, size), FontFamily = "Arial",
        Color = color, BgColor = Transparent, Bold = bold, Italic = italic, Alignment = "Left", Wrap = true,
    };

    private static double EstimateWidth(string text, double size) => text.Length * size * 0.52;

    private static double TextHeight(string text, double width, double size)
    {
        int lines = Math.Max(1, (int)Math.Ceiling(EstimateWidth(text.Trim(), size) / Math.Max(20, width - 4)));
        return lines * size * 1.35 + 2;
    }
}
