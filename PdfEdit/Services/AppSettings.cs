using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfEdit.Services;

public class AppSettings
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfEdit");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    // ── Appearance ───────────────────────────────────────────────────────────
    public string Theme { get; set; } = "Dark";
    public double UiScale { get; set; } = 1.0;

    // Per-area interface font sizes (keyed by InterfaceFontArea.Key).
    // Absent keys fall back to each area's default size.
    public Dictionary<string, double> InterfaceFontSizes { get; set; } = new();

    // ── Window geometry ──────────────────────────────────────────────────────
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }

    // ── Editor defaults ──────────────────────────────────────────────────────
    public string DefaultFontFamily { get; set; } = "Arial";
    public double DefaultFontSize { get; set; } = 12;
    public string DefaultFontColor { get; set; } = "#000000";
    public bool ForceUpperCaseDefault { get; set; }
    public string DateFormat { get; set; } = "MMMM d, yyyy";
    public string LastActiveTool { get; set; } = "Hand";

    // ── AI ───────────────────────────────────────────────────────────────────
    public string ClaudeApiKey { get; set; } = string.Empty;
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string AiProvider { get; set; } = "Claude";
    public string AiModel { get; set; } = "claude-haiku-4-5-20251001";

    // ── Accessibility ────────────────────────────────────────────────────────
    public bool HighContrastFocusIndicators { get; set; }

    // ── Recent files (max 12, newest first) ──────────────────────────────────
    public List<string> RecentFiles { get; set; } = new();

    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > 12) RecentFiles.RemoveAt(RecentFiles.Count - 1);
        Save();
    }

    public void RemoveRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

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
