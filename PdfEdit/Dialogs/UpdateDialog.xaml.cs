using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>
/// File → Check for Updates: looks for a newer release on GitHub, downloads the package that
/// matches how PdfEdit was installed (to a folder the user picks) with a progress bar, and installs
/// it, optionally closing PdfEdit first and starting it again afterwards.
/// </summary>
public partial class UpdateDialog : Window
{
    private readonly InstallKind _kind = UpdateInstaller.InstallKind;
    private UpdateInfo? _release;
    private ReleaseAsset? _asset;
    private string? _downloaded;           // full path once the package is downloaded and checked
    private CancellationTokenSource? _cts;
    private bool _loading;
    private bool _downloading;

    /// <param name="found">A release the startup check already found, so it isn't fetched twice.</param>
    public UpdateDialog(UpdateInfo? found = null)
    {
        InitializeComponent();

        var s = AppSettings.Current;
        _loading = true;
        AtStartupBox.IsChecked = s.CheckForUpdatesAtStartup;
        PrereleaseBox.IsChecked = s.IncludePrereleaseUpdates;
        CloseFirstBox.IsChecked = s.CloseBeforeUpdate;
        _loading = false;
        FolderBox.Text = UpdateInstaller.DownloadFolder;
        CurrentText.Text = $"You have PdfEdit {UpdateInstaller.CurrentVersion} ({UpdateInstaller.Describe(_kind)}).";
        CloseHintText.Text = _kind switch
        {
            InstallKind.Installer => "If you untick this, the setup wizard opens and PdfEdit keeps running until setup asks to close it.",
            InstallKind.Msix => "If you untick this, Windows App Installer opens and closes PdfEdit itself when it updates.",
            _ => "If you untick this, the new files are copied in as soon as you close PdfEdit yourself.",
        };

        Loaded += async (_, _) =>
        {
            if (found != null) ShowRelease(found);
            else await CheckAsync();
        };
    }

    // ── Checking ─────────────────────────────────────────────────────────────

