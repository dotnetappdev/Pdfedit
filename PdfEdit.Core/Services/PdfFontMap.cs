namespace PdfEdit.Services;

/// <summary>
/// Maps a PDF font name (from a form field's /DA, e.g. "Helv", "HelveticaLTStd-Bold",
/// "ABCDEF+TimesNewRomanPS-ItalicMT") to the Windows font the field should be shown in,
/// so filled-in values look like they do in other PDF readers.
/// </summary>
public static class PdfFontMap
{
    public readonly record struct FieldFont(string Family, bool Bold, bool Italic);

    public static FieldFont Resolve(string? pdfFontName)
    {
        if (string.IsNullOrWhiteSpace(pdfFontName)) return new("Arial", false, false);
        string name = pdfFontName.Trim().TrimStart('/');
        int plus = name.IndexOf('+');
        if (plus == 6) name = name[(plus + 1)..];            // subset prefix "ABCDEF+"

        string lower = name.ToLowerInvariant();
        bool bold = lower.Contains("bold") || lower.Contains("black") || lower.Contains("heavy")
                    || lower.Contains("semibold") || lower.Contains("demi") || lower.EndsWith("-bd") || lower.Contains("-bdit");
        bool italic = lower.Contains("italic") || lower.Contains("oblique") || lower.EndsWith("-it") || lower.Contains("bdit");

        // standard short names and the base-14 families
        string family = lower switch
        {
            _ when lower is "helv" || lower.StartsWith("helvetica") || lower.StartsWith("arial") => "Arial",
            _ when lower is "tiro" || lower.StartsWith("times") => "Times New Roman",
            _ when lower is "cour" || lower.StartsWith("courier") => "Courier New",
            _ when lower is "zadb" || lower.StartsWith("zapfdingbats") => "Segoe UI Symbol",
            _ when lower is "symb" || lower.StartsWith("symbol") => "Symbol",
            _ when lower.StartsWith("myriad") => "Segoe UI",       // Myriad: a common UI font; closest on Windows
            _ when lower.StartsWith("minionpro") => "Times New Roman",
            _ => BaseFamily(name),
        };
        return new(family, bold, italic);
    }

    // "TimesNewRomanPS-BoldMT" → "Times New Roman PS"; "Calibri,Bold" → "Calibri"; "Verdana" → "Verdana"
    private static string BaseFamily(string name)
    {
        string baseName = name.Split('-', ',')[0];
        if (baseName.EndsWith("MT", StringComparison.Ordinal)) baseName = baseName[..^2];
        if (baseName.EndsWith("PS", StringComparison.Ordinal)) baseName = baseName[..^2];
        // Split CamelCase into words: "TimesNewRoman" → "Times New Roman", "SegoeUI" → "Segoe UI"
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < baseName.Length; i++)
        {
            char c = baseName[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(baseName[i - 1])) sb.Append(' ');
            sb.Append(c);
        }
        string family = sb.ToString().Trim();
        return family.Length == 0 ? "Arial" : family;
    }
}
