using System.Globalization;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>
/// Dates placed on the page (Date stamp, or any text that reads as a date) can be edited in the
/// Properties panel like Windows' date settings: pick a format from a list (dd/MM/yyyy,
/// MM/dd/yyyy, yyyy-MM-dd, 4 October 2026 …) and set the day, month and year separately.
/// The text on the page is rewritten in the chosen format.
/// </summary>
public partial class MainViewModel
{
    /// <summary>One entry in the date-format list: the pattern and how today looks in it.</summary>
    public sealed record DateFormatChoice(string Format, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly string[] BaseDateFormats =
    {
        "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd", "yyyy/MM/dd",
        "dd-MM-yyyy", "dd.MM.yyyy", "dd/MM/yy", "MM/dd/yy", "M/d/yy", "yy-MM-dd",
        "d MMM yyyy", "dd MMM yyyy", "MMM d, yyyy", "d MMMM yyyy", "MMMM d, yyyy",
        "dddd, d MMMM yyyy", "dddd, MMMM d, yyyy", "ddd, d MMM yyyy",
    };

    private IReadOnlyList<DateFormatChoice>? _dateFormatChoices;

    /// <summary>The formats offered: this PC's own short / long date first, then the common ones.</summary>
    public IReadOnlyList<DateFormatChoice> DateFormatChoices => _dateFormatChoices ??= BuildDateFormatChoices();

    // appFormatFirst: when reading a date, try the Date stamp's format (Settings) first, so
    // 04/10/2026 stamped as dd/MM/yyyy is not read back as April 10 on a US-format PC.
    private static List<string> AllDateFormats(bool appFormatFirst = false)
    {
        var dtf = CultureInfo.CurrentCulture.DateTimeFormat;
        var list = new List<string>();
        void Add(string? f) { if (!string.IsNullOrWhiteSpace(f) && !list.Contains(f)) list.Add(f); }
        if (appFormatFirst) Add(AppSettings.Current?.DateFormat);
        Add(dtf.ShortDatePattern);
        Add(dtf.LongDatePattern);
        Add(AppSettings.Current?.DateFormat);
        foreach (var f in BaseDateFormats) Add(f);
        return list;
    }

    private static IReadOnlyList<DateFormatChoice> BuildDateFormatChoices()
    {
        var today = DateTime.Today;
        return AllDateFormats()
            .Select(f => new DateFormatChoice(f, $"{FormatDate(today, f)}   ({f})"))
            .ToList();
    }

    public IReadOnlyList<int> DayChoices { get; } = Enumerable.Range(1, 31).ToList();

    public IReadOnlyList<string> MonthChoices { get; } =
        Enumerable.Range(1, 12).Select(m => CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)).ToList();

    // '/' in a .NET date pattern means "this PC's date separator" — show exactly what was picked.
    private static string LiteralSlashes(string format) => format.Replace("/", "'/'");

    private static string FormatDate(DateTime d, string format) =>
        d.ToString(LiteralSlashes(format), CultureInfo.CurrentCulture);

    /// <summary>The date the annotation's text shows, and the format it is written in.</summary>
    private static (DateTime Date, string Format)? ReadDate(FreeTextAnnotation? ann)
    {
        if (ann == null || MarkShapes.FromGlyph(ann.Text) != null) return null;
        string text = ann.Text.Trim();
        if (text.Length < 6 || text.Length > 40) return null;

        // Remembered by an earlier edit, and the text still says it.
        if (ann.DateValue is { } known && !string.IsNullOrEmpty(ann.DateFormat)
            && FormatDate(known, ann.DateFormat) == text)
            return (known, ann.DateFormat);

        foreach (var f in AllDateFormats(appFormatFirst: true))
        {
            if (DateTime.TryParseExact(text, LiteralSlashes(f), CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
                || DateTime.TryParseExact(text, LiteralSlashes(f), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out d))
                return (d.Date, f);
        }
        return null;
    }

    /// <summary>True when the selected text is a date (shows the Date rows in Properties).</summary>
    public bool IsSelectedAnnotationDate => ReadDate(_selectedAnnotation) != null;

    private void SetSelectedDate(DateTime date, string? format = null)
    {
        if (ReadDate(_selectedAnnotation) is not { } cur) return;
        string fmt = format ?? cur.Format;
        EditSelectedAnnotation(a =>
        {
            a.DateValue = date.Date;
            a.DateFormat = fmt;
            a.Text = FormatDate(date, fmt);
        });
    }

    public string SelectedAnnotationDateFormat
    {
        get => ReadDate(_selectedAnnotation)?.Format ?? string.Empty;
        set
        {
            if (string.IsNullOrEmpty(value) || ReadDate(_selectedAnnotation) is not { } cur || cur.Format == value) return;
            SetSelectedDate(cur.Date, value);
        }
    }

    public int SelectedAnnotationDay
    {
        get => ReadDate(_selectedAnnotation)?.Date.Day ?? 1;
        set
        {
            if (ReadDate(_selectedAnnotation) is not { } cur || cur.Date.Day == value) return;
            int day = Math.Clamp(value, 1, DateTime.DaysInMonth(cur.Date.Year, cur.Date.Month));
            SetSelectedDate(new DateTime(cur.Date.Year, cur.Date.Month, day));
        }
    }

    /// <summary>0-based month (index into <see cref="MonthChoices"/>).</summary>
    public int SelectedAnnotationMonthIndex
    {
        get => (ReadDate(_selectedAnnotation)?.Date.Month ?? 1) - 1;
        set
        {
            if (value < 0 || value > 11 || ReadDate(_selectedAnnotation) is not { } cur || cur.Date.Month == value + 1) return;
            int month = value + 1;
            int day = Math.Min(cur.Date.Day, DateTime.DaysInMonth(cur.Date.Year, month));
            SetSelectedDate(new DateTime(cur.Date.Year, month, day));
        }
    }

    public int SelectedAnnotationYear
    {
        get => ReadDate(_selectedAnnotation)?.Date.Year ?? DateTime.Today.Year;
        set
        {
            if (ReadDate(_selectedAnnotation) is not { } cur || cur.Date.Year == value) return;
            // Two-digit years read as this century (26 → 2026), like Windows.
            int year = value is >= 0 and < 100 ? 2000 + value : value;
            year = Math.Clamp(year, 1, 9999);
            int day = Math.Min(cur.Date.Day, DateTime.DaysInMonth(year, cur.Date.Month));
            SetSelectedDate(new DateTime(year, cur.Date.Month, day));
        }
    }

    private RelayCommand? _setSelectedDateTodayCommand;
    public RelayCommand SetSelectedDateTodayCommand =>
        _setSelectedDateTodayCommand ??= new RelayCommand(() => SetSelectedDate(DateTime.Today));

    private void NotifySelectedDateProperties()
    {
        foreach (var n in new[] { nameof(IsSelectedAnnotationDate), nameof(SelectedAnnotationDateFormat),
                                  nameof(SelectedAnnotationDay), nameof(SelectedAnnotationMonthIndex),
                                  nameof(SelectedAnnotationYear) })
            OnPropertyChanged(n);
    }
}
