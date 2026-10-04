using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>
/// Stamps (Acrobat's Stamp tool): the catalogue grouped like Acrobat (Standard Business, Sign Here,
/// Dynamic, More, Custom), the one chosen for the Stamp tool, and adding / removing custom stamps.
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

    private void SyncStamps()
    {
        string? keepCategory = _selectedStampDefinition?.Category, keepTitle = _selectedStampDefinition?.Title;
        Stamps.Clear();
        foreach (var d in StampCatalog.All(AppSettings.Current.CustomStamps))
            Stamps.Add(d);
        SelectedStampDefinition = Stamps.FirstOrDefault(d => d.Category == keepCategory && d.Title == keepTitle) ?? Stamps.FirstOrDefault();
    }

    private void AddCustomStamp()
    {
        var dlg = new Dialogs.InputDialog("Custom Stamp", "Enter the text for the custom stamp:", "");
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;
        string text = dlg.InputText.Trim().ToUpperInvariant();
        if (!AppSettings.Current.CustomStamps.Contains(text))
        {
            AppSettings.Current.CustomStamps.Add(text);
            AppSettings.Current.Save();
            SyncStamps();
        }
        SelectedStampDefinition = Stamps.First(d => d.Category == StampCatalog.CustomCategory && d.Title == text);
        ActiveTool = ActiveTool.Stamp;
        StatusText = $"Custom stamp '{text}' added — click the page to place it.";
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
        AppSettings.Current.CustomStamps.RemoveAll(c => c.Equals(title, StringComparison.OrdinalIgnoreCase));
        AppSettings.Current.Save();
        _selectedStampDefinition = null;
        SyncStamps();
        StatusText = $"Custom stamp '{title}' removed.";
    }, () => IsSelectedStampCustom);
}
