using System.Windows;
using PdfEdit.Models;

namespace PdfEdit.Services;

/// <summary>
/// Applies the per-area interface font sizes to live application resources.
/// Styles reference these keys via DynamicResource, so updating a resource
/// re-flows the affected part of the UI immediately.
/// </summary>
public static class FontService
{
    /// <summary>Effective size for an area: the user's saved value, or its default.</summary>
    public static double GetSize(InterfaceFontArea area)
    {
        var sizes = AppSettings.Current?.InterfaceFontSizes;
        if (sizes != null && sizes.TryGetValue(area.Key, out var size) && size > 0)
            return Math.Clamp(size, InterfaceFonts.MinSize, InterfaceFonts.MaxSize);
        return area.DefaultSize;
    }

    /// <summary>Sets and persists the size for a single area, applying it live.</summary>
    public static void SetSize(InterfaceFontArea area, double size)
    {
        size = Math.Clamp(size, InterfaceFonts.MinSize, InterfaceFonts.MaxSize);
        var settings = AppSettings.Current;
        settings.InterfaceFontSizes[area.Key] = size;
        ApplyArea(area, size);
    }

    /// <summary>Pushes every area's current size into application resources.</summary>
    public static void ApplyAll()
    {
        foreach (var area in InterfaceFonts.Areas)
            ApplyArea(area, GetSize(area));
    }

    /// <summary>Resets every area to its default size.</summary>
    public static void ResetAll()
    {
        AppSettings.Current.InterfaceFontSizes.Clear();
        ApplyAll();
    }

    private static void ApplyArea(InterfaceFontArea area, double size)
    {
        var app = Application.Current;
        if (app == null) return;
        app.Resources[area.ResourceKey] = size;
    }
}
