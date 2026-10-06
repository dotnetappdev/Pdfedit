using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>
/// Stamps (Acrobat's Stamp tool): the catalogue grouped like Acrobat (Standard Business, Sign Here,
/// Dynamic, More, Custom), the one chosen for the Stamp tool, the default, and creating / editing /
/// removing custom stamps (Stamps… dialog).
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<StampDefinition> Stamps { get; } = new();

    private ICollectionView? _stampsView;
    /// <summary>The stamp list grouped by category, for the ribbon pickers.</summary>
    public ICollectionView StampsView
    {
        get
        {
            if (_stampsView == null)
            {
                var cvs = new CollectionViewSource { Source = Stamps };
                cvs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(StampDefinition.Category)));
                _stampsView = cvs.View;
            }
            return _stampsView;
        }
    }

    private StampDefinition? _selectedStampDefinition;
    public StampDefinition? SelectedStampDefinition
    {
        get => _selectedStampDefinition;
        set
        {
            if (value == null || ReferenceEquals(value, _selectedStampDefinition)) return;
            _selectedStampDefinition = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedStamp));
            OnPropertyChanged(nameof(IsSelectedStampCustom));
        }
    }

    /// <summary>Title of the chosen stamp.</summary>
    public string SelectedStamp
    {
        get => _selectedStampDefinition?.Title ?? "APPROVED";
        set
        {
            var def = Stamps.FirstOrDefault(d => d.Title.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (def != null) SelectedStampDefinition = def;
        }
    }

    public bool IsSelectedStampCustom => _selectedStampDefinition?.Category == StampCatalog.CustomCategory;

    /// <summary>True for the stamp the Stamp tool starts with (Stamps… → Set as default).</summary>
    public bool IsDefaultStamp(StampDefinition d) =>
        string.IsNullOrEmpty(AppSettings.Current.DefaultStamp)
            ? ReferenceEquals(d, Stamps.FirstOrDefault())
            : StampCatalog.KeyOf(d) == AppSettings.Current.DefaultStamp;

    private void SyncStamps()
    {
        var s = AppSettings.Current;
        if (s.CustomStamps.Count > 0)
        {
            // Older settings kept only the text: carry those stamps over in purple.
            foreach (var t in s.CustomStamps.Where(t => !string.IsNullOrWhiteSpace(t)))
                if (!s.CustomStampDefinitions.Any(c => c.Title.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase)))
                    s.CustomStampDefinitions.Add(new CustomStampSetting { Title = t.Trim() });
            s.CustomStamps.Clear();
            s.Save();
        }

        string? keepCategory = _selectedStampDefinition?.Category, keepTitle = _selectedStampDefinition?.Title;
        Stamps.Clear();
        foreach (var d in StampCatalog.All(s.CustomStampDefinitions))
            Stamps.Add(d);
        _selectedStampDefinition = null;
        SelectedStampDefinition = Stamps.FirstOrDefault(d => d.Category == keepCategory && d.Title == keepTitle)
                                  ?? Stamps.FirstOrDefault(d => StampCatalog.KeyOf(d) == s.DefaultStamp)
                                  ?? Stamps.FirstOrDefault();
    }

    /// <summary>
    /// Creates a custom stamp, or changes one (<paramref name="oldTitle"/> = its current text).
    /// Returns the saved stamp, or null with <paramref name="error"/> set.
    /// </summary>
    public StampDefinition? SaveCustomStamp(string? oldTitle, string title, string color, bool dynamic, out string? error)
    {
        error = null;
        title = title.Trim().ToUpperInvariant();
        if (title.Length == 0) { error = "Type the text for the stamp."; return null; }
        var list = AppSettings.Current.CustomStampDefinitions;
        var clash = list.FirstOrDefault(c => c.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        var existing = oldTitle == null ? null : list.FirstOrDefault(c => c.Title.Equals(oldTitle, StringComparison.OrdinalIgnoreCase));
        if (clash != null && !ReferenceEquals(clash, existing)) { error = $"You already have a custom stamp called \"{title}\"."; return null; }

        bool wasDefault = existing != null && AppSettings.Current.DefaultStamp == $"{StampCatalog.CustomCategory}|{existing.Title}";
        if (existing == null) list.Add(existing = new CustomStampSetting());
        existing.Title = title;
        existing.Color = color;
        existing.Dynamic = dynamic;
        if (wasDefault) AppSettings.Current.DefaultStamp = $"{StampCatalog.CustomCategory}|{title}";
        AppSettings.Current.Save();
        SyncStamps();
        var saved = Stamps.First(d => d.Category == StampCatalog.CustomCategory && d.Title == title);
        SelectedStampDefinition = saved;
        StatusText = oldTitle == null ? $"Custom stamp '{title}' created." : $"Custom stamp '{title}' saved.";
        return saved;
    }

    /// <summary>Removes a custom stamp from the list (stamps already placed on pages stay).</summary>
    public void DeleteCustomStamp(string title)
    {
        var s = AppSettings.Current;
        s.CustomStampDefinitions.RemoveAll(c => c.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (s.DefaultStamp == $"{StampCatalog.CustomCategory}|{title}") s.DefaultStamp = string.Empty;
        s.Save();
        if (_selectedStampDefinition?.Category == StampCatalog.CustomCategory && _selectedStampDefinition.Title == title)
            _selectedStampDefinition = null;
        SyncStamps();
        StatusText = $"Custom stamp '{title}' removed.";
    }

    /// <summary>Makes <paramref name="d"/> the stamp the Stamp tool starts with, now and next time.</summary>
    public void SetDefaultStamp(StampDefinition d)
    {
        AppSettings.Current.DefaultStamp = StampCatalog.KeyOf(d);
        AppSettings.Current.Save();
        SelectedStampDefinition = d;
        StatusText = $"'{d.Title}' is now the default stamp.";
    }

    private void AddCustomStamp() => ManageStamps(createNew: true);

    private ICommand? _manageStampsCommand;
    /// <summary>Stamps… : pick, create, edit, delete and set the default stamp, or use one as a background.</summary>
    public ICommand ManageStampsCommand => _manageStampsCommand ??= new RelayCommand(() => ManageStamps(createNew: false));

    public void ManageStamps(bool createNew)
    {
        var dlg = new Dialogs.StampManagerDialog(this, createNew) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;
        if (dlg.OpenWatermark)
        {
            if (HasDocument) _ = WatermarkAsync();
            return;
        }
        if (dlg.Chosen is not { } def) return;
        SelectedStampDefinition = def;
        if (dlg.UseAsBackground)
        {
            if (HasDocument) _ = WatermarkWithAsync(def.Title, def.Color);
            return;
        }
        if (HasDocument)
        {
            IsDesignMode = false;
            ActiveTool = ActiveTool.Stamp;
            StatusText = $"Click the page to place the '{def.Title}' stamp.";
        }
    }

    private ICommand? _removeCustomStampCommand;
    /// <summary>Removes the chosen custom stamp from the list (stamps already placed stay).</summary>
    public ICommand RemoveCustomStampCommand => _removeCustomStampCommand ??= new RelayCommand(() =>
    {
        if (!IsSelectedStampCustom || _selectedStampDefinition == null) return;
        string title = _selectedStampDefinition.Title;
        if (!Dialogs.AppDialog.ShowConfirm($"Remove the custom stamp \"{title}\" from the list?\nStamps already placed on pages are kept.",
                "Remove custom stamp", "Remove", "Cancel", isDanger: true))
            return;
        DeleteCustomStamp(title);
    }, () => IsSelectedStampCustom);
}
