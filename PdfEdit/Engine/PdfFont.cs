using System.Windows;
using System.Windows.Media;

namespace PdfEdit.Engine;

/// <summary>
/// Maps PDF font resources to WPF typefaces and handles character encoding.
/// Covers the 14 standard PDF fonts, Windows system fonts, and ToUnicode CMaps.
/// </summary>
internal sealed class PdfFont
{
    public  string       WpfFamilyName  { get; }
    public  bool         IsBold         { get; }
    public  bool         IsItalic       { get; }
    public  GlyphTypeface? GlyphTypeface { get; }
    public  double       FirstChar      { get; }
    public  double[]?    Widths         { get; }   // per-character widths in PDF glyph units (1000 = 1pt)
    private readonly Dictionary<int, char> _toUnicode = new();
    private readonly int[]? _differences;

    // Standard 14 PDF fonts → Windows equivalents
    private static readonly Dictionary<string, (string Family, bool Bold, bool Italic)> StandardFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Helvetica"]             = ("Arial",           false, false),
        ["Helvetica-Bold"]        = ("Arial",           true,  false),
        ["Helvetica-Oblique"]     = ("Arial",           false, true),
        ["Helvetica-BoldOblique"] = ("Arial",           true,  true),
        ["Times-Roman"]           = ("Times New Roman", false, false),
        ["Times-Bold"]            = ("Times New Roman", true,  false),
        ["Times-Italic"]          = ("Times New Roman", false, true),
        ["Times-BoldItalic"]      = ("Times New Roman", true,  true),
        ["Courier"]               = ("Courier New",     false, false),
        ["Courier-Bold"]          = ("Courier New",     true,  false),
        ["Courier-Oblique"]       = ("Courier New",     false, true),
        ["Courier-BoldOblique"]   = ("Courier New",     true,  true),
        ["Symbol"]                = ("Symbol",          false, false),
        ["ZapfDingbats"]          = ("Wingdings",       false, false),
    };

    public static PdfFont FromDictionary(PdfDictionary dict, PdfParser parser)
    {
        string baseFont  = dict.GetName("BaseFont") ?? "Helvetica";
        string encoding  = dict.GetName("Encoding") ?? "WinAnsiEncoding";

        // Strip subset prefix like "ABCDEF+Arial"
        if (baseFont.Length > 7 && baseFont[6] == '+') baseFont = baseFont[7..];

        // Determine WPF family, bold, italic
        (string family, bool bold, bool italic) = ResolveFamily(baseFont, dict);

        // Glyph widths
        long   firstChar = dict.GetInt("FirstChar", 0);
        double[]? widths = null;
        var widthArr = dict.GetArray("Widths");
        if (widthArr != null)
        {
            widths = new double[widthArr.Count];
            for (int i = 0; i < widthArr.Count; i++)
                widths[i] = widthArr[i] is PdfReal r ? r.Value : widthArr[i] is PdfInteger ii ? ii.Value : 0;
        }

        // ToUnicode CMap
        var toUnicodeMap = new Dictionary<int, char>();
        byte[] cmapBytes = parser.DecodeStream(dict.Get("ToUnicode"));
        if (cmapBytes.Length > 0) ParseToUnicode(cmapBytes, toUnicodeMap);

        // Encoding differences
        int[]? differences = null;
        var encObj = parser.ResolveDict(dict.Get("Encoding"));
        if (encObj != null)
        {
            var diffArr = encObj.GetArray("Differences");
            if (diffArr != null) differences = ParseDifferences(diffArr);
        }

        return new PdfFont(family, bold, italic, (int)firstChar, widths, toUnicodeMap, differences);
    }

    private PdfFont(string family, bool bold, bool italic, int firstChar, double[]? widths,
                    Dictionary<int, char> toUnicode, int[]? differences)
    {
        WpfFamilyName = family;
        IsBold        = bold;
        IsItalic      = italic;
        FirstChar     = firstChar;
        Widths        = widths;
        _toUnicode    = toUnicode;
        _differences  = differences;
        GlyphTypeface = ResolveGlyphTypeface(family, bold, italic);
    }

    // ── glyph typeface ────────────────────────────────────────────────────────

    private static GlyphTypeface? ResolveGlyphTypeface(string family, bool bold, bool italic)
    {
        try
        {
            var typeface = new Typeface(
                new FontFamily(family),
                italic ? FontStyles.Italic : FontStyles.Normal,
                bold   ? FontWeights.Bold  : FontWeights.Normal,
                FontStretches.Normal);
            typeface.TryGetGlyphTypeface(out var gt);
            return gt;
        }
        catch { return null; }
    }

    // ── character decoding ────────────────────────────────────────────────────

    /// <summary>Decodes a PDF string byte into a Unicode character for display.</summary>
    public char DecodeChar(int code)
    {
        if (_toUnicode.TryGetValue(code, out char uc)) return uc;
        if (_differences != null && code < _differences.Length && _differences[code] >= 0)
            return (char)_differences[code];
        // WinAnsiEncoding fallback (covers most common PDFs)
        return (char)WinAnsi[code & 0xFF];
    }

    /// <summary>Returns character advance width in PDF glyph units (1000 = 1 point at this size).</summary>
    public double GetCharWidth(int code, double fontSize)
    {
        if (Widths != null)
        {
            int idx = code - (int)FirstChar;
            if (idx >= 0 && idx < Widths.Length)
                return Widths[idx] * fontSize / 1000.0;
        }
        // Estimate from glyph typeface
        if (GlyphTypeface != null)
        {
            char c = DecodeChar(code);
            if (GlyphTypeface.CharacterToGlyphMap.TryGetValue(c, out ushort glyph))
                return GlyphTypeface.AdvanceWidths[glyph] * fontSize;
        }
        return fontSize * 0.5; // fallback
    }

    // ── family resolution ─────────────────────────────────────────────────────

    private static (string Family, bool Bold, bool Italic) ResolveFamily(string baseFont, PdfDictionary dict)
    {
        if (StandardFonts.TryGetValue(baseFont, out var std)) return std;

        // Parse bold/italic from name
        bool bold   = baseFont.Contains("Bold",  StringComparison.OrdinalIgnoreCase);
        bool italic = baseFont.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                      baseFont.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

        // Clean up the name: remove -Bold, -Italic, etc.
        string clean = baseFont
            .Replace("-Bold",    "", StringComparison.OrdinalIgnoreCase)
            .Replace("-Italic",  "", StringComparison.OrdinalIgnoreCase)
            .Replace("-Oblique", "", StringComparison.OrdinalIgnoreCase)
            .Replace(",",        " ")
            .Trim();

        // Try to find a matching installed font
        foreach (var ff in System.Windows.Media.Fonts.SystemFontFamilies)
        {
            string fname = ff.Source;
            if (string.Equals(fname, clean, StringComparison.OrdinalIgnoreCase))
                return (fname, bold, italic);
        }

        return ("Arial", bold, italic); // ultimate fallback
    }

    // ── ToUnicode CMap parser ─────────────────────────────────────────────────

    private static void ParseToUnicode(byte[] data, Dictionary<int, char> map)
    {
        string text = System.Text.Encoding.Latin1.GetString(data);
        // Parse begincidchar/endcidchar blocks: <XX> <YYYY>
        int pos = 0;
        while (pos < text.Length)
        {
            int bcc = text.IndexOf("beginbfchar", pos, StringComparison.Ordinal);
            if (bcc < 0) break;
            int ecc = text.IndexOf("endbfchar", bcc, StringComparison.Ordinal);
            if (ecc < 0) break;
            string block = text[(bcc + 11)..ecc];
            ParseBfBlock(block, map);
            pos = ecc + 9;
        }
        pos = 0;
        while (pos < text.Length)
        {
            int bcc = text.IndexOf("beginbfrange", pos, StringComparison.Ordinal);
            if (bcc < 0) break;
            int ecc = text.IndexOf("endbfrange", bcc, StringComparison.Ordinal);
            if (ecc < 0) break;
            string block = text[(bcc + 12)..ecc];
            ParseBfRangeBlock(block, map);
            pos = ecc + 10;
        }
    }

    private static void ParseBfBlock(string block, Dictionary<int, char> map)
    {
        int pos = 0;
        while (pos < block.Length)
        {
            int lt = block.IndexOf('<', pos); if (lt < 0) break;
            int gt = block.IndexOf('>', lt);  if (gt < 0) break;
            string src = block[(lt + 1)..gt];
            lt = block.IndexOf('<', gt);      if (lt < 0) break;
            gt = block.IndexOf('>', lt);      if (gt < 0) break;
            string dst = block[(lt + 1)..gt];
            if (int.TryParse(src, System.Globalization.NumberStyles.HexNumber, null, out int code) &&
                int.TryParse(dst, System.Globalization.NumberStyles.HexNumber, null, out int uni))
                map[code] = (char)uni;
            pos = gt + 1;
        }
    }

    private static void ParseBfRangeBlock(string block, Dictionary<int, char> map)
    {
        int pos = 0;
        while (pos < block.Length)
        {
            // <srcLo> <srcHi> <dstStart>
            int lt  = block.IndexOf('<', pos); if (lt < 0) break;
            int gt  = block.IndexOf('>', lt);  if (gt < 0) break;
            string lo = block[(lt + 1)..gt];
            lt = block.IndexOf('<', gt);       if (lt < 0) break;
            gt = block.IndexOf('>', lt);       if (gt < 0) break;
            string hi = block[(lt + 1)..gt];
            lt = block.IndexOf('<', gt);       if (lt < 0) break;
            gt = block.IndexOf('>', lt);       if (gt < 0) break;
            string dst = block[(lt + 1)..gt];
            if (int.TryParse(lo,  System.Globalization.NumberStyles.HexNumber, null, out int srcLo) &&
                int.TryParse(hi,  System.Globalization.NumberStyles.HexNumber, null, out int srcHi) &&
                int.TryParse(dst, System.Globalization.NumberStyles.HexNumber, null, out int dstStart))
                for (int c = srcLo; c <= srcHi; c++)
                    map[c] = (char)(dstStart + (c - srcLo));
            pos = gt + 1;
        }
    }

    // ── Differences encoding ──────────────────────────────────────────────────

    private static int[] ParseDifferences(PdfArray arr)
    {
        var result = new int[256];
        for (int i = 0; i < 256; i++) result[i] = -1; // -1 = use default

        int code = 0;
        foreach (var item in arr.Items)
        {
            if (item is PdfInteger pi) { code = (int)pi.Value; continue; }
            if (item is PdfName pn)
            {
                if (code < 256 && GlyphNameToUnicode.TryGetValue(pn.Value, out int uni))
                    result[code] = uni;
                code++;
            }
        }
        return result;
    }

    // ── WinAnsiEncoding table ─────────────────────────────────────────────────
    // CP1252 — covers most business PDFs
    private static readonly int[] WinAnsi = BuildWinAnsi();
    private static int[] BuildWinAnsi()
    {
        var t = new int[256];
        // 0-127: ASCII
        for (int i = 0; i < 128; i++) t[i] = i;
        // 128-159: CP1252 extras
        int[] extras = {
            0x20AC,0x0081,0x201A,0x0192,0x201E,0x2026,0x2020,0x2021,
            0x02C6,0x2030,0x0160,0x2039,0x0152,0x008D,0x017D,0x008F,
            0x0090,0x2018,0x2019,0x201C,0x201D,0x2022,0x2013,0x2014,
            0x02DC,0x2122,0x0161,0x203A,0x0153,0x009D,0x017E,0x0178
        };
        for (int i = 0; i < extras.Length; i++) t[128 + i] = extras[i];
        // 160-255: Latin-1 supplement
        for (int i = 160; i < 256; i++) t[i] = i;
        return t;
    }

    // ── Glyph name → Unicode (subset of Adobe Glyph List) ────────────────────
    private static readonly Dictionary<string, int> GlyphNameToUnicode = new()
    {
        ["space"]       = 0x0020, ["exclam"]    = 0x0021, ["quotedbl"]  = 0x0022,
        ["numbersign"]  = 0x0023, ["dollar"]    = 0x0024, ["percent"]   = 0x0025,
        ["ampersand"]   = 0x0026, ["quotesingle"]= 0x0027,["parenleft"] = 0x0028,
        ["parenright"]  = 0x0029, ["asterisk"]  = 0x002A, ["plus"]      = 0x002B,
        ["comma"]       = 0x002C, ["hyphen"]    = 0x002D, ["period"]    = 0x002E,
        ["slash"]       = 0x002F, ["colon"]     = 0x003A, ["semicolon"] = 0x003B,
        ["less"]        = 0x003C, ["equal"]     = 0x003D, ["greater"]   = 0x003E,
        ["question"]    = 0x003F, ["at"]        = 0x0040, ["bracketleft"]= 0x005B,
        ["backslash"]   = 0x005C, ["bracketright"]=0x005D,["asciicircum"]= 0x005E,
        ["underscore"]  = 0x005F, ["grave"]     = 0x0060, ["braceleft"] = 0x007B,
        ["bar"]         = 0x007C, ["braceright"]= 0x007D, ["asciitilde"]= 0x007E,
        ["endash"]      = 0x2013, ["emdash"]    = 0x2014, ["quoteleft"] = 0x2018,
        ["quoteright"]  = 0x2019, ["quotedblleft"]=0x201C,["quotedblright"]=0x201D,
        ["bullet"]      = 0x2022, ["ellipsis"]  = 0x2026, ["Euro"]      = 0x20AC,
        ["trademark"]   = 0x2122, ["fi"]        = 0xFB01, ["fl"]        = 0xFB02,
    };
}
