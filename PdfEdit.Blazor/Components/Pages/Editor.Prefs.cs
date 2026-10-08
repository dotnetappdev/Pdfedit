using Microsoft.AspNetCore.Components;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

// Settings, custom stamps and fill-in profiles, kept for this browser in the app's SQLite database.
public partial class Editor
{
    [Inject] private UserPrefs Prefs { get; set; } = default!;

    public WebSettings Settings { get; private set; } = new();

    /// <summary>Called once the browser's ID is known (after the first render).</summary>
    private void LoadPrefs()
    {
        if (_clientId.Length == 0) return;
        try
        {
            Settings = Prefs.Get<WebSettings>(_clientId, "settings") ?? new WebSettings();
            CustomStamps = Prefs.Get<List<CustomStampSetting>>(_clientId, "stamps") ?? new();
            Profiles = Prefs.Get<List<PersonalProfile>>(_clientId, "profiles") ?? new();
            ProfileId = Prefs.Get<string[]>(_clientId, "profile")?.FirstOrDefault() ?? Profiles.FirstOrDefault()?.Id ?? "";
        }
        catch { /* no database: the defaults for this visit */ }
        ApplySettings();
        if (Settings.ShowTipsAtStart && _dialog == DialogKind.None) ShowTip();
    }

    private void ApplySettings()
    {
        UiScale = Math.Clamp(Settings.UiScale, 0.8, 1.6);
        HighlightFields = Settings.HighlightFields;
        _fontSize = Math.Clamp(Settings.DefaultFontSize, 4, 144);
        _textColor = Settings.DefaultTextColour;
        if (Settings.AuthorName.Length > 0) _stampAuthor = Settings.AuthorName;
    }

    public void SaveSettings()
    {
        if (_clientId.Length == 0) return;
        try { Prefs.Set(_clientId, "settings", Settings); }
        catch (Exception ex) { Toast("Couldn't keep the settings: " + ex.Message, "error"); }
    }

    /// <summary>The Settings dialog's Save.</summary>
    public void SaveSettingsFromDialog(WebSettings s)
    {
        s.UiScale = Math.Clamp(s.UiScale, 0.8, 1.6);
        s.DefaultFontSize = Math.Clamp(s.DefaultFontSize, 4, 144);
        try { _ = DateTime.Today.ToString(s.DateFormat); } catch (FormatException) { s.DateFormat = "d MMMM yyyy"; }
        Settings = s;
        ApplySettings();
        SaveSettings();
        _dialog = DialogKind.None;
        Toast("Settings saved for this browser.", "success");
    }

    public string DateText(DateTime d)
    {
        return FormatDate(d, Settings.DateFormat);
    }

    // ── Custom stamps (Stamps… / New Stamp / Remove Custom) ──────────────────

    public List<CustomStampSetting> CustomStamps { get; private set; } = new();

    public IEnumerable<StampDefinition> AllStamps => StampCatalog.All(CustomStamps);

    /// <summary>The chosen stamp when it's one of yours.</summary>
    public CustomStampSetting? SelectedCustomStamp =>
        _stampKey.StartsWith(StampCatalog.CustomCategory + "|")
            ? CustomStamps.FirstOrDefault(c => StampCatalog.CustomCategory + "|" + c.Title.Trim() == _stampKey)
            : null;

    /// <summary>A stamp being made or changed in the Stamps dialog.</summary>
    public CustomStampSetting? EditingStamp { get; set; }
    private string? _editingStampTitle;

    private void NewCustomStamp()
    {
        EditingStamp = new CustomStampSetting { Title = "", Color = "#6A1B9A" };
        _editingStampTitle = null;
        _dialog = DialogKind.Stamps;
    }

    public void EditCustomStamp(CustomStampSetting s)
    {
        EditingStamp = new CustomStampSetting { Title = s.Title, Color = s.Color, Dynamic = s.Dynamic };
        _editingStampTitle = s.Title;
    }

