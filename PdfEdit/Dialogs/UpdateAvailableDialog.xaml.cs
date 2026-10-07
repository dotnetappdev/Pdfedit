using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>What the user answered when asked about a new version.</summary>
public enum UpdateAnswer
{
    /// <summary>Update now.</summary>
    Yes,
    /// <summary>Skip this version: don't ask about it again.</summary>
    No,
    /// <summary>Not now: ask again next time PdfEdit starts.</summary>
    Cancel,
}

/// <summary>
/// The question shown at launch when GitHub has a newer release: its version, a link to it,
/// the release notes on request, and Yes, update / No / Cancel.
/// </summary>
public partial class UpdateAvailableDialog : Window
{
    private readonly UpdateInfo _release;

    public UpdateAnswer Answer { get; private set; } = UpdateAnswer.Cancel;

    public UpdateAvailableDialog(UpdateInfo release)
    {
        InitializeComponent();
        _release = release;
        VersionText.Text = $"PdfEdit {release.Version}{(release.Prerelease ? " (pre-release)" : "")} — you have {UpdateInstaller.CurrentVersion}." +
                           (release.Published is { } p ? $" Released {p.LocalDateTime:d MMMM yyyy}." : "");
        LinkText.Text = $"View PdfEdit {release.Version} on GitHub";
        NotesBox.Text = UpdateDialog.Notes(release);
        AtStartupBox.IsChecked = AppSettings.Current.CheckForUpdatesAtStartup;
    }

    private void Yes_Click(object sender, RoutedEventArgs e) { Answer = UpdateAnswer.Yes; DialogResult = true; }

    private void No_Click(object sender, RoutedEventArgs e) { Answer = UpdateAnswer.No; DialogResult = false; }

    private void NotesToggle_Changed(object sender, RoutedEventArgs e)
    {
        bool show = NotesToggle.IsChecked == true;
        NotesBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        NotesToggle.Content = show ? "Hide release notes ▴" : "See release notes ▾";
    }

    private void GitHub_Click(object sender, RoutedEventArgs e)
    {
        var url = string.IsNullOrEmpty(_release.PageUrl) ? UpdateService.ReleasesPage : _release.PageUrl;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser */ }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        AppSettings.Current.CheckForUpdatesAtStartup = AtStartupBox.IsChecked == true;
        AppSettings.Current.Save();
        base.OnClosing(e);
    }
}
