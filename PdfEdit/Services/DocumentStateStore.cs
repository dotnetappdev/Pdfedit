using System.IO;
using System.Text.Json;
using PdfEdit.Models;

namespace PdfEdit.Services;

/// <summary>Persists per-document state (last page, zoom) keyed by normalised file path.</summary>
public static class DocumentStateStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PdfEdit", "docstates.json");

    private static Dictionary<string, DocumentState> _cache = new();
    private static bool _loaded;

    public static DocumentState? Get(string filePath)
    {
        EnsureLoaded();
        return _cache.TryGetValue(Normalise(filePath), out var s) ? s : null;
    }

    public static void Set(string filePath, DocumentState state)
    {
        EnsureLoaded();
        state.LastOpened = DateTime.Now;
        _cache[Normalise(filePath)] = state;
        Flush();
    }

    public static void Remove(string filePath)
    {
        EnsureLoaded();
        if (_cache.Remove(Normalise(filePath)))
            Flush();
    }

    private static string Normalise(string path) => path.Trim().ToLowerInvariant();

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, DocumentState>>(json);
                if (dict != null) _cache = dict;
            }
        }
        catch { }
    }

    private static void Flush()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath,
                JsonSerializer.Serialize(_cache,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
