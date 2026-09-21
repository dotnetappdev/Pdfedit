using System.IO;
using System.Text.Json;
using PdfEdit.Models;

namespace PdfEdit.Services;

public static class SignatureStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PdfEdit", "signatures.json");

    public static List<SavedSignature> Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var list = JsonSerializer.Deserialize<List<SavedSignature>>(json);
                if (list != null) return list;
            }
        }
        catch { }
        return new();
    }

    public static void Save(IEnumerable<SavedSignature> signatures)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath,
                JsonSerializer.Serialize(signatures.ToList(),
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static void Add(SavedSignature sig)
    {
        var list = Load();
        list.Add(sig);
        Save(list);
    }

    public static void Remove(string id)
    {
        var list = Load();
        list.RemoveAll(s => s.Id == id);
        Save(list);
    }
}
