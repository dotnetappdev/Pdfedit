using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace PdfEdit.Services;

/// <summary>A file attached to a GitHub release (installer, ZIP, MSIX).</summary>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size, string? Sha256);

/// <summary>A published PdfEdit release on GitHub.</summary>
public sealed record UpdateInfo(
    Version Version, string Tag, string Name, string Notes, string PageUrl,
    bool Prerelease, DateTimeOffset? Published, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>How this copy of PdfEdit was installed, which decides the package an update needs.</summary>
public enum InstallKind
{
    /// <summary>The EXE installer (Inno Setup): update with the new installer.</summary>
    Installer,
    /// <summary>The self-contained ZIP: unzip the new portable ZIP over it.</summary>
    Portable,
    /// <summary>The small ZIP that needs the .NET Desktop Runtime.</summary>
    PortableFrameworkDependent,
    /// <summary>The MSIX package: Windows' App Installer applies the new .msix.</summary>
    Msix,
}

/// <summary>Bytes downloaded so far and the total (0 when the server doesn't say).</summary>
public readonly record struct DownloadProgress(long Received, long Total, double BytesPerSecond)
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Received / Total, 0, 1) : 0;
}

/// <summary>
/// Checks the GitHub releases of dotnetappdev/pdfedit for a newer version, picks the package that
/// matches how this copy was installed and downloads it. Applying it is up to the front end.
/// </summary>
public static class UpdateService
{
    public const string Owner = "dotnetappdev";
    public const string Repo = "pdfedit";
    public const string ReleasesPage = $"https://github.com/{Owner}/{Repo}/releases";

