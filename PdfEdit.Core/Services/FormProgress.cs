using PdfEdit.Models;

namespace PdfEdit.Services;

/// <summary>
/// How far through a form you are: "7 of 12 filled · 2 required left", and which empty field to go to
/// next. Text boxes, drop-downs, lists and radio groups count; checkboxes don't (unticked is a fine
/// answer), nor do signatures and buttons.
/// </summary>
public sealed record FormProgress(int Filled, int Total, int RequiredLeft)
{
    public bool IsForm => Total > 0;
    public bool Done => Filled == Total;

    /// <summary>"7 of 12 filled · 2 required left".</summary>
    public string Label => RequiredLeft > 0
        ? $"{Filled} of {Total} filled · {RequiredLeft} required left"
        : $"{Filled} of {Total} filled";

    private static bool Counts(FormFieldInfo f) =>
        (f.FieldType is FieldType.Text or FieldType.ComboBox or FieldType.ListBox or FieldType.RadioButton) && !f.IsReadOnly;

    private static bool IsEmpty(string? value) => string.IsNullOrWhiteSpace(value) || value == "Off";

    /// <summary>One entry per field (a field shown in several places counts once), in page order.</summary>
    private static List<FormFieldInfo> Fields(IEnumerable<FormFieldInfo> widgets) =>
        widgets.Where(Counts).GroupBy(f => f.Name).Select(g => g.First())
               .OrderBy(f => f.PageNumber).ThenByDescending(f => f.Bottom + f.Height).ThenBy(f => f.Left).ToList();

    public static FormProgress Of(IEnumerable<FormFieldInfo> widgets, Func<string, string?> valueOf)
    {
        var fields = Fields(widgets);
        int filled = fields.Count(f => !IsEmpty(valueOf(f.Name)));
        int requiredLeft = fields.Count(f => f.IsRequired && IsEmpty(valueOf(f.Name)));
        return new FormProgress(filled, fields.Count, requiredLeft);
    }

    /// <summary>
    /// The next empty field after <paramref name="after"/> (wrapping round): an empty required field
    /// first, otherwise any empty one. Null when everything's filled.
    /// </summary>
    public static FormFieldInfo? NextEmpty(IEnumerable<FormFieldInfo> widgets, Func<string, string?> valueOf, string? after)
    {
        var fields = Fields(widgets);
        int start = after == null ? -1 : fields.FindIndex(f => f.Name == after);
        var order = fields.Skip(start + 1).Concat(fields.Take(start + 1)).ToList();
        return order.FirstOrDefault(f => f.IsRequired && IsEmpty(valueOf(f.Name)))
            ?? order.FirstOrDefault(f => IsEmpty(valueOf(f.Name)));
    }
}
