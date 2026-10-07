using System.Windows;
using PdfEdit.Dialogs;
using PdfEdit.Services;

namespace PdfEdit;

/// <summary>
/// Software updates: File → Check for Updates, and a quiet check of GitHub at startup (at most
/// once a day, unless turned off) that offers a newer version the user hasn't skipped.
/// </summary>
public partial class MainWindow
{
    private void CheckForUpdates_Click(object sender, RoutedEventArgs e) =>
        new UpdateDialog { Owner = this }.ShowDialog();

    private async Task CheckForUpdatesAtStartupAsync()
    {
        var s = AppSettings.Current;
        if (!s.CheckForUpdatesAtStartup || DateTime.UtcNow - s.LastUpdateCheckUtc < TimeSpan.FromDays(1)) return;

        UpdateInfo? latest;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5));   // let startup (and any tip or tour) settle first
            latest = await UpdateService.GetLatestAsync(s.IncludePrereleaseUpdates);
            s.LastUpdateCheckUtc = DateTime.UtcNow;
            s.Save();
        }
        catch { return; }   // offline or GitHub unavailable: try again next start

        if (latest == null || !UpdateService.IsNewer(latest.Version, UpdateInstaller.CurrentVersion)) return;
        if (latest.Version.ToString() == s.SkippedUpdateVersion) return;

        // Offer it straight away when nothing else is open; otherwise just mention it.
        if (IsVisible && IsActive && OwnedWindows.Count == 0 && !_tourRunning)
            new UpdateDialog(latest) { Owner = this }.ShowDialog();
        else
            ToastService.Instance.Info($"PdfEdit {latest.Version} is available — File → Check for Updates to install it.");
    }
}
