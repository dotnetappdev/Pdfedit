using System.IO;
using System.Text.Json;
using PdfEdit.Services;
using Xunit;

namespace PdfEdit.Tests;

/// <summary>The software updater: reading GitHub releases, comparing versions, picking the package.</summary>
public class UpdateServiceTests
{
    private const string ReleasesJson = """
        [
          { "tag_name": "v1.3.0-beta.1", "name": "PdfEdit 1.3.0-beta.1", "draft": false, "prerelease": true,
            "html_url": "https://github.com/dotnetappdev/pdfedit/releases/tag/v1.3.0-beta.1", "body": "beta", "assets": [] },
          { "tag_name": "v1.2.0", "name": "PdfEdit 1.2.0", "draft": false, "prerelease": false,
            "html_url": "https://github.com/dotnetappdev/pdfedit/releases/tag/v1.2.0",
            "body": "### What's new\n- Updater\n\n---\n### Installation",
            "published_at": "2026-10-01T10:00:00Z",
            "assets": [
              { "name": "PdfEdit-1.2.0-win-x64.zip", "size": 100, "browser_download_url": "https://example/fd.zip" },
              { "name": "PdfEdit-1.2.0-win-x64-portable.zip", "size": 200, "browser_download_url": "https://example/p.zip",
                "digest": "sha256:ABCDEF" },
              { "name": "PdfEditSetup-1.2.0.exe", "size": 300, "browser_download_url": "https://example/setup.exe" },
              { "name": "PdfEdit-1.2.0.0.msix", "size": 400, "browser_download_url": "https://example/app.msix" }
            ] },
          { "tag_name": "v9.9.9", "draft": true, "prerelease": false, "assets": [] },
          { "tag_name": "nightly", "draft": false, "prerelease": false, "assets": [] }
        ]
        """;

    private static List<UpdateInfo> Parse(bool pre)
    {
        using var doc = JsonDocument.Parse(ReleasesJson);
        return UpdateService.ParseReleases(doc.RootElement, pre).ToList();
    }

    [Fact]
    public void Drafts_and_non_version_tags_are_ignored()
    {
        var stable = Parse(pre: false);
        Assert.Single(stable);
        Assert.Equal(new Version(1, 2, 0), stable[0].Version);
        Assert.Equal(4, stable[0].Assets.Count);
        Assert.Equal("ABCDEF", stable[0].Assets[1].Sha256);

        Assert.Equal(2, Parse(pre: true).Count);
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3-beta.1", "1.2.3")]
    [InlineData("v1.2", "1.2.0")]
    [InlineData("1.2.3.0", "1.2.3")]
    public void Versions_are_read_from_tags(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateService.ParseVersion(tag));

    [Theory]
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_version_tags_give_null(string? tag) => Assert.Null(UpdateService.ParseVersion(tag));

    [Fact]
    public void Newer_compares_major_minor_build_only()
    {
        Assert.True(UpdateService.IsNewer(new Version(1, 2, 0), new Version(1, 1, 0, 0)));
        Assert.False(UpdateService.IsNewer(new Version(1, 1, 0), new Version(1, 1, 0, 0)));
        Assert.False(UpdateService.IsNewer(new Version(1, 0, 9), new Version(1, 1, 0, 0)));
    }

    [Theory]
    [InlineData(InstallKind.Installer, "PdfEditSetup-1.2.0.exe")]
    [InlineData(InstallKind.Portable, "PdfEdit-1.2.0-win-x64-portable.zip")]
    [InlineData(InstallKind.PortableFrameworkDependent, "PdfEdit-1.2.0-win-x64.zip")]
    [InlineData(InstallKind.Msix, "PdfEdit-1.2.0.0.msix")]
    public void The_package_matches_the_install(InstallKind kind, string expected) =>
        Assert.Equal(expected, UpdateService.PickAsset(Parse(false)[0], kind)?.Name);

    [Fact]
    public void Install_kind_is_detected_from_the_app_folder()
    {
        var dir = Directory.CreateTempSubdirectory("pdfedit-update-").FullName;
        try
        {
            Assert.Equal(InstallKind.PortableFrameworkDependent, UpdateService.DetectInstallKind(dir));
            File.WriteAllText(Path.Combine(dir, "coreclr.dll"), "");
            Assert.Equal(InstallKind.Portable, UpdateService.DetectInstallKind(dir));
            File.WriteAllText(Path.Combine(dir, "unins000.exe"), "");
            Assert.Equal(InstallKind.Installer, UpdateService.DetectInstallKind(dir));
            Assert.Equal(InstallKind.Msix, UpdateService.DetectInstallKind(@"C:\Program Files\WindowsApps\PdfEdit_1.1.0.0_x64__abc"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task A_matching_file_is_complete_and_a_wrong_one_is_not()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "hello");
            // SHA-256 of "hello"
            const string sha = "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824";
            Assert.True(await UpdateService.IsCompleteAsync(new ReleaseAsset("a", "u", 5, sha), file));
            Assert.False(await UpdateService.IsCompleteAsync(new ReleaseAsset("a", "u", 5, new string('0', 64)), file));
            Assert.False(await UpdateService.IsCompleteAsync(new ReleaseAsset("a", "u", 6, null), file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Zip_script_quotes_paths_and_relaunches_only_when_asked()
    {
        var script = UpdateInstaller.BuildZipScript(@"C:\Users\O'Neil\Downloads\u.zip", @"C:\Apps\PdfEdit",
            @"C:\Apps\PdfEdit\PdfEdit.exe", relaunch: true, elevated: false, @"C:\Temp\log.txt");
        Assert.Contains(@"$zip = 'C:\Users\O''Neil\Downloads\u.zip'", script);
        Assert.Contains("$relaunch = $true", script);
        Assert.Contains("robocopy", script);

        var later = UpdateInstaller.BuildZipScript("z", "d", "e", relaunch: false, elevated: false, "l");
        Assert.Contains("$relaunch = $false", later);
    }

    [Fact]
    public void Clean_install_script_uninstalls_then_installs_into_the_same_folder()
    {
        var script = UpdateInstaller.BuildCleanInstallScript(@"C:\Dl\PdfEditSetup-1.3.0.exe", @"C:\Program Files\PdfEdit",
            @"C:\Program Files\PdfEdit\PdfEdit.exe", relaunch: true, @"C:\Temp\log.txt");
        int uninstall = script.IndexOf("unins000.exe", StringComparison.Ordinal);
        int install = script.IndexOf("'/DIR=\"' + $dest + '\"'", StringComparison.Ordinal);
        Assert.True(uninstall > 0 && install > uninstall);
        Assert.Contains("$elevated = $true", script);   // relaunched through Explorer, as the normal user
    }
}
