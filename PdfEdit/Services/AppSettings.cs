using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfEdit.Services;

public class AppSettings
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfEdit");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public string Theme { get; set; } = "Dark";
    public string DefaultFontFamily { get; set; } = "Arial";
    public double DefaultFontSize { get; set; } = 12;
    public double UiScale { get; set; } = 1.0;
    public string ClaudeApiKey { get; set; } = string.Empty;
    public string DefaultFontColor { get; set; } = "#000000";
    public bool ForceUpperCaseDefault { get; set; }
    public bool HighContrastFocusIndicators { get; set; }

    [JsonIgnore]
    public static AppSettings Current { get; private set; } = new();

    public static void Initialize()
    {
        Current = Load();
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) return loaded;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, opts));
        }
        catch { }
    }
}
