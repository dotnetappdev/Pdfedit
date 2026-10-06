using System.Windows;
using PdfEdit.Models;

namespace PdfEdit;

/// <summary>Converts between PdfEdit.Core's UI-free types and their WPF equivalents.</summary>
internal static class CoreInterop
{
    public static Point ToWpf(this PointD p) => new(p.X, p.Y);
    public static PointD ToCore(this Point p) => new(p.X, p.Y);

    // TextAlign has the same members in the same order as WPF's TextAlignment.
    public static TextAlignment ToWpf(this TextAlign a) => (TextAlignment)(int)a;
    public static TextAlign ToCore(this TextAlignment a) => (TextAlign)(int)a;
}
