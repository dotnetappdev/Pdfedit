using System.Runtime.InteropServices;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// Check for Updates in the desktop app (Help, and About PdfEdit): the newest release on GitHub
/// against this version, with a download of the right installer for this computer. The website
/// itself is always the latest, so it has none of this.
/// </summary>
public partial class Editor
{
    public static bool IsDesktop => PdfEditWebHost.Desktop != null;

    public static Version CurrentVersion =>
        typeof(PdfFormService).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : new Version(0, 0, 0);

    public bool CheckingForUpdates { get; private set; }
    /// <summary>The newer release found, or null.</summary>
    public UpdateInfo? AvailableUpdate { get; private set; }
    /// <summary>What the last check found ("PdfEdit is up to date", an error …), or null before one.</summary>
    public string? UpdateStatus { get; private set; }

    // Once per run of the app, not each time the page reloads.
    private static bool _checkedAtStartup;

    private void ShowCheckForUpdates()
    {
        ShowDialog(DialogKind.About);
        _ = CheckForUpdatesAsync();
    }

    public async Task CheckForUpdatesAsync(bool quiet = false)
    {
        if (!IsDesktop || CheckingForUpdates) return;
        CheckingForUpdates = true;
        if (!quiet) { UpdateStatus = "Checking for updates…"; StateHasChanged(); }
        try
        {
            var latest = await UpdateService.GetLatestAsync(includePrerelease: false);
            AvailableUpdate = latest != null && UpdateService.IsNewer(latest.Version, CurrentVersion) ? latest : null;
            UpdateStatus = AvailableUpdate is { } u
                ? $"PdfEdit {u.Version.ToString(3)} is available (you have {CurrentVersion.ToString(3)})."
                : $"PdfEdit is up to date ({CurrentVersion.ToString(3)}).";
            if (quiet && AvailableUpdate is { } found)
                Toast($"PdfEdit {found.Version.ToString(3)} is available. Help → Check for Updates to get it.", "success");
        }
        catch (Exception ex)
        {
            UpdateStatus = quiet ? null : "Couldn't check for updates: " + ex.Message;
        }
        finally
        {
            CheckingForUpdates = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task CheckForUpdatesAtStartupAsync()
    {
        if (!IsDesktop || _checkedAtStartup || !Settings.CheckForUpdatesAtStartup) return;
        _checkedAtStartup = true;
        await CheckForUpdatesAsync(quiet: true);
    }

    /// <summary>Downloads the update's installer in the browser, or opens its release page when there isn't one for this computer.</summary>
    public Task DownloadUpdateAsync()
    {
        if (AvailableUpdate is not { } u) return Task.CompletedTask;
        var asset = UpdateService.PickDesktopAsset(u, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(),
                                                   RuntimeInformation.OSArchitecture == Architecture.Arm64);
        return OpenUrlAsync(asset?.DownloadUrl is { Length: > 0 } url ? url : u.PageUrl is { Length: > 0 } page ? page : UpdateService.ReleasesPage);
    }
}
