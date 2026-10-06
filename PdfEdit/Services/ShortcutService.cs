using System.Windows.Input;
using PdfEdit.ViewModels;

namespace PdfEdit.Services;

/// <summary>A key and its modifiers, written like "Ctrl+Shift+S" or "F5".</summary>
public readonly record struct KeyCombo(Key Key, ModifierKeys Modifiers)
{
    private static readonly Dictionary<Key, string> Names = new()
    {
        [Key.OemPlus] = "Plus", [Key.OemMinus] = "Minus", [Key.Add] = "Num+", [Key.Subtract] = "Num-",
        [Key.Multiply] = "Num*", [Key.Divide] = "Num/", [Key.OemOpenBrackets] = "[", [Key.OemCloseBrackets] = "]",
        [Key.OemComma] = ",", [Key.OemPeriod] = ".", [Key.OemQuestion] = "/", [Key.OemSemicolon] = ";",
        [Key.OemQuotes] = "'", [Key.OemBackslash] = "\\", [Key.Oem5] = "\\", [Key.OemTilde] = "`",
        [Key.Next] = "PageDown", [Key.Prior] = "PageUp", [Key.Return] = "Enter", [Key.Back] = "Backspace",
        [Key.Capital] = "CapsLock",
    };

    /// <summary>Plain keys (no Ctrl / Alt) are tool keys: they're ignored while typing.</summary>
    public bool IsPlain => (Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0 && !IsFunctionKey;
    public bool IsFunctionKey => Key is >= Key.F1 and <= Key.F24;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    public static string KeyName(Key k) =>
        Names.TryGetValue(k, out var n) ? n
        : k is >= Key.D0 and <= Key.D9 ? ((char)('0' + (k - Key.D0))).ToString()
        : k is >= Key.NumPad0 and <= Key.NumPad9 ? "Num" + (k - Key.NumPad0)
        : k.ToString();

    public static KeyCombo? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split('+', StringSplitOptions.TrimEntries);
        // "Ctrl++" / "Num+" have a '+' as the key itself.
        if (text.EndsWith("++")) parts = parts.Where(p => p.Length > 0).Append("Plus").ToArray();
        else if (text.EndsWith("Num+", StringComparison.OrdinalIgnoreCase)) parts = parts.Where(p => p.Length > 0).Select(p => p.Equals("Num", StringComparison.OrdinalIgnoreCase) ? "Num+" : p).ToArray();
        var mods = ModifierKeys.None;
        foreach (var p in parts[..^1])
            mods |= p.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control, "shift" => ModifierKeys.Shift,
                "alt" => ModifierKeys.Alt, "win" or "windows" => ModifierKeys.Windows, _ => ModifierKeys.None,
            };
        string keyText = parts[^1];
        Key key;
        var named = Names.FirstOrDefault(kv => string.Equals(kv.Value, keyText, StringComparison.OrdinalIgnoreCase));
        if (named.Value != null) key = named.Key;
        else if (keyText.Length == 1 && char.IsDigit(keyText[0])) key = Key.D0 + (keyText[0] - '0');
        else if (keyText.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && keyText.Length == 4 && char.IsDigit(keyText[3])) key = Key.NumPad0 + (keyText[3] - '0');
        else if (!Enum.TryParse(keyText, true, out key)) return null;
        return new KeyCombo(key, mods);
    }

    /// <summary>The combo for a key press (Alt+key arrives as Key.System).</summary>
    public static KeyCombo FromEvent(KeyEventArgs e) => new(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);

    public static bool IsModifierKey(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.None;
}

/// <summary>Something a shortcut can do.</summary>
public sealed record ShortcutAction(string Id, string Category, string Label, string Defaults, Func<MainViewModel, ICommand?> Command, object? Parameter = null)
{
    public IReadOnlyList<KeyCombo> DefaultCombos => ShortcutService.ParseList(Defaults);
}

/// <summary>
/// Every action that can have a keyboard shortcut, the built-in keys, and the user's own choices
/// (Settings → Keyboard, saved in settings.json as overrides of the defaults).
/// </summary>
public static class ShortcutService
{
    /// <summary>Raised when the user saves new shortcuts, so the window can rebind them.</summary>
    public static event Action? Changed;
    public static void RaiseChanged() => Changed?.Invoke();

    private static ShortcutAction Tool(string id, string label, string keys, string tool) =>
        new(id, "Tools", label, keys, vm => vm.SetToolCommand, tool);

