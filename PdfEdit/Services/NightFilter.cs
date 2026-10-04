using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PdfEdit.Services;

/// <summary>
/// Night mode for pages: light becomes dark and dark becomes light, while colours keep their hue
/// (invert, then turn the hue half way round, like the browser filter
/// "invert(1) hue-rotate(180deg)"). White paper becomes a soft dark grey rather than pure black.
/// </summary>
public static class NightFilter
{
    public static BitmapSource Apply(BitmapSource source)
    {
        var src = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
            ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);

        for (int i = 0; i < px.Length; i += 4)
        {
            double b = 255 - px[i], g = 255 - px[i + 1], r = 255 - px[i + 2];
            double nr = -0.574 * r + 1.430 * g + 0.144 * b;
            double ng = 0.426 * r + 0.430 * g + 0.144 * b;
            double nb = 0.426 * r + 1.430 * g - 0.856 * b;
            px[i + 2] = Soften(nr);
            px[i + 1] = Soften(ng);
            px[i] = Soften(nb);
        }

        var result = BitmapSource.Create(w, h, src.DpiX, src.DpiY, src.Format, null, px, stride);
        result.Freeze();
        return result;

        // 0 → 30 (soft black for the paper), 255 → 235 (text not glaring white).
        static byte Soften(double v) => (byte)(30 + Math.Clamp(v, 0, 255) * (205.0 / 255.0));
    }
}