    private static readonly HttpClient _http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };   // downloads can be slow; calls pass their own tokens
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PdfEdit-Updater", "1.0"));
        return http;
    }

    /// <summary>
    /// The newest release (pre-releases too when <paramref name="includePrerelease"/>), or null
    /// when the repository has none.
    /// </summary>
    public static async Task<UpdateInfo?> GetLatestAsync(bool includePrerelease, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=20");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await _http.SendAsync(req, timeout.Token).ConfigureAwait(false);
        if ((int)resp.StatusCode == 403 || (int)resp.StatusCode == 429)
            throw new InvalidOperationException("GitHub is limiting requests from this network right now. Try again in a while.");
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        return ParseReleases(json.RootElement, includePrerelease)
            .OrderByDescending(r => r.Version)
            .FirstOrDefault();
    }

    /// <summary>Reads the GitHub "list releases" JSON, leaving out drafts and tags that aren't versions.</summary>
    internal static IEnumerable<UpdateInfo> ParseReleases(JsonElement releases, bool includePrerelease)
    {
        if (releases.ValueKind != JsonValueKind.Array) yield break;
        foreach (var r in releases.EnumerateArray())
        {
            if (Bool(r, "draft")) continue;
            bool pre = Bool(r, "prerelease");
            if (pre && !includePrerelease) continue;
            var tag = Str(r, "tag_name");
            if (ParseVersion(tag) is not { } version) continue;

            var assets = new List<ReleaseAsset>();
            if (r.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array)
                foreach (var asset in a.EnumerateArray())
                {
                    var digest = Str(asset, "digest");   // "sha256:<hex>" on releases made since mid-2025
                    assets.Add(new ReleaseAsset(
                        Str(asset, "name"), Str(asset, "browser_download_url"),
                        asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var n) ? n : 0,
                        digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null));
                }

            DateTimeOffset? published = DateTimeOffset.TryParse(Str(r, "published_at"), out var p) ? p : null;
            var name = Str(r, "name");
            yield return new UpdateInfo(version, tag, string.IsNullOrWhiteSpace(name) ? tag : name,
                Str(r, "body"), Str(r, "html_url"), pre, published, assets);
        }

        static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        static bool Bool(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    }

    /// <summary>"v1.2.3", "1.2.3" or "v1.2.3-beta.1" → 1.2.3 (null when it isn't a version).</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(['-', '+']);
        if (cut >= 0) s = s[..cut];
        if (!Version.TryParse(s, out var v)) return null;
        // Compare as major.minor.build: 1.2.3 and 1.2.3.0 are the same release.
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    /// <summary>True when <paramref name="latest"/> is newer than the running <paramref name="current"/> version.</summary>
    public static bool IsNewer(Version latest, Version current) =>
        latest > new Version(current.Major, current.Minor, Math.Max(current.Build, 0));

    /// <summary>Works out how the copy of PdfEdit in <paramref name="appDir"/> was installed.</summary>
    public static InstallKind DetectInstallKind(string appDir)
    {
        if (appDir.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return InstallKind.Msix;
        if (File.Exists(Path.Combine(appDir, "unins000.exe"))) return InstallKind.Installer;
        // A self-contained publish carries the runtime next to the app.
        return File.Exists(Path.Combine(appDir, "coreclr.dll"))
            ? InstallKind.Portable
            : InstallKind.PortableFrameworkDependent;
    }

    /// <summary>The release file that updates an install of the given kind (null when the release lacks one).</summary>
    public static ReleaseAsset? PickAsset(UpdateInfo release, InstallKind kind)
    {
        // PdfEdit Desktop's files (PdfEdit-Desktop-…-win-x64.zip) are for the other app.
        bool Is(ReleaseAsset a, string prefix, string suffix) =>
            a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !a.Name.StartsWith("PdfEdit-Desktop-", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

        return kind switch
        {
            InstallKind.Installer => release.Assets.FirstOrDefault(a => Is(a, "PdfEditSetup", ".exe")),
            InstallKind.Portable => release.Assets.FirstOrDefault(a => Is(a, "PdfEdit-", "-win-x64-portable.zip")),
            // Releases since 1.4.2 have only the portable ZIP (.NET included), which updates this one too.
            InstallKind.PortableFrameworkDependent => release.Assets.FirstOrDefault(a => Is(a, "PdfEdit-", "-win-x64.zip"))
                                                      ?? release.Assets.FirstOrDefault(a => Is(a, "PdfEdit-", "-win-x64-portable.zip")),
            InstallKind.Msix => release.Assets.FirstOrDefault(a => Is(a, "PdfEdit-", ".msix")),
            _ => null,
        };
    }

    /// <summary>
    /// The PdfEdit Desktop (Avalonia) download for this computer: the Mac disk image for Apple silicon
    /// or Intel, or the Linux AppImage. Null when the release lacks one, such as on Windows from 1.4.2,
    /// where PdfEdit for Windows replaces it.
    /// </summary>
    public static ReleaseAsset? PickDesktopAsset(UpdateInfo release, bool windows, bool mac, bool arm64)
    {
        bool Is(ReleaseAsset a, string prefix, string suffix) =>
            a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

        if (windows) return release.Assets.FirstOrDefault(a => Is(a, "PdfEdit-Desktop-Setup-", ".exe"));
        if (mac) return release.Assets.FirstOrDefault(a => Is(a, "PdfEdit-Desktop-", arm64 ? "-mac-arm64.dmg" : "-mac-x64.dmg"));
        return arm64 ? null : release.Assets.FirstOrDefault(a => Is(a, "PdfEdit-Desktop-", "-linux-x64.AppImage"));
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> to <paramref name="destPath"/>, reporting progress. The file
    /// is written to "&lt;dest&gt;.part" and only renamed once complete and checked (size and, when
    /// GitHub gives one, its SHA-256), so a cancelled or broken download never looks finished.
    /// A complete, matching file already at the destination is reused.
    /// </summary>
    public static async Task DownloadAsync(ReleaseAsset asset, string destPath,
        IProgress<DownloadProgress>? progress, CancellationToken ct = default)
    {
        if (File.Exists(destPath) && await IsCompleteAsync(asset, destPath, ct).ConfigureAwait(false))
        {
            var len = new FileInfo(destPath).Length;
            progress?.Report(new DownloadProgress(len, len, 0));
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destPath))!);
        var part = destPath + ".part";
        try
        {
            using var resp = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? asset.Size;

            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                long lastReport = -1;
                int read;
                while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;
                    // ~10 updates a second is plenty for a progress bar.
                    if (clock.ElapsedMilliseconds - lastReport >= 100)
                    {
                        lastReport = clock.ElapsedMilliseconds;
                        progress?.Report(new DownloadProgress(received, total, received / Math.Max(clock.Elapsed.TotalSeconds, 0.001)));
                    }
                }
                progress?.Report(new DownloadProgress(received, total, received / Math.Max(clock.Elapsed.TotalSeconds, 0.001)));
            }

            if (!await IsCompleteAsync(asset, part, ct).ConfigureAwait(false))
                throw new InvalidDataException("The downloaded file is incomplete or damaged (its size or checksum doesn't match). Please try again.");

            File.Move(part, destPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(part)) File.Delete(part); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>True when the file at <paramref name="path"/> matches the asset's size and SHA-256 (when known).</summary>
    public static async Task<bool> IsCompleteAsync(ReleaseAsset asset, string path, CancellationToken ct = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists || (asset.Size > 0 && info.Length != asset.Size)) return false;
        if (string.IsNullOrEmpty(asset.Sha256)) return asset.Size > 0;

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"12.4 MB" style size text.</summary>
    public static string FormatSize(double bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB"];
        int i = 0;
        while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return i == 0 ? $"{bytes:0} {units[i]}" : $"{bytes:0.0} {units[i]}";
    }
}
