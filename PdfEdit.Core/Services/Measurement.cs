using System.Globalization;
using PdfEdit.Models;

namespace PdfEdit.Services;

/// <summary>Distance, perimeter and area measurements (Tools → Measure), in points and display units.</summary>
public static class Measurement
{
    public static double PointsPerUnit(string unit) => unit switch
    {
        "mm" => 72.0 / 25.4,
        "cm" => 72.0 / 2.54,
        "pt" => 1.0,
        _    => 72.0,   // in
    };

    public static string FormatLength(double pts, string unit) =>
        (pts / PointsPerUnit(unit)).ToString(unit == "pt" ? "0" : "0.00", CultureInfo.CurrentCulture) + " " + unit;

    public static string FormatArea(double sqPts, string unit)
    {
        double k = PointsPerUnit(unit);
        return (sqPts / (k * k)).ToString(unit == "pt" ? "0" : "0.00", CultureInfo.CurrentCulture) + " sq " + unit;
    }

    public static double Distance(PointD a, PointD b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    public static double PolyLength(IReadOnlyList<PointD> pts, bool closed)
    {
        double len = 0;
        for (int i = 1; i < pts.Count; i++) len += Distance(pts[i - 1], pts[i]);
        if (closed && pts.Count > 2) len += Distance(pts[^1], pts[0]);
        return len;
    }

    public static double PolyArea(IReadOnlyList<PointD> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i]; var q = pts[(i + 1) % pts.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return Math.Abs(a) / 2;
    }

    /// <summary>The label a measurement shows (also written to the saved annotation).</summary>
    public static string Label(ShapeAnnotation s) => s.Kind switch
    {
        ShapeKind.Distance  => FormatLength(Distance(new(s.X1, s.Y1), new(s.X2, s.Y2)), s.MeasureUnit),
        ShapeKind.Perimeter => FormatLength(PolyLength(s.Points ?? new(), closed: false), s.MeasureUnit),
        ShapeKind.Area      => FormatArea(PolyArea(s.Points ?? new()), s.MeasureUnit),
        _ => string.Empty,
    };
}
