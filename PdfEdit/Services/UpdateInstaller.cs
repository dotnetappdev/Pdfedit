using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace PdfEdit.Services;

/// <summary>
/// Applies a downloaded update. The EXE installer and the MSIX are handed to Windows; a ZIP is
/// unpacked over this folder by a small PowerShell script that waits for PdfEdit to close first
/// (files of a running program can't be replaced) and starts it again afterwards.
/// </summary>
public static class UpdateInstaller
{
    /// <summary>The folder PdfEdit runs from (no trailing backslash).</summary>
    public static string AppDir => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppDir, "PdfEdit.exe");

    /// <summary>The running version as major.minor.build.</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    public static InstallKind InstallKind => UpdateService.DetectInstallKind(AppDir);

    /// <summary>Downloads\PdfEdit Updates, or the folder the user picked last time.</summary>
    public static string DownloadFolder
    {
        get
        {
            var saved = AppSettings.Current.UpdateDownloadFolder;
            if (!string.IsNullOrWhiteSpace(saved)) return saved;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "PdfEdit Updates");
        }
    }

    public static string Describe(InstallKind kind) => kind switch
    {
        InstallKind.Installer => "installed with the setup program",
        InstallKind.Portable => "portable (ZIP, .NET included)",
        InstallKind.PortableFrameworkDependent => "portable (ZIP, uses the .NET Desktop Runtime)",
        InstallKind.Msix => "installed from an MSIX package",
        _ => "unknown",
    };

    /// <summary>
    /// Starts installing <paramref name="package"/>. With <paramref name="closeFirst"/> the update
    /// runs unattended and PdfEdit restarts when it's done (the caller then closes PdfEdit);
    /// otherwise the setup wizard is shown (installer), or the files are replaced once PdfEdit is
    /// closed (ZIP). The new setup itself uninstalls the previous version before installing.
    /// Returns false when the user declined the Windows administrator prompt.
    /// </summary>
    public static bool Start(string package, InstallKind kind, bool closeFirst)
    {
        try
        {
            switch (kind)
            {
                case InstallKind.Installer:
                    // Inno Setup: /CLOSEAPPLICATIONS closes any PdfEdit still open and /RELAUNCH=1
                    // (read by PdfEditSetup.iss) starts it again after a silent install — once, so Setup's own
                    // restart of closed apps is turned off.
                    var args = closeFirst
                        ? "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /RELAUNCH=1"
                        : "";
                    Process.Start(new ProcessStartInfo(package, args) { UseShellExecute = true });
                    break;

                case InstallKind.Msix:
                    // App Installer updates the package in place (it closes PdfEdit if it's still open).
                    Process.Start(new ProcessStartInfo(package) { UseShellExecute = true });
                    break;

                default:
                    StartZipUpdate(package, relaunch: closeFirst);
                    break;
            }
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)   // ERROR_CANCELLED: UAC prompt declined
        {
            return false;
        }
    }

    /// <summary>
    /// Asks every other PdfEdit window running from this folder to close, as if the user clicked ×
    /// (each keeps its unsaved work for next time, like a normal close).
    /// </summary>
    public static void CloseOtherInstances()
    {
        int me = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExePath)))
        {
            using (p)
            {
                try
                {
                    if (p.Id == me) continue;
                    var path = p.MainModule?.FileName;
                    if (path != null && !string.Equals(Path.GetDirectoryName(path), AppDir, StringComparison.OrdinalIgnoreCase))
                        continue;   // a different copy of PdfEdit: leave it alone
                    p.CloseMainWindow();
                }
                catch { /* gone already, or not ours to touch */ }
            }
        }
    }

    // ── ZIP ──────────────────────────────────────────────────────────────────

    private static void StartZipUpdate(string zip, bool relaunch)
    {
        bool elevate = !CanWriteTo(AppDir);   // e.g. the portable copy lives under Program Files
        RunScript(BuildZipScript(zip, AppDir, ExePath, relaunch, elevate, LogPath), elevate);
    }

    private static string WorkDir
    {
        get
        {
            var work = Path.Combine(Path.GetTempPath(), "PdfEditUpdate");
            Directory.CreateDirectory(work);
            return work;
        }
    }

    private static string LogPath => Path.Combine(WorkDir, "apply-update.log");

    private static void RunScript(string content, bool elevate)
    {
        var script = Path.Combine(WorkDir, "apply-update.ps1");
        File.WriteAllText(script, content, new UTF8Encoding(true));   // BOM: Windows PowerShell 5 reads UTF-8 only with one

        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (elevate) psi.Verb = "runas";
        Process.Start(psi);
    }

    private static bool CanWriteTo(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".pdfedit-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>The PowerShell (5.1 compatible) that waits for PdfEdit to close and unpacks the ZIP over <paramref name="dest"/>.</summary>
    public static string BuildZipScript(string zip, string dest, string exe, bool relaunch, bool elevated, string log) =>
        BuildScript("unpacks the new version over the old one once PdfEdit has closed.",
            zip, dest, exe, relaunch, elevated, log, """
                $tmp = Join-Path ([IO.Path]::GetTempPath()) ('PdfEditUpdate\unpacked-' + [guid]::NewGuid().ToString('N'))
                Log "Unpacking to $tmp"
                Expand-Archive -LiteralPath $package -DestinationPath $tmp -Force
                $src = $tmp
                $items = @(Get-ChildItem -LiteralPath $tmp)
                if ($items.Count -eq 1 -and $items[0].PSIsContainer) { $src = $items[0].FullName }
                if (-not (Test-Path -LiteralPath (Join-Path $src 'PdfEdit.exe'))) { throw 'The update package does not contain PdfEdit.exe.' }

                Log "Copying files"
                # robocopy retries files that are briefly locked; exit codes below 8 mean success.
                & robocopy $src $dest /E /R:10 /W:2 /NP /NFL /NDL /NJH /NJS | Out-Null
                if ($LASTEXITCODE -ge 8) { throw "Copying the new files failed (robocopy exit code $LASTEXITCODE). Is PdfEdit still open?" }
            """);

    private static string BuildScript(string purpose, string package, string dest, string exe,
        bool relaunch, bool elevated, string log, string steps)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var sb = new StringBuilder();
        sb.AppendLine("# PdfEdit updater: " + purpose);
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine($"$package = {Q(package)}");
        sb.AppendLine($"$dest = {Q(dest)}");
        sb.AppendLine($"$exe = {Q(exe)}");
        sb.AppendLine($"$log = {Q(log)}");
        sb.AppendLine($"$relaunch = ${(relaunch ? "true" : "false")}");
        sb.AppendLine($"$elevated = ${(elevated ? "true" : "false")}");
        sb.AppendLine($"$exeName = {Q(Path.GetFileNameWithoutExtension(exe))}");
        sb.Append("""
            function Log($m) { try { Add-Content -LiteralPath $log -Value ("{0:u}  {1}" -f (Get-Date), $m) } catch {} }
            $prefix = $dest.TrimEnd('\') + '\'
            function Running {
                @(Get-Process -Name $exeName -ErrorAction SilentlyContinue | Where-Object {
                    try { $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
                })
            }
            $tmp = $null
            try {
                Log "Update from $package to $dest"
                # Wait for PdfEdit to close. When it was asked to close for the update, give it two
                # minutes; otherwise wait for the user to close it whenever they're ready.
                $deadline = (Get-Date).AddMinutes(2)
                while ((Running).Count -gt 0) {
                    if ($relaunch -and (Get-Date) -gt $deadline) { throw 'PdfEdit did not close, so the update could not be installed. Close PdfEdit and try again.' }
                    Start-Sleep -Seconds 1
                }
                Start-Sleep -Seconds 1

            """);
        sb.Append(steps);
        sb.Append("""

                Log "Update installed"
                if ($relaunch) {
                    # From an elevated script, start PdfEdit through Explorer so it runs as the normal user.
                    if ($elevated) { Start-Process explorer.exe -ArgumentList ('"' + $exe + '"') } else { Start-Process -FilePath $exe }
                }
            }
            catch {
                Log "Failed: $($_.Exception.Message)"
                Add-Type -AssemblyName PresentationFramework
                [void][System.Windows.MessageBox]::Show("PdfEdit could not install the update.`n`n$($_.Exception.Message)`n`nThe downloaded file is still at:`n$package", 'PdfEdit update', 'OK', 'Error')
            }
            finally {
                if ($tmp -and (Test-Path -LiteralPath $tmp)) { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
            }

            """);
        return sb.ToString();
    }
}
