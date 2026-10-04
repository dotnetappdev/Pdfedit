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
    /// <summary>Spell check typed text and text fields.</summary>
    public bool SpellCheck { get; set; } = true;
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
    public string DefaultDrawingColor { get; set; } = "#C62828";

    // ── Custom stamps ────────────────────────────────────────────────────────
    public List<string> CustomStamps { get; set; } = new();

    // ── AI ───────────────────────────────────────────────────────────────────
    public string ClaudeApiKey { get; set; } = string.Empty;
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string AiProvider { get; set; } = "Claude";
    // Local AI (free, private, no API cost): any OpenAI-compatible server on this machine or the
    // network — Ollama (http://localhost:11434/v1), LM Studio (http://localhost:1234/v1), llama.cpp
    // server, Jan, GPT4All, LocalAI…
    public string LocalAiEndpoint { get; set; } = "http://localhost:11434/v1";
    public string LocalAiModel { get; set; } = "llama3.2";
    public string LocalAiApiKey { get; set; } = string.Empty;   // most local servers need none

    // OCR: "Auto" (Windows OCR if a language is installed, else Tesseract), "Windows" or "Tesseract";
    // Tesseract languages as codes joined with '+', e.g. "eng" or "eng+deu"
    public string OcrEngine { get; set; } = "Auto";
    public string OcrLanguages { get; set; } = "eng";

    // Scan dialog: remembered between scans
    public string? LastScanner { get; set; }
    public int ScanDpi { get; set; } = 200;
    public int ScanColorMode { get; set; }        // 0 colour, 1 grey, 2 black & white
    public int ScanSource { get; set; }           // 0 flatbed, 1 feeder, 2 duplex
    public bool ScanOcr { get; set; } = true;
    public bool ScanShowDriverUi { get; set; }
    public int ScanFileType { get; set; }          // 0 PDF, 1 PNG, 2 JPEG, 3 TIFF, 4 BMP
    public string ScanPaperSize { get; set; } = "Auto";
    public bool ScanAutoCrop { get; set; }
    public string AiModel { get; set; } = "claude-haiku-4-5-20251001";

    // ── Floating toolbox position ────────────────────────────────────────────
    public double ToolboxLeft { get; set; } = double.NaN;
    public double ToolboxTop  { get; set; } = double.NaN;

    // ── Render engine ────────────────────────────────────────────────────────
    /// <summary>"Pdfium" (default, Chrome/Adobe quality) or "WinRT" (legacy Windows renderer).</summary>
    public string RenderEngine { get; set; } = "Custom";

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
