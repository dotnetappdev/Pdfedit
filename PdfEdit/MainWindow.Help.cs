using System.Diagnostics;
using System.Windows;
using PdfEdit.Dialogs;
using PdfEdit.Services;

namespace PdfEdit;

/// <summary>
/// The Help tab: Check for Updates, What's New, the tour and tips, Report a Problem, GitHub and
/// About; plus a check of GitHub each time PdfEdit starts (unless turned off) that asks about a
/// newer version the user hasn't skipped.
/// </summary>
public partial class MainWindow
{
    public const string GitHubUrl = "https://github.com/dotnetappdev/pdfedit";

    private void CheckForUpdates_Click(object sender, RoutedEventArgs e) => ShowUpdateDialog();

    private void WhatsNew_Click(object sender, RoutedEventArgs e) => OpenWebPage(UpdateService.ReleasesPage);

    private void ReportProblem_Click(object sender, RoutedEventArgs e) => OpenWebPage(GitHubUrl + "/issues/new");

    private void GitHubPage_Click(object sender, RoutedEventArgs e) => OpenWebPage(GitHubUrl);

    /// <summary>Opens <paramref name="url"/> in the default browser.</summary>
    public static void OpenWebPage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { AppDialog.ShowError($"Couldn't open {url}.", ex, "Open web page"); }
    }

    /// <summary>Opens the Software Update window (optionally with a release already found).</summary>
    public void ShowUpdateDialog(UpdateInfo? found = null) =>
        new UpdateDialog(found) { Owner = this }.ShowDialog();

    private async Task CheckForUpdatesAtStartupAsync()
    {
        var s = AppSettings.Current;
        if (!s.CheckForUpdatesAtStartup) return;

        UpdateInfo? latest;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));   // let startup settle first
            latest = await UpdateService.GetLatestAsync(s.IncludePrereleaseUpdates);
            s.LastUpdateCheckUtc = DateTime.UtcNow;
            s.Save();
        }
        catch { return; }   // offline or GitHub unavailable: try again next start

        if (latest == null || !UpdateService.IsNewer(latest.Version, UpdateInstaller.CurrentVersion)) return;
        if (latest.Version.ToString() == s.SkippedUpdateVersion) return;

        // Wait (up to two minutes) for the tour, Tip of the Day or any other window to close, so
        // the question doesn't pop up on top of something else; then just mention it.
        for (int i = 0; i < 120 && (OwnedWindows.Count > 0 || _tourRunning || !IsVisible); i++)
            await Task.Delay(1000);
        if (OwnedWindows.Count > 0 || _tourRunning || !IsVisible)
        {
            ToastService.Instance.Info($"PdfEdit {latest.Version} is available — Help → Check for Updates to install it.");
            return;
        }

        var ask = new UpdateAvailableDialog(latest) { Owner = this };
        ask.ShowDialog();
        switch (ask.Answer)
        {
            case UpdateAnswer.Yes:
                ShowUpdateDialog(latest);
                break;
            case UpdateAnswer.No:
                s.SkippedUpdateVersion = latest.Version.ToString();
                s.Save();
                break;
        }
    }
}