    private async Task CheckAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        SetBusy(true);
        HeadingText.Text = "Checking for updates…";
        DetailsPanel.Visibility = Visibility.Collapsed;
        ReleaseLinkText.Visibility = Visibility.Collapsed;
        SkipBtn.Visibility = DownloadOnlyBtn.Visibility = Visibility.Collapsed;
        PrimaryBtn.IsEnabled = false;
        try
        {
            var latest = await UpdateService.GetLatestAsync(PrereleaseBox.IsChecked == true, ct);
            if (ct.IsCancellationRequested) return;
            AppSettings.Current.LastUpdateCheckUtc = DateTime.UtcNow;
            if (latest == null)
                ShowMessage("No releases were found on GitHub.");
            else if (!UpdateService.IsNewer(latest.Version, UpdateInstaller.CurrentVersion))
                ShowMessage($"PdfEdit is up to date. The latest version is {latest.Version}.", latest);
            else
                ShowRelease(latest);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ShowMessage("Couldn't check for updates: " + Friendly(ex));
        }
        finally
        {
            if (!ct.IsCancellationRequested) SetBusy(false);
            PrimaryBtn.IsEnabled = true;
        }
    }

    private void ShowMessage(string heading, UpdateInfo? release = null)
    {
        _release = null;
        _asset = null;
        HeadingText.Text = heading;
        ReleaseLinkText.Visibility = release != null ? Visibility.Visible : Visibility.Collapsed;
        ReleaseLink.Tag = release?.PageUrl;
        DetailsPanel.Visibility = Visibility.Collapsed;
        SkipBtn.Visibility = DownloadOnlyBtn.Visibility = Visibility.Collapsed;
        PrimaryBtn.Content = "Check _again";
    }

    private void ShowRelease(UpdateInfo release)
    {
        SetBusy(false);
        _release = release;
        _asset = UpdateService.PickAsset(release, _kind);
        _downloaded = null;

        HeadingText.Text = $"PdfEdit {release.Version}{(release.Prerelease ? " (pre-release)" : "")} is available";
        ReleaseLinkText.Visibility = Visibility.Visible;
        ReleaseLink.Tag = release.PageUrl;
        NotesBox.Text = Notes(release);
        DetailsPanel.Visibility = Visibility.Visible;
        ProgressPanel.Visibility = Visibility.Collapsed;
        SkipBtn.Visibility = Visibility.Visible;

        if (_asset == null)
        {
            PackageText.Text = "This release has no download for your kind of install. Use “View release on GitHub” to get it.";
            DownloadOnlyBtn.Visibility = Visibility.Collapsed;
            PrimaryBtn.Content = "Open _GitHub";
            return;
        }

        PackageText.Text = $"Download: {_asset.Name}" +
                           (_asset.Size > 0 ? $" ({UpdateService.FormatSize(_asset.Size)})" : "") +
                           (string.IsNullOrEmpty(_asset.Sha256) ? "" : " · checked with its SHA-256 checksum after download");
        DownloadOnlyBtn.Visibility = Visibility.Visible;
        PrimaryBtn.Content = "Download and _install";
    }

    /// <summary>The "What's new" part of the release text (the install instructions below it aren't needed here).</summary>
    internal static string Notes(UpdateInfo release)
    {
        var text = release.Notes.Replace("\r\n", "\n");
        int cut = text.IndexOf("\n---", StringComparison.Ordinal);
        if (cut > 0) text = text[..cut];
        text = text.Trim();
        return string.IsNullOrEmpty(text) ? "No release notes." : text;
    }

    // ── Downloading and installing ───────────────────────────────────────────

    private async Task<bool> DownloadAsync()
    {
        if (_release == null || _asset == null) return false;

        string folder;
        try
        {
            folder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(FolderBox.Text.Trim()));
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            AppDialog.ShowError("Choose a folder you can save to.\n\n" + ex.Message, title: "Software Update");
            return false;
        }
        var dest = Path.Combine(folder, _asset.Name);

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        SetDownloading(true);
        Progress.Value = 0;
        Progress.IsIndeterminate = _asset.Size <= 0;
        ProgressText.Text = "Starting download…";
        var progress = new Progress<DownloadProgress>(p =>
        {
            if (p.Total > 0)
            {
                Progress.IsIndeterminate = false;
                Progress.Value = p.Fraction;
            }
            var speed = p.BytesPerSecond > 0 ? $" · {UpdateService.FormatSize(p.BytesPerSecond)}/s" : "";
            ProgressText.Text = p.Total > 0
                ? $"{p.Fraction:P0} — {UpdateService.FormatSize(p.Received)} of {UpdateService.FormatSize(p.Total)}{speed}"
                : $"{UpdateService.FormatSize(p.Received)}{speed}";
        });

        try
        {
            await UpdateService.DownloadAsync(_asset, dest, progress, ct);
            _downloaded = dest;
            AppSettings.Current.UpdateDownloadFolder = folder;
            AppSettings.Current.Save();
            Progress.IsIndeterminate = false;
            Progress.Value = 1;
            ProgressText.Text = $"Downloaded to {dest}";
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (IsLoaded) ProgressText.Text = "Download cancelled.";
            return false;
        }
        catch (Exception ex)
        {
            ProgressText.Text = "Download failed: " + Friendly(ex);
            return false;
        }
        finally
        {
            if (IsLoaded) SetDownloading(false);
        }
    }

    private void Install()
    {
        if (_downloaded == null || !File.Exists(_downloaded)) return;
        bool closeFirst = CloseFirstBox.IsChecked == true;
        SavePreferences();


        bool started;
        try { started = UpdateInstaller.Start(_downloaded, _kind, closeFirst); }
        catch (Exception ex)
        {
            AppDialog.ShowError("The update could not be started.", ex, "Software Update");
            return;
        }
        if (!started)
        {
            ProgressText.Text = "Installing needs administrator permission, which wasn't given. Click Install now to try again.";
            return;
        }

        Close();
        if (closeFirst)
        {
            // Close the other PdfEdit windows, then this one (each keeps its unsaved work for next
            // time, as on any normal close); the update then installs and PdfEdit starts again.
            UpdateInstaller.CloseOtherInstances();
            Application.Current.MainWindow?.Close();
            Application.Current.Shutdown();
            return;
        }

        AppDialog.ShowInfo(_kind switch
        {
            InstallKind.Installer => "The setup program has started. Follow its steps to finish the update.",
            InstallKind.Msix => "Windows App Installer has opened. Click Update there to finish.",
            _ => "The update is ready. It will be installed as soon as you close PdfEdit; the next time you start PdfEdit you'll have the new version.",
        }, "Software Update");
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_release == null) { await CheckAsync(); return; }
        if (_asset == null) { OpenUrl(_release.PageUrl); return; }
        if (_downloaded != null) { Install(); return; }
        if (await DownloadAsync()) Install();
    }

    private async void DownloadOnly_Click(object sender, RoutedEventArgs e)
    {
        if (await DownloadAsync())
        {
            PrimaryBtn.Content = "_Install now";
            DownloadOnlyBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Save the PdfEdit update in" };
        try { if (Directory.Exists(FolderBox.Text)) dlg.InitialDirectory = FolderBox.Text; } catch { }
        if (dlg.ShowDialog(this) == true)
        {
            FolderBox.Text = dlg.FolderName;
            _downloaded = null;   // a new place: download again (an existing good copy there is reused)
            if (_asset != null)
            {
                PrimaryBtn.Content = "Download and _install";
                DownloadOnlyBtn.Visibility = Visibility.Visible;
            }
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (_release != null) AppSettings.Current.SkippedUpdateVersion = _release.Version.ToString();
        Close();
    }

    private async void Prerelease_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        AppSettings.Current.IncludePrereleaseUpdates = PrereleaseBox.IsChecked == true;
        await CheckAsync();
    }

    private void ReleaseLink_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(ReleaseLink.Tag as string ?? UpdateService.ReleasesPage);

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_downloading) _cts?.Cancel();   // Cancel stops the download and keeps the dialog open
        else Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _cts?.Cancel();   // stop a download in progress (the partial file is removed)
        SavePreferences();
        base.OnClosing(e);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SavePreferences()
    {
        var s = AppSettings.Current;
        s.CheckForUpdatesAtStartup = AtStartupBox.IsChecked == true;
        s.IncludePrereleaseUpdates = PrereleaseBox.IsChecked == true;
        s.CloseBeforeUpdate = CloseFirstBox.IsChecked == true;
        s.Save();
    }

    private void SetBusy(bool busy) =>
        CheckingBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

    private void SetDownloading(bool downloading)
    {
        _downloading = downloading;
        ProgressPanel.Visibility = Visibility.Visible;
        PrimaryBtn.IsEnabled = DownloadOnlyBtn.IsEnabled = SkipBtn.IsEnabled = !downloading;
        FolderBox.IsEnabled = BrowseBtn.IsEnabled = PrereleaseBox.IsEnabled = !downloading;
        CloseFirstBox.IsEnabled = !downloading;
        CloseBtn.Content = downloading ? "Cancel" : "Close";
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser: nothing more to do */ }
    }

    private static string Friendly(Exception ex) => ex switch
    {
        System.Net.Http.HttpRequestException => "couldn't reach GitHub. Check your internet connection.",
        TaskCanceledException => "GitHub took too long to answer.",
        _ => ex.Message,
    };
}
