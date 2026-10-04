namespace PdfEdit.Models;

/// <summary>What a batch step does to each file.</summary>
public enum BatchStepKind
{
    Ocr, Compress, Watermark, Flatten, Rotate, PageNumbers, HeaderFooter, Bates, Sanitize, Password, PdfA, ExportText,
}

/// <summary>One setting of a batch step, shown as a field in the batch window.</summary>
public sealed record BatchParam(string Key, string Label, string Default, string[]? Choices = null, bool IsBool = false, bool Secret = false);

/// <summary>A step in a batch action and its settings (secret ones, like passwords, are never saved).</summary>
public class BatchStep
{
    public BatchStepKind Kind { get; set; }
    public Dictionary<string, string> Settings { get; set; } = new();

    public string Get(string key) =>
        Settings.TryGetValue(key, out var v) ? v : Schema(Kind).FirstOrDefault(p => p.Key == key)?.Default ?? "";
    public bool GetBool(string key) => Get(key).Equals("true", StringComparison.OrdinalIgnoreCase);
    public int GetInt(string key, int fallback) => int.TryParse(Get(key), out var n) ? n : fallback;

    public static BatchStep Create(BatchStepKind kind)
    {
        var s = new BatchStep { Kind = kind };
        foreach (var p in Schema(kind)) s.Settings[p.Key] = p.Default;
        return s;
    }

    public static string Title(BatchStepKind k) => k switch
    {
        BatchStepKind.Ocr => "Recognise text (OCR)",
        BatchStepKind.Compress => "Compress",
        BatchStepKind.Watermark => "Add watermark",
        BatchStepKind.Flatten => "Flatten form fields",
        BatchStepKind.Rotate => "Rotate pages",
        BatchStepKind.PageNumbers => "Add page numbers",
        BatchStepKind.HeaderFooter => "Add header / footer",
        BatchStepKind.Bates => "Add Bates numbers",
        BatchStepKind.Sanitize => "Remove hidden information",
        BatchStepKind.Password => "Password protect",
        BatchStepKind.PdfA => "Save as PDF/A",
        BatchStepKind.ExportText => "Export text (.txt)",
        _ => k.ToString(),
    };

    public string Summary()
    {
        string extra = Kind switch
        {
            BatchStepKind.Watermark => $"“{Get("Text")}”, {Get("Opacity")}%{(GetBool("Behind") ? ", behind" : "")}",
            BatchStepKind.Rotate => $"{Get("Degrees")}°",
            BatchStepKind.PageNumbers => $"{Get("Format")}, {Get("Position")}",
            BatchStepKind.Bates => $"{Get("Prefix")}{new string('0', Math.Max(1, GetInt("Digits", 6) - 1))}{Get("Start")}",
            BatchStepKind.HeaderFooter => string.Join(" / ", new[] { Get("Header"), Get("Footer") }.Where(t => t.Length > 0)),
            _ => "",
        };
        return extra.Length > 0 ? $"{Title(Kind)} — {extra}" : Title(Kind);
    }

    private static readonly string[] Positions6 = { "BottomRight", "BottomCenter", "BottomLeft", "TopRight", "TopCenter", "TopLeft" };

    public static IReadOnlyList<BatchParam> Schema(BatchStepKind k) => k switch
    {
        BatchStepKind.Watermark => new BatchParam[]
        {
            new("Text", "Text", "DRAFT"), new("FontSize", "Font size", "72"), new("Opacity", "Opacity %", "25"),
            new("Color", "Colour", "#808080"), new("Diagonal", "Diagonal", "true", IsBool: true),
            new("Behind", "Behind text and fields", "true", IsBool: true),
        },
        BatchStepKind.Rotate => new BatchParam[] { new("Degrees", "Turn clockwise", "90", new[] { "90", "180", "270" }) },
        BatchStepKind.PageNumbers => new BatchParam[]
        {
            new("Format", "Format", "Page {n} of {total}"), new("Position", "Position", "BottomCenter", new[] { "BottomCenter", "BottomLeft", "BottomRight" }),
        },
        BatchStepKind.HeaderFooter => new BatchParam[]
        {
            new("Header", "Header", ""), new("Footer", "Footer", "{file}"), new("Alignment", "Alignment", "Center", new[] { "Left", "Center", "Right" }),
        },
        BatchStepKind.Bates => new BatchParam[]
        {
            new("Prefix", "Prefix", "ABC"), new("Start", "Start at", "1"), new("Digits", "Digits", "6"),
            new("Position", "Position", "BottomRight", Positions6), new("Continue", "Continue numbering across files", "true", IsBool: true),
        },
        BatchStepKind.Sanitize => new BatchParam[]
        {
            new("Metadata", "Metadata", "true", IsBool: true), new("Scripts", "JavaScript and actions", "true", IsBool: true),
            new("Attachments", "Attached files", "true", IsBool: true), new("Comments", "Comments and markup", "false", IsBool: true),
            new("Bookmarks", "Bookmarks", "false", IsBool: true),
        },
        BatchStepKind.Password => new BatchParam[]
        {
            new("Open", "Password to open", "", Secret: true), new("Owner", "Permissions password", "", Secret: true),
            new("Print", "Allow printing", "true", IsBool: true), new("Copy", "Allow copying", "false", IsBool: true),
        },
        _ => Array.Empty<BatchParam>(),
    };
}

/// <summary>A saved list of steps (Acrobat calls these Actions).</summary>
public class BatchAction
{
    public string Name { get; set; } = "";
    public List<BatchStep> Steps { get; set; } = new();
}
