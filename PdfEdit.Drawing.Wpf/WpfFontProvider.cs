using System.Windows;
using System.Windows.Media;
using PdfEdit.Render.Drawing;

namespace PdfEdit.Drawing.Wpf;

/// <summary>The Windows system fonts, through WPF.</summary>
public sealed class WpfFontProvider : IFontProvider
{
    private static readonly Lazy<string[]> Installed = new(() =>
        Fonts.SystemFontFamilies.Select(f => f.Source).ToArray());

    public string? FindInstalledFamily(string name) =>
        Installed.Value.FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

    public IGlyphFont? GetFont(string family, int weight, bool italic)
    {
        try
        {
            var typeface = new Typeface(new FontFamily(family),
                italic ? FontStyles.Italic : FontStyles.Normal,
                FontWeight.FromOpenTypeWeight(weight),
                FontStretches.Normal);
            return typeface.TryGetGlyphTypeface(out var gt) ? new WpfGlyphFont(family, gt) : null;
        }
        catch { return null; }
    }
}

/// <summary>A WPF GlyphTypeface, for drawing glyph runs.</summary>
public sealed class WpfGlyphFont : IGlyphFont
{
    public WpfGlyphFont(string family, GlyphTypeface typeface) { Family = family; Typeface = typeface; }

    public string Family { get; }
    public GlyphTypeface Typeface { get; }

    public bool TryGetAdvanceWidth(char ch, out double emWidth)
    {
        if (Typeface.CharacterToGlyphMap.TryGetValue(ch, out ushort glyph)) { emWidth = Typeface.AdvanceWidths[glyph]; return true; }
        emWidth = 0;
        return false;
    }
}
