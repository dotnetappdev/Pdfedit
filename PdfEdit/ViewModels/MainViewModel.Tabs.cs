using System.Collections.ObjectModel;
using System.Windows.Input;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>
/// Document tabs like other PDF editors': every PDF you open gets a tab. Switching tabs keeps your
/// unsaved work (filled fields, text, signatures, comments, page and zoom) for each file, the
/// same way PdfEdit already keeps it when you close and reopen a file.
/// Ctrl+Tab / Ctrl+Shift+Tab move between tabs; Ctrl+W closes the current one.
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<OpenDocumentTab> OpenTabs { get; } = new();
    public bool HasTabs => OpenTabs.Count > 0;

    private bool _switchingTab;
    private ICommand? _nextTabCommand, _previousTabCommand, _closeTabCommand;

    public ICommand NextTabCommand => _nextTabCommand ??= new AsyncRelayCommand(() => CycleTabAsync(+1), () => OpenTabs.Count > 1);
    public ICommand PreviousTabCommand => _previousTabCommand ??= new AsyncRelayCommand(() => CycleTabAsync(-1), () => OpenTabs.Count > 1);
    /// <summary>Closes the current tab (Ctrl+W) and shows the one next to it.</summary>
    public ICommand CloseTabCommand => _closeTabCommand ??= new AsyncRelayCommand(async () =>
    {
        var active = OpenTabs.FirstOrDefault(t => t.IsActive);
        if (active != null) await CloseTabAsync(active);
        else if (HasDocument) CloseDocument();
    }, () => HasDocument);

    /// <summary>Called whenever a file is loaded: adds its tab (or selects it).</summary>
    private void TrackTab(string path)
    {
        var tab = OpenTabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (tab == null)
        {
            tab = new OpenDocumentTab(path);
            // Open next to the current tab, like a browser.
            int at = OpenTabs.IndexOf(OpenTabs.FirstOrDefault(t => t.IsActive)!);
            OpenTabs.Insert(at < 0 ? OpenTabs.Count : at + 1, tab);
            OnPropertyChanged(nameof(HasTabs));
        }
        foreach (var t in OpenTabs) t.IsActive = t == tab;
    }

    /// <summary>Save As moved the open document to a new file.</summary>
    private void RenameActiveTab(string newPath)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.IsActive);
        if (tab != null) tab.Path = newPath;
        else TrackTab(newPath);
    }

    public async Task SwitchToTabAsync(OpenDocumentTab tab)
    {
        if (_switchingTab || tab.IsActive && string.Equals(tab.Path, _currentFilePath, StringComparison.OrdinalIgnoreCase)) return;
        if (!System.IO.File.Exists(tab.Path))
        {
            ToastService.Instance.Warning($"{tab.Title} is no longer there.");
            OpenTabs.Remove(tab);
            OnPropertyChanged(nameof(HasTabs));
            return;
        }
        _switchingTab = true;
        try
        {
            SaveDocumentState();
            await LoadDocumentAsync(tab.Path);
        }
        finally { _switchingTab = false; }
    }

    public async Task CloseTabAsync(OpenDocumentTab tab)
    {
        if (_switchingTab) return;
        int index = OpenTabs.IndexOf(tab);
        if (!tab.IsActive)
        {
            // Not on screen: its unsaved work is already kept; just drop the tab.
            OpenTabs.Remove(tab);
            OnPropertyChanged(nameof(HasTabs));
            return;
        }
        CloseDocument();   // keeps unsaved work for next time, clears the view
        OpenTabs.Remove(tab);
        OnPropertyChanged(nameof(HasTabs));
        if (OpenTabs.Count > 0)
            await SwitchToTabAsync(OpenTabs[Math.Clamp(index, 0, OpenTabs.Count - 1)]);
    }

    public async Task CloseOtherTabsAsync(OpenDocumentTab keep)
    {
        if (!keep.IsActive) await SwitchToTabAsync(keep);
        foreach (var t in OpenTabs.Where(t => t != keep).ToList()) OpenTabs.Remove(t);
        OnPropertyChanged(nameof(HasTabs));
    }

    private async Task CycleTabAsync(int step)
    {
        if (OpenTabs.Count < 2) return;
        int i = OpenTabs.IndexOf(OpenTabs.FirstOrDefault(t => t.IsActive)!);
        await SwitchToTabAsync(OpenTabs[((i < 0 ? 0 : i) + step + OpenTabs.Count) % OpenTabs.Count]);
    }
}
