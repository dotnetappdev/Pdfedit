using System.Globalization;
using System.Text.RegularExpressions;
using PdfEdit.Models;

namespace PdfEdit.Services;

/// <summary>
/// Number / currency / percent / special formats and simple calculations for form fields, using
/// the same Acrobat scripts (AFNumber_Format, AFPercent_Format, AFSpecial_Format,
/// AFSimple_Calculate) so the PDF behaves the same in Acrobat and other readers.
/// </summary>
public static class FieldFormatting
{
    public static readonly string[] Formats = { "Number", "Currency", "Percent", "Zip", "Zip+4", "Phone", "SSN" };
    public static readonly (string Op, string Label)[] CalcOps =
    {
        ("SUM", "sum (+)"), ("PRD", "product (×)"), ("AVG", "average"), ("MIN", "minimum"), ("MAX", "maximum"),
    };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static bool IsSpecial(string? f) => f is "Zip" or "Zip+4" or "Phone" or "SSN";

    /// <summary>A number typed by the user, ignoring currency signs, spaces and thousands separators.</summary>
    public static double? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        bool percent = text.Contains('%');
        bool negative = text.Contains('(') && text.Contains(')');
        string clean = Regex.Replace(text, @"[^0-9.\-]", "");
        if (!double.TryParse(clean, NumberStyles.Float, Inv, out double n)) return null;
        if (negative && n > 0) n = -n;
        return percent ? n / 100 : n;
    }

    /// <summary>What's stored as the field's value for what the user typed.</summary>
    public static string ToStored(string typed, FormFieldInfo f)
    {
        if (!f.HasNumberFormat || string.IsNullOrWhiteSpace(typed)) return typed;
        if (IsSpecial(f.NumberFormat)) return Regex.Replace(typed, @"\D", "");
        double? n = ParseNumber(typed);
        if (n == null) return typed;
        // In a percent field "15" means 15 % (Acrobat would read it as 1500 %).
        if (f.NumberFormat == "Percent" && !typed.Contains('%') && Math.Abs(n.Value) > 1) n /= 100;
        return n.Value.ToString("0.###############", Inv);
    }

    /// <summary>How a stored value is shown in the field.</summary>
    public static string ToDisplay(string? stored, FormFieldInfo f) =>
        ToDisplay(stored, f.NumberFormat, f.Decimals, f.CurrencySymbol);

    public static string ToDisplay(string? stored, string? format, int decimals, string symbol)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(format)) return stored ?? "";
        if (IsSpecial(format))
        {
            string d = Regex.Replace(stored, @"\D", "");
            return format switch
            {
                "Zip" when d.Length >= 5 => d[..5],
                "Zip+4" when d.Length >= 9 => $"{d[..5]}-{d[5..9]}",
                "Phone" when d.Length == 10 => $"({d[..3]}) {d[3..6]}-{d[6..]}",
                "Phone" when d.Length == 7 => $"{d[..3]}-{d[3..]}",
                "SSN" when d.Length >= 9 => $"{d[..3]}-{d[3..5]}-{d[5..9]}",
                _ => stored,
            };
        }
        double? n = ParseNumber(stored);
        if (n == null) return stored;
        string fmt = "N" + Math.Clamp(decimals, 0, 6);
        return format switch
        {
            "Currency" => (n < 0 ? "-" : "") + symbol + Math.Abs(n.Value).ToString(fmt, Inv),
            "Percent" => (n.Value * 100).ToString(fmt, Inv) + "%",
            _ => n.Value.ToString(fmt, Inv),
        };
    }

    // ── Acrobat scripts ───────────────────────────────────────────────────────
    private static string Js(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static (string Format, string Keystroke)? Scripts(FormFieldInfo f)
    {
        int d = Math.Clamp(f.Decimals, 0, 6);
        return f.NumberFormat switch
        {
            "Number" => ($"AFNumber_Format({d}, 0, 0, 0, \"\", true);", $"AFNumber_Keystroke({d}, 0, 0, 0, \"\", true);"),
            "Currency" => ($"AFNumber_Format({d}, 0, 0, 0, \"{Js(f.CurrencySymbol)}\", true);", $"AFNumber_Keystroke({d}, 0, 0, 0, \"{Js(f.CurrencySymbol)}\", true);"),
            "Percent" => ($"AFPercent_Format({d}, 0);", $"AFPercent_Keystroke({d}, 0);"),
            "Zip" => ("AFSpecial_Format(0);", "AFSpecial_Keystroke(0);"),
            "Zip+4" => ("AFSpecial_Format(1);", "AFSpecial_Keystroke(1);"),
            "Phone" => ("AFSpecial_Format(2);", "AFSpecial_Keystroke(2);"),
            "SSN" => ("AFSpecial_Format(3);", "AFSpecial_Keystroke(3);"),
            _ => null,
        };
    }

    public static string? CalcScript(FormFieldInfo f) =>
        string.IsNullOrEmpty(f.CalcOp) || f.CalcFields.Count == 0 ? null
        : $"AFSimple_Calculate(\"{f.CalcOp}\", new Array ({string.Join(", ", f.CalcFields.Select(n => $"\"{Js(n)}\""))}));";

    private static readonly Regex NumberRx = new(@"AFNumber_(?:Format|Keystroke)\(\s*(\d+)\s*,\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*""([^""]*)""", RegexOptions.Compiled);
    private static readonly Regex PercentRx = new(@"AFPercent_(?:Format|Keystroke)\(\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex SpecialRx = new(@"AFSpecial_(?:Format|Keystroke)\(\s*(\d)", RegexOptions.Compiled);
    private static readonly Regex CalcRx = new(@"AFSimple_Calculate\(\s*[""'](\w+)[""']\s*,\s*(?:new\s+Array\s*\(([^)]*)\)|\[([^\]]*)\])", RegexOptions.Compiled);
    private static readonly Regex NameRx = new(@"[""']([^""']+)[""']", RegexOptions.Compiled);

    /// <summary>Reads the format settings from a field's format / keystroke script.</summary>
    public static void ReadFormat(string js, FormFieldInfo info)
    {
        if (NumberRx.Match(js) is { Success: true } m)
        {
            info.Decimals = int.Parse(m.Groups[1].Value, Inv);
            string sym = m.Groups[2].Value;
            info.NumberFormat = sym.Length > 0 ? "Currency" : "Number";
            if (sym.Length > 0) info.CurrencySymbol = sym;
        }
        else if (PercentRx.Match(js) is { Success: true } p) { info.NumberFormat = "Percent"; info.Decimals = int.Parse(p.Groups[1].Value, Inv); }
        else if (SpecialRx.Match(js) is { Success: true } s)
            info.NumberFormat = s.Groups[1].Value switch { "0" => "Zip", "1" => "Zip+4", "2" => "Phone", "3" => "SSN", _ => null };
    }

    public static void ReadCalc(string js, FormFieldInfo info)
    {
        var m = CalcRx.Match(js);
        if (!m.Success) return;
        info.CalcOp = m.Groups[1].Value.ToUpperInvariant();
        string list = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
        info.CalcFields = NameRx.Matches(list).Select(x => x.Groups[1].Value).ToList();
    }

    public static bool IsOurFormatScript(string js) => js.Contains("AFNumber_") || js.Contains("AFPercent_") || js.Contains("AFSpecial_");

    /// <summary>The result of a calculation over the given values (empty / non-numbers count as 0, except for MIN/MAX/AVG which skip them).</summary>
    public static double Calculate(string op, IEnumerable<string?> values)
    {
        var nums = values.Select(ParseNumber).ToList();
        var present = nums.Where(n => n != null).Select(n => n!.Value).ToList();
        return op switch
        {
            "PRD" => nums.Aggregate(1.0, (a, n) => a * (n ?? 0)),
            "AVG" => present.Count == 0 ? 0 : present.Average(),
            "MIN" => present.Count == 0 ? 0 : present.Min(),
            "MAX" => present.Count == 0 ? 0 : present.Max(),
            _ => present.Sum(),
        };
    }
}
