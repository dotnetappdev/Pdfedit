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
    /// <summary>"System" (follow Windows), "Light", "Dark" or "HighContrast".</summary>
    public string Theme { get; set; } = "System";
    /// <summary>
    /// True once the user picks a theme themselves. Until then PdfEdit follows Windows (light,
    /// dark or a contrast theme), whatever an older version saved as its default.
    /// </summary>
    public bool ThemeChosenByUser { get; set; }
    /// <summary>The theme actually used: the user's choice, or "System" (follow Windows).</summary>
    [JsonIgnore] public string EffectiveTheme => ThemeChosenByUser ? Theme : "System";
    /// <summary>Use the Windows accent colour for highlights, buttons and the ribbon.</summary>
    public bool UseWindowsAccent { get; set; }
    /// <summary>Spell check typed text and text fields.</summary>
    public bool SpellCheck { get; set; } = true;
    public double UiScale { get; set; } = 1.0;

    // Per-area interface font sizes (keyed by InterfaceFontArea.Key).
    // Absent keys fall back to each area's default size.
    public Dictionary<string, double> InterfaceFontSizes { get; set; } = new();
    /// <summary>The "Text size (all)" slider in Settings → Accessibility (1.0 = default sizes).</summary>
    public double GlobalTextScale { get; set; } = 1.0;

    // ── Cloud storage (your own OAuth app credentials) ─────────────────────
    public string GoogleClientId { get; set; } = string.Empty;
    public string GoogleClientSecret { get; set; } = string.Empty;
    public string OneDriveClientId { get; set; } = string.Empty;
    /// <summary>"common" (any Microsoft account), "consumers", "organizations" or a tenant ID.</summary>
    public string OneDriveTenant { get; set; } = "common";
    /// <summary>Sign-in tokens per provider, encrypted for this Windows user (DPAPI).</summary>
    public Dictionary<string, string> CloudTokens { get; set; } = new();
    /// <summary>Signed-in account name per provider, for display.</summary>
    public Dictionary<string, string> CloudAccounts { get; set; } = new();
    /// <summary>Local copies of cloud files → where they came from, so Save uploads them back.</summary>
    public Dictionary<string, CloudLink> CloudLinks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Upload a cloud file back automatically when it's saved.</summary>
    public bool CloudAutoUpload { get; set; } = true;

    // ── Keyboard ─────────────────────────────────────────────────────────────
    /// <summary>Shortcuts the user changed: action id → keys ("Ctrl+Shift+K", several joined by ", ", or "" for none).</summary>
    public Dictionary<string, string> Shortcuts { get; set; } = new();

    // ── Reading views ────────────────────────────────────────────────────────
    public bool NightMode { get; set; }
    public bool TwoPageView { get; set; }
    /// <summary>Auto-scroll speed in screen pixels a second.</summary>
    public double AutoScrollSpeed { get; set; } = 40;

    // ── Welcome ──────────────────────────────────────────────────────────────
    /// <summary>False until the first-launch tour has been finished or skipped.</summary>
    public bool TourCompleted { get; set; }
    /// <summary>Show the Tip of the Day dialog when PdfEdit starts.</summary>
    public bool ShowTipsAtStartup { get; set; } = true;
    /// <summary>The next tip to show (index into the tips list).</summary>
    public int NextTipIndex { get; set; }

    // ── Updates ──────────────────────────────────────────────────────────────
    /// <summary>Look for a new release on GitHub each time PdfEdit starts.</summary>
    public bool CheckForUpdatesAtStartup { get; set; } = true;
    /// <summary>Offer pre-release (beta) versions too.</summary>
    public bool IncludePrereleaseUpdates { get; set; }
    /// <summary>When the startup check last ran (UTC).</summary>
    public DateTime LastUpdateCheckUtc { get; set; }
    /// <summary>A version the user chose to skip; the startup check stays quiet about it.</summary>
    public string SkippedUpdateVersion { get; set; } = string.Empty;
    /// <summary>Folder updates are downloaded to; empty = Downloads\PdfEdit Updates.</summary>
    public string UpdateDownloadFolder { get; set; } = string.Empty;
    /// <summary>Close PdfEdit before installing a downloaded update.</summary>
    public bool CloseBeforeUpdate { get; set; } = true;

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
    /// <summary>Older settings files: custom stamp texts only. Moved into CustomStampDefinitions on load.</summary>
    public List<string> CustomStamps { get; set; } = new();
    /// <summary>The user's own stamps: text, colour and whether they add name + time.</summary>
    public List<CustomStampSetting> CustomStampDefinitions { get; set; } = new();
    /// <summary>The stamp the Stamp tool starts with, as "Category|Title" ("" = the first, APPROVED).</summary>
    public string DefaultStamp { get; set; } = string.Empty;

    // ── AI ───────────────────────────────────────────────────────────────────
    public string ClaudeApiKey { get; set; } = string.Empty;
    public string OpenAiApiKey { get; set; } = string.Empty;
    /// <summary>GitHub personal access token with the "models" permission, for GitHub Copilot (GitHub Models).</summary>
    public string GitHubToken { get; set; } = string.Empty;
    public string AiProvider { get; set; } = "Claude";
    // Local AI (free, private, no API cost): any OpenAI-compatible server on this machine or the
    // network — Ollama (http://localhost:11434/v1), LM Studio (http://localhost:1234/v1), llama.cpp
    // server, Jan, GPT4All, LocalAI…
    public string LocalAiEndpoint { get; set; } = "http://localhost:11434/v1";
    public string LocalAiModel { get; set; } = "llama3.2";
    public string LocalAiApiKey { get; set; } = string.Empty;   // most local servers need none
    /// <summary>Language for AI translations; empty = Windows' display language.</summary>
    public string AiTranslateLanguage { get; set; } = string.Empty;

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
    public string AiModel { get; set; } = "claude-opus-5-5";

    // ── Floating toolbox position ────────────────────────────────────────────
    public double ToolboxLeft { get; set; } = double.NaN;
    public double ToolboxTop  { get; set; } = double.NaN;

    // ── Render engine ────────────────────────────────────────────────────────
    /// <summary>"Pdfium" (default, Chrome/Adobe quality) or "WinRT" (legacy Windows renderer).</summary>
    public string RenderEngine { get; set; } = "Custom";

    // ── Accessibility ────────────────────────────────────────────────────────
    /// <summary>A thick yellow-and-black ring round whatever has keyboard focus.</summary>
    public bool HighContrastFocusIndicators { get; set; }
    /// <summary>Turn off fades and slides (menus, notifications). Windows' "Animation effects: off" does the same.</summary>
    public bool ReduceMotion { get; set; }
    /// <summary>How long notifications stay on screen, in seconds; 0 = until clicked.</summary>
    public int ToastSeconds { get; set; } = 4;
    /// <summary>Wait before a tooltip appears, in milliseconds.</summary>
    public int TooltipDelayMs { get; set; } = 500;
    /// <summary>Keep tooltips open until the pointer moves away (instead of a few seconds).</summary>
    public bool TooltipsStayOpen { get; set; }
    /// <summary>Single-letter tool keys (H, V, T, D …). Off for screen reader users who navigate with letters.</summary>
    public bool SingleKeyShortcuts { get; set; } = true;
    /// <summary>Read the guided tour and Tip of the Day aloud in PdfEdit's voice.</summary>
    public bool ReadTourAloud { get; set; }
    /// <summary>Ribbon and toolbar icon size: "Small", "Normal", "Large" or "ExtraLarge".</summary>
    public string IconSize { get; set; } = "Normal";

    // What PdfEdit announces…
    public bool AnnounceStatus { get; set; } = true;
    public bool AnnouncePageChanges { get; set; } = true;
    public bool AnnounceToolChanges { get; set; } = true;
    // …and how: to a screen reader (Narrator, NVDA, JAWS) and/or with PdfEdit's own voice.
    public bool AnnounceToScreenReader { get; set; } = true;
    public bool NarrateAnnouncements { get; set; }
    /// <summary>Speak the name of a button, box or menu item when it gets keyboard focus.</summary>
    public bool NarrateFocus { get; set; }
    /// <summary>Speak tooltips when they appear.</summary>
    public bool NarrateTooltips { get; set; }
    /// <summary>Keep PdfEdit's voice quiet while a screen reader is running, so nothing is said twice.</summary>
    public bool NarrateOnlyWithoutScreenReader { get; set; } = true;
    /// <summary>Windows voice for narration and Read Aloud; empty = the Windows default voice.</summary>
    public string NarrationVoice { get; set; } = string.Empty;
    public double NarrationRate { get; set; } = 1.0;
    public double NarrationVolume { get; set; } = 1.0;

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

    // Kept on this PC: never written to a preferences backup, and kept when one is restored or
    // preferences are reset (API keys, sign-ins, recent file paths).
    private static readonly string[] LocalOnly =
    {
        nameof(ClaudeApiKey), nameof(OpenAiApiKey), nameof(GitHubToken), nameof(LocalAiApiKey),
        nameof(GoogleClientSecret), nameof(CloudTokens), nameof(CloudAccounts), nameof(CloudLinks), nameof(RecentFiles),
        nameof(UpdateDownloadFolder), nameof(LastUpdateCheckUtc),
    };

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Writes every preference except keys, sign-ins and recent files to <paramref name="path"/>.</summary>
    public void ExportPreferences(string path)
    {
        var node = JsonSerializer.SerializeToNode(this)!.AsObject();
        foreach (var key in LocalOnly) node.Remove(key);
        node["PdfEditPreferences"] = 1;
        File.WriteAllText(path, node.ToJsonString(Indented));
    }

    /// <summary>
    /// Replaces the current preferences with a backup made by <see cref="ExportPreferences"/>,
    /// keeping this PC's keys, sign-ins and recent files. Throws if the file isn't a backup.
    /// </summary>
    public static void ImportPreferences(string path)
    {
        var imported = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                       ?? throw new InvalidDataException("The file is empty.");
        if (!imported.ContainsKey("PdfEditPreferences") && !imported.ContainsKey(nameof(Theme)))
            throw new InvalidDataException("This file isn't a PdfEdit preferences backup.");
        ReplaceKeepingLocal(imported);
    }

    /// <summary>Puts every preference back to its default, keeping this PC's keys, sign-ins and recent files.</summary>
    public static void ResetPreferences() =>
        ReplaceKeepingLocal(JsonSerializer.SerializeToNode(new AppSettings())!.AsObject());

    private static void ReplaceKeepingLocal(System.Text.Json.Nodes.JsonObject incoming)
    {
        var current = JsonSerializer.SerializeToNode(Current)!.AsObject();
        incoming.Remove("PdfEditPreferences");
        foreach (var key in LocalOnly)
            incoming[key] = current[key]?.DeepClone();
        var next = incoming.Deserialize<AppSettings>() ?? throw new InvalidDataException("The preferences couldn't be read.");
        Current = next;
        next.Save();
    }

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
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, Indented));
        }
        catch { }
    }
}

/// <summary>A local copy of a file in cloud storage.</summary>
public class CloudLink
{
    public string Provider { get; set; } = "";
    public string FileId { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>A custom stamp as saved in settings.json.</summary>
public sealed class CustomStampSetting
{
    public string Title { get; set; } = string.Empty;
    public string Color { get; set; } = "#6A1B9A";
    /// <summary>Adds "By … at …" under the title, like Acrobat's dynamic stamps.</summary>
    public bool Dynamic { get; set; }
}
