namespace PdfEdit.Services;

/// <summary>
/// Acrobat's visual Compare Files, the pixel part: given two grey-scale page renders, marks what
/// changed — red for ink only in the old version, green for ink only in the new one, orange where
/// both have ink that differs — and fades unchanged content. Shared by the Windows app and the web.
/// </summary>
public static class VisualDiff
{
    private const int InkThreshold = 200, DiffThreshold = 48;

    /// <summary>Grey levels (0 black – 255 white) from BGRA pixels, padded with white to <paramref name="w"/> × <paramref name="h"/>.</summary>
    public static byte[] GrayFromBgra(byte[]? bgra, int srcW, int srcH, int w, int h)
    {
        var buf = Enumerable.Repeat((byte)255, w * h).ToArray();
        if (bgra == null) return buf;
        for (int y = 0; y < Math.Min(h, srcH); y++)
            for (int x = 0; x < Math.Min(w, srcW); x++)
            {
                int s = (y * srcW + x) * 4;
                if (s + 2 >= bgra.Length) break;
                buf[y * w + x] = (byte)((bgra[s] * 29 + bgra[s + 1] * 150 + bgra[s + 2] * 77) >> 8);
            }
        return buf;
    }

    /// <summary>The difference image (BGRA, <paramref name="w"/> × <paramref name="h"/>) and the share of inked pixels that changed (%).</summary>
    public static (byte[] Bgra, double ChangedPercent) Diff(byte[] grayOld, byte[] grayNew, int w, int h)
    {
        var px = new byte[w * h * 4];
        int changed = 0, ink = 0;
        for (int i = 0; i < w * h; i++)
        {
            byte va = grayOld[i], vb = grayNew[i];
            bool inkA = va < InkThreshold, inkB = vb < InkThreshold;
            if (inkA || inkB) ink++;
            int o = i * 4;
            if (Math.Abs(va - vb) > DiffThreshold && (inkA || inkB))
            {
                changed++;
                if (inkA && !inkB) { px[o] = 0x3B; px[o + 1] = 0x3B; px[o + 2] = 0xE5; }       // removed: red (BGR)
                else if (inkB && !inkA) { px[o] = 0x4E; px[o + 1] = 0xB0; px[o + 2] = 0x2E; }  // added: green
                else { px[o] = 0x00; px[o + 1] = 0x8C; px[o + 2] = 0xE6; }                       // changed: orange
                px[o + 3] = 255;
            }
            else
            {
                byte faded = (byte)(255 - (255 - vb) * 0.25);   // unchanged content, faded
                px[o] = px[o + 1] = px[o + 2] = faded;
                px[o + 3] = 255;
            }
        }
        double pct = ink == 0 ? 0 : 100.0 * changed / ink;
        return (px, Math.Round(pct, 1));
    }
}
