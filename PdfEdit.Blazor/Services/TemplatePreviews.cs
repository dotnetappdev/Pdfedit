using System.Collections.Concurrent;
using PdfEdit.Render;
using PdfEdit.Services;
using PdfEdit.Templates;

namespace PdfEdit.Blazor.Services;

/// <summary>
/// Thumbnails for the template chooser: each template made into its PDF and its page drawn with
/// Pdfium, so the picture is exactly what choosing it gives. Kept in memory once drawn.
/// </summary>
public sealed class TemplatePreviews
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();
    private readonly SemaphoreSlim _gate = new(2, 2);

    public async Task<byte[]?> GetAsync(string id, double scale)
    {
        var template = TemplateCatalog.Find(id);
        if (template == null) return null;
        scale = Math.Clamp(Math.Round(scale, 1), 0.2, 2);
        // Templates show today's date: a new day draws them again.
        string key = $"{template.Id}|{scale}|{DateTime.Today:yyyyMMdd}";
        if (_cache.TryGetValue(key, out var png)) return png;

        await _gate.WaitAsync();
        try
        {
            if (_cache.TryGetValue(key, out png)) return png;
            var pdf = Path.Combine(Path.GetTempPath(), $"pdfedit-template-{Guid.NewGuid():N}.pdf");
            try
            {
                await Task.Run(() => DesignPdfExporter.Export(template.Create(), pdf));
                using var engine = new PdfiumRenderEngine();
                await engine.LoadAsync(pdf);
                var page = await engine.RenderPageAsync(0, zoom: 1.0, dpiScale: scale);
                png = PngEncoder.FromBgra(page.Pixels ?? [], page.PixelWidth, page.PixelHeight);
                _cache[key] = png;
                return png;
            }
            finally { try { File.Delete(pdf); } catch { } }
        }
        finally { _gate.Release(); }
    }
}
