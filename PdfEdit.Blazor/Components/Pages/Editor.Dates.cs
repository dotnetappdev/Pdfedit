using System.Globalization;
using PdfEdit.Blazor.Components.Editor;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// Dates placed on the page (the Date tool, or any text that reads as a date) can be rewritten like
/// the Windows app's Properties panel: pick a format (dd/MM/yyyy, MM/dd/yyyy, yyyy-MM-dd, 4 October
/// 2026 …) and set the day, month and year separately.
/// </summary>
public partial class Editor
{
    /// <summary>The formats offered, as in the Windows app (MainViewModel.DateText).</summary>
    public static readonly string[] DateFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd", "yyyy/MM/dd",
        "dd-MM-yyyy", "dd.MM.yyyy", "dd/MM/yy", "MM/dd/yy", "M/d/yy", "yy-MM-dd",
        "d MMM yyyy", "dd MMM yyyy", "MMM d, yyyy", "d MMMM yyyy", "MMMM d, yyyy",
        "dddd, d MMMM yyyy", "dddd, MMMM d, yyyy", "ddd, d MMM yyyy",
    ];

    /// <summary>Every format a date may be written in: the Date tool's (Settings) first, then the list.</summary>
    public IEnumerable<string> AllDateFormats => new[] { Settings.DateFormat }.Concat(DateFormats).Distinct();

    // '/' in a .NET date pattern means "the culture's separator" — write exactly what was picked.
    private static string LiteralSlashes(string format) => format.Replace("/", "'/'");

    public static string FormatDate(DateTime d, string format)
    {
        try { return d.ToString(LiteralSlashes(format), CultureInfo.CurrentCulture); }
        catch (FormatException) { return d.ToString("d MMMM yyyy", CultureInfo.CurrentCulture); }
    }

    /// <summary>The date a text item shows and the format it's written in, or null when it isn't a date.</summary>
    public (DateTime Date, string Format)? ReadDate(PageItem? item)
    {
        if (item is not { Kind: ItemKind.Text }) return null;
        string text = item.Text.Trim();
        if (text.Length < 6 || text.Length > 40) return null;
        if (item.DateValue is { } known && item.DateFormat is { Length: > 0 } kf && FormatDate(known, kf) == text)
            return (known, kf);
        foreach (var f in AllDateFormats)
        {
            if (DateTime.TryParseExact(text, LiteralSlashes(f), CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
                || DateTime.TryParseExact(text, LiteralSlashes(f), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out d))
                return (d.Date, f);
        }
        return null;
    }

    /// <summary>Rewrites a date item's text (keeping its format unless one is given).</summary>
    public void SetItemDate(PageItem item, DateTime date, string? format = null)
    {
        if (ReadDate(item) is not { } cur) return;
        string fmt = format ?? cur.Format;
        item.DateValue = date.Date;
        item.DateFormat = fmt;
        item.Text = FormatDate(date, fmt);
        GrowText(item);
    }
}