    public void SaveEditingStamp()
    {
        if (EditingStamp is not { } s) return;
        s.Title = s.Title.Trim().ToUpperInvariant();
        if (s.Title.Length == 0) { Toast("Type the stamp's text.", "error"); return; }
        if (s.Title.Length > 40) { Toast("Keep the stamp's text to 40 letters or fewer.", "error"); return; }
        if (StampCatalog.BuiltIn.Any(b => b.Title == s.Title) || CustomStamps.Any(c => c.Title == s.Title && c.Title != _editingStampTitle))
        {
            Toast($"There's already a stamp called {s.Title}.", "error");
            return;
        }
        if (_editingStampTitle != null) CustomStamps.RemoveAll(c => c.Title == _editingStampTitle);
        CustomStamps.Add(s);
        SaveStamps();
        EditingStamp = null;
        PickStamp(StampCatalog.CustomCategory + "|" + s.Title);
        Status($"Stamp {s.Title} ready — click the page to stamp it");
    }

    public void DeleteCustomStamp(CustomStampSetting s)
    {
        CustomStamps.Remove(s);
        if (_stampKey == StampCatalog.CustomCategory + "|" + s.Title) _stampKey = StampCatalog.KeyOf(StampCatalog.BuiltIn[0]);
        SaveStamps();
    }

    private Task RemoveSelectedCustomStampAsync()
    {
        if (SelectedCustomStamp is { } s)
        {
            DeleteCustomStamp(s);
            Toast($"Removed the {s.Title} stamp.");
        }
        return Task.CompletedTask;
    }

    private void SaveStamps()
    {
        if (_clientId.Length == 0) return;
        try { Prefs.Set(_clientId, "stamps", CustomStamps); }
        catch (Exception ex) { Toast("Couldn't keep the stamp: " + ex.Message, "error"); }
    }

    // ── Fill-in profiles (Manage Profiles / Quick Fill) ──────────────────────

    public List<PersonalProfile> Profiles { get; private set; } = new();
    private string _profileId = "";

    public string ProfileId
    {
        get => _profileId;
        set
        {
            _profileId = value ?? "";
            if (_clientId.Length > 0) try { Prefs.Set(_clientId, "profile", new[] { _profileId }); } catch { }
        }
    }

    public PersonalProfile? CurrentProfile => Profiles.FirstOrDefault(p => p.Id == _profileId);

    public void SaveProfile(PersonalProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.DisplayName)) profile.DisplayName = profile.FullName.Length > 0 ? profile.FullName : "My Profile";
        int i = Profiles.FindIndex(p => p.Id == profile.Id);
        if (i >= 0) Profiles[i] = profile; else Profiles.Add(profile);
        ProfileId = profile.Id;
        SaveProfiles();
        Toast($"Profile “{profile.DisplayName}” saved for this browser.", "success");
    }

    public void DeleteProfile(PersonalProfile profile)
    {
        Profiles.RemoveAll(p => p.Id == profile.Id);
        if (_profileId == profile.Id) ProfileId = Profiles.FirstOrDefault()?.Id ?? "";
        SaveProfiles();
    }

    private void SaveProfiles()
    {
        if (_clientId.Length == 0) return;
        try { Prefs.Set(_clientId, "profiles", Profiles); }
        catch (Exception ex) { Toast("Couldn't keep the profile: " + ex.Message, "error"); }
    }

    /// <summary>Fills the fields whose names match the profile (name, address, email …).</summary>
    public void QuickFill()
    {
        if (Doc == null || CurrentProfile is not { } profile) return;
        var names = Doc.Info.FormFields.Select(f => f.Name).Distinct().ToList();
        var values = PersonalProfileStore.MatchFields(names, profile);
        int n = 0;
        foreach (var (name, value) in values)
            if (Values.ContainsKey(name) && !string.IsNullOrEmpty(value)) { Values[name] = value; n++; }
        Toast(n == 0 ? $"None of this form's fields match the “{profile.DisplayName}” profile." : $"Filled {n} field{(n == 1 ? "" : "s")} from “{profile.DisplayName}”.",
            n == 0 ? "" : "success");
    }
}
