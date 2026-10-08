namespace PdfEdit.Services;

/// <summary>
/// Turns a photo or scan of a handwritten signature into a clean signature: the paper becomes
/// see-through (with soft edges, so the ink isn't jagged), the ink keeps its colour, and the picture
/// is cropped to the signature. Works on 32-bit BGRA pixels so each app can use its own decoder.
/// The web version does the same steps in the browser (pdfedit.js, sigCleanup).
/// </summary>
public static class SignatureCleanup
{
    /// <summary>
    /// Makes the paper in <paramref name="bgra"/> transparent, in place, and returns the area holding
    /// the ink (with a small margin), or null when no ink was found.
    /// </summary>
    public static (int X, int Y, int Width, int Height)? RemovePaper(byte[] bgra, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0) return null;
        var lum = new byte[width * height];
        var histogram = new int[256];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * stride + x * 4;
            int l = (bgra[i + 2] * 299 + bgra[i + 1] * 587 + bgra[i] * 114) / 1000;
            lum[y * width + x] = (byte)l;
            histogram[l]++;
        }
        // The paper is the bright majority: its level is taken well up the histogram, the ink well down.
        int paper = Percentile(histogram, width * height, 0.75);
        int ink = Percentile(histogram, width * height, 0.005);
        if (paper - ink < 40) return null;   // no contrast: nothing that looks like ink on paper
        // Pixels near the paper's brightness vanish; the fade ends a little above the ink's level.
        double clear = paper - (paper - ink) * 0.18, solid = ink + (paper - ink) * 0.35;

        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * stride + x * 4;
            double l = lum[y * width + x];
            double a = l >= clear ? 0 : l <= solid ? 1 : (clear - l) / (clear - solid);
            byte alpha = (byte)Math.Round(a * bgra[i + 3]);
            bgra[i + 3] = alpha;
            if (alpha > 0)
            {
                // Deepen the ink a little: phone photos make it greyish.
                bgra[i] = (byte)(bgra[i] * 0.8);
                bgra[i + 1] = (byte)(bgra[i + 1] * 0.8);
                bgra[i + 2] = (byte)(bgra[i + 2] * 0.8);
            }
            else { bgra[i] = bgra[i + 1] = bgra[i + 2] = 0; }
            if (alpha > 96)
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0) return null;
        int margin = Math.Max(4, Math.Min(width, height) / 50);
        minX = Math.Max(0, minX - margin); minY = Math.Max(0, minY - margin);
        maxX = Math.Min(width - 1, maxX + margin); maxY = Math.Min(height - 1, maxY + margin);
        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static int Percentile(int[] histogram, int total, double fraction)
    {
        long target = (long)(total * fraction), seen = 0;
        for (int v = 0; v < 256; v++)
        {
            seen += histogram[v];
            if (seen > target) return v;
        }
        return 255;
    }
}