    public static readonly IReadOnlyList<ShortcutAction> Actions = new List<ShortcutAction>
    {
        // File
        new("open", "File", "Open", "Ctrl+O", vm => vm.OpenCommand),
        new("save", "File", "Save", "Ctrl+S", vm => vm.SaveCommand),
        new("save_as", "File", "Save As", "Ctrl+Shift+S", vm => vm.SaveAsCommand),
        new("print", "File", "Print", "Ctrl+P", vm => vm.PrintCommand),
        new("close_tab", "File", "Close tab", "Ctrl+W", vm => vm.CloseTabCommand),
        new("next_tab", "File", "Next tab", "Ctrl+Tab", vm => vm.NextTabCommand),
        new("previous_tab", "File", "Previous tab", "Ctrl+Shift+Tab", vm => vm.PreviousTabCommand),
        new("import_office", "File", "Import Word or Office file", "", vm => vm.CreatePdfFromOfficeCommand),
        new("import_google", "File", "Import from Google Docs", "", vm => vm.ImportGoogleLinkCommand),
        new("import_cloud", "File", "Import from cloud", "", vm => vm.OpenFromCloudCommand),
        new("save_cloud", "File", "Save to cloud", "", vm => vm.SaveToCloudCommand),
        new("flatten", "File", "Flatten & save", "", vm => vm.FlattenAndSaveCommand),
        new("properties", "File", "Document properties", "", vm => vm.DocumentPropertiesCommand),
        new("settings", "File", "Settings", "", vm => vm.OpenSettingsCommand),

        // Edit
        new("undo", "Edit", "Undo", "Ctrl+Z", vm => vm.UndoAnyCommand),
        new("redo", "Edit", "Redo", "Ctrl+Y", vm => vm.RedoAnyCommand),
        new("find", "Edit", "Find", "Ctrl+F, Ctrl+Shift+F", vm => vm.ToggleSearchCommand),
        new("go_to_page", "Edit", "Go to page", "Ctrl+G", vm => vm.GoToPageCommand),
        new("find_highlight", "Edit", "Find & highlight", "", vm => vm.FindAndHighlightCommand),
        new("detect_fields", "Edit", "Detect fields", "", vm => vm.DetectFieldsCommand),
        new("clear_fields", "Edit", "Clear all fields", "", vm => vm.ClearAllFieldsCommand),
        new("export_xfdf", "Edit", "Export comments (XFDF)", "Ctrl+Shift+E", vm => vm.ExportXfdfCommand),
        new("import_xfdf", "Edit", "Import comments (XFDF)", "Ctrl+Shift+I", vm => vm.ImportXfdfCommand),
        new("export_word", "Edit", "Export to Word", "", vm => vm.ExportWordCommand),
        new("export_excel", "Edit", "Export to Excel", "", vm => vm.ExportExcelCommand),
        new("apply_redactions", "Edit", "Apply redactions", "", vm => vm.ApplyRedactionsCommand),

        // Pages
        new("next_page", "Pages", "Next page", "Ctrl+Right", vm => vm.NextPageCommand),
        new("previous_page", "Pages", "Previous page", "Ctrl+Left", vm => vm.PreviousPageCommand),
        new("first_page", "Pages", "First page", "Ctrl+Home", vm => vm.FirstPageCommand),
        new("last_page", "Pages", "Last page", "Ctrl+End", vm => vm.LastPageCommand),
        new("rotate_cw", "Pages", "Rotate page right", "Ctrl+]", vm => vm.RotatePageCWCommand),
        new("rotate_ccw", "Pages", "Rotate page left", "Ctrl+[", vm => vm.RotatePageCCWCommand),
        new("insert_blank", "Pages", "Insert blank page", "", vm => vm.InsertBlankPageCommand),
        new("delete_page", "Pages", "Delete page", "", vm => vm.DeleteCurrentPageCommand),
        new("extract_page", "Pages", "Extract page", "", vm => vm.ExtractCurrentPageCommand),
        new("merge", "Pages", "Merge PDFs", "", vm => vm.MergePdfCommand),
        new("watermark", "Pages", "Watermark", "", vm => vm.WatermarkCommand),
        new("page_numbers", "Pages", "Page numbers", "", vm => vm.AddPageNumbersCommand),
        new("compress", "Pages", "Compress PDF", "", vm => vm.CompressPdfCommand),
        new("ocr", "Pages", "Recognise text (OCR)", "", vm => vm.OcrMakeSearchableCommand),

        // View
        new("zoom_in", "View", "Zoom in on the page", "Ctrl+Alt+Plus, Ctrl+Alt+Num+", vm => vm.ZoomInCommand),
        new("zoom_out", "View", "Zoom out of the page", "Ctrl+Alt+Minus, Ctrl+Alt+Num-", vm => vm.ZoomOutCommand),
        new("zoom_fit", "View", "Fit page", "Ctrl+Shift+0", vm => vm.ZoomFitCommand),
        new("zoom_actual", "View", "Actual size", "Ctrl+1", vm => vm.ZoomActualCommand),
        new("zoom_width", "View", "Fit width", "Ctrl+Shift+W", vm => vm.ZoomWidthCommand),
        new("ui_bigger", "View", "Make everything bigger", "Ctrl+Plus, Ctrl+Shift+Plus, Ctrl+Num+", vm => vm.IncreaseUiScaleCommand),
        new("ui_smaller", "View", "Make everything smaller", "Ctrl+Minus, Ctrl+Shift+Minus, Ctrl+Num-", vm => vm.DecreaseUiScaleCommand),
        new("ui_reset", "View", "Everything back to 100%", "Ctrl+0", vm => vm.ResetUiScaleCommand),
        new("slide_show", "View", "Slide show", "F5", vm => vm.SlideShowCommand),
        new("two_pages", "View", "Two-page view on / off", "", vm => vm.ToggleTwoPageViewCommand),
        new("night_mode", "View", "Night mode on / off", "", vm => vm.ToggleNightModeCommand),
        new("thumbnails", "View", "Show / hide thumbnails", "", vm => vm.ToggleThumbnailsCommand),
        new("read_page", "View", "Read page aloud", "", vm => vm.ReadPageAloudCommand),
        new("stop_reading", "View", "Stop reading", "", vm => vm.StopReadingCommand),
        new("compare", "View", "Visual compare", "", vm => vm.VisualCompareCommand),
        new("shortcuts", "View", "Keyboard shortcuts", "F1", vm => vm.ShowShortcutsCommand),

        // Tools (plain keys work when you're not typing in a box)
        Tool("tool_hand", "Hand", "H", "Hand"),
        Tool("tool_select", "Select", "V", "Select"),
        Tool("tool_text", "Add text", "T, A", "AddText"),
        Tool("tool_date", "Date", "D", "DateStamp"),
        Tool("tool_check", "Check mark", "K, C", "Checkmark"),
        Tool("tool_cross", "Cross", "X", "XMark"),
        Tool("tool_stamp", "Stamp", "M", "Stamp"),
        Tool("tool_sign", "Signature", "S", "Signature"),
        Tool("tool_fill", "Fill text field", "F", "TextFill"),
        Tool("tool_edit_fields", "Edit fields", "E", "EditFields"),
        Tool("tool_zoom", "Zoom tool", "Z", "Zoom"),
        Tool("tool_select_text", "Select text (ask AI)", "Shift+S", "SelectText"),
        Tool("tool_highlight", "Highlight", "I", "Highlight"),
        Tool("tool_draw", "Draw", "W", "DrawFreehand"),
        Tool("tool_note", "Sticky note", "N", "StickyNote"),
        Tool("tool_redact", "Redact", "", "Redact"),
        Tool("tool_link", "Link", "", "Link"),
        Tool("tool_eraser", "Eraser", "", "Eraser"),

        // AI
        new("ai_panel", "AI", "AI Assistant panel", "", vm => vm.ToggleAiPanelCommand),
        new("ai_outline", "AI", "Outline", "", vm => vm.GenerateSummaryCommand),
        new("ai_translate", "AI", "Translate PDF", "", vm => vm.TranslatePdfCommand),
        new("ai_ask_across", "AI", "Ask across PDFs", "", vm => vm.AskAcrossPdfsCommand),
        new("ai_mind_map", "AI", "Mind map", "", vm => vm.MindMapCommand),
        new("ai_design_form", "AI", "Design a form with AI", "", vm => vm.DesignFormWithAiCommand),
    };

    public static IReadOnlyList<KeyCombo> ParseList(string text) =>
        text.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(KeyCombo.Parse).Where(c => c != null).Select(c => c!.Value).ToList();

    /// <summary>The keys an action uses now: the user's choice if they made one, else the defaults.</summary>
    public static IReadOnlyList<KeyCombo> Effective(ShortcutAction a) =>
        AppSettings.Current.Shortcuts.TryGetValue(a.Id, out var custom) ? ParseList(custom) : a.DefaultCombos;

    public static string Display(IReadOnlyList<KeyCombo> combos) => string.Join(", ", combos.Select(c => c.ToString()));

    /// <summary>Every (combo → action) in use now.</summary>
    public static Dictionary<KeyCombo, ShortcutAction> Map()
    {
        var map = new Dictionary<KeyCombo, ShortcutAction>();
        foreach (var a in Actions)
            foreach (var c in Effective(a))
                map.TryAdd(c, a);
        return map;
    }
}
