# Building from source and releases

[Back to README](../README.md)

## Running from source

You'll need Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), or Visual Studio 2022 17.12 or later.

```powershell
git clone https://github.com/dotnetappdev/Pdfedit.git
cd Pdfedit
dotnet build PdfEdit.sln -c Release
dotnet run --project PdfEdit
```

### The cross-platform desktop app (Windows, macOS, Linux)

`PdfEdit.Avalonia` is PdfEdit in an [Avalonia](https://avaloniaui.net) window: it runs the web
version (PdfEdit.Blazor) inside its own process, on a private address on `127.0.0.1` that only its
window can use, and shows it in the system's own web view: WebView2 on Windows, WebKit on macOS,
WebKitGTK on Linux. The program is called **PdfEdit**, like the WPF app. It builds on any of the three:

```bash
dotnet run --project PdfEdit.Avalonia                 # run from source
dotnet run --project PdfEdit.Avalonia -- form.pdf     # and open a PDF
dotnet publish PdfEdit.Avalonia -c Release -r win-x64     # or osx-arm64, osx-x64, linux-x64
```

Publishing puts the web app's page files (`wwwroot`) next to the program. What it needs on each
system: on Windows, the WebView2 runtime (part of Windows 10 and 11); on macOS, nothing extra; on Linux,
WebKitGTK (`sudo apt install libwebkit2gtk-4.1-0` on Ubuntu and Debian). Save and Save As use the
system's Save dialog, Print opens the PDF in your PDF viewer, and links open in your browser.
Signatures, stamps and settings are kept in your app data folder (`PdfEdit/pdfedit-desktop.db`).

A few things depend on what the system's web view offers: Share opens the system share sheet with
WebView2 and WebKit (Windows, macOS) and falls back to the Save dialog where there's none (Linux);
Dictate needs speech recognition (WebView2 and WebKit on macOS; not WebKitGTK); On Phone needs
PdfEdit on a server your phone can reach, so it isn't for the desktop app.

To run the tests:

```powershell
dotnet test PdfEdit.Tests
```

## Building the installers locally

```powershell
pwsh installer\build-installer.ps1 -SkipMsix    # EXE installer
```

The EXE needs [Inno Setup 6](https://jrsoftware.org/isinfo.php). (The script can still make an MSIX with the Windows SDK's `makeappx.exe`, but releases no longer include one.)

### PdfEdit Desktop packages (Mac and Linux)

The cross-platform app is released for Mac and Linux (Windows has the Windows app):

```bash
installer/desktop/build-mac.sh               # dist/PdfEdit-Desktop-<version>-mac-arm64.dmg and -mac-x64.dmg
installer/desktop/build-linux.sh             # dist/PdfEdit-Desktop-<version>-linux-x64.AppImage and .tar.gz
```

- **Linux** runs on any Linux machine with the .NET 10 SDK and `desktop-file-validate`
  (`desktop-file-utils`); it downloads `appimagetool` when it isn't installed. Both files include
  .NET; the app needs WebKitGTK 4.1 or 4.0 on the computer it runs on.
- **Windows** (`installer/desktop/build-windows.ps1`, Inno Setup 6.3+) still builds a PdfEdit
  Desktop installer and portable ZIP for trying the app on Windows, but releases don't include them.
- **Mac** needs a Mac with the .NET 10 SDK (the script builds `PdfEdit.app` for Apple silicon and
  Intel and packs each in a disk image; elsewhere it makes a `.tar.gz` of the app to check). It's
  signed ad hoc unless `MACOS_SIGN_IDENTITY` names a Developer ID certificate; with `APPLE_ID`,
  `APPLE_TEAM_ID` and `APPLE_APP_PASSWORD` set too it's notarized, so it opens without a warning.
  Signed ad hoc, the first launch needs right-click > **Open** (or System Settings > Privacy &
  Security > Open Anyway).

GitHub Actions builds both on every release (the `linux` and `mac` jobs in `release.yml`, next to the
`windows` job; a final `publish` job makes the release with all their files at once), and the **PdfEdit Desktop installers** workflow builds them on demand or whenever the
desktop app changes, as downloads on the run's page. For signed Mac builds, add the repository
secrets `MACOS_CERT_P12` (base64 of the .p12), `MACOS_CERT_PASSWORD`, `MACOS_SIGN_IDENTITY`,
`APPLE_ID`, `APPLE_TEAM_ID` and `APPLE_APP_PASSWORD`.

## How releases are published

GitHub Actions builds and publishes every release. There's nothing to do by hand.

- **From `devmain`**: when the `<Version>` in `Directory.Build.props` hasn't been released yet, the next push builds it and publishes it as a release. To ship a new version, bump that one number: every project and every file in the release (Windows, Mac and Linux) gets it.
- **From `main`**: every push releases the next patch version automatically (for example 1.0.4 after 1.0.3).
- **From a tag**: pushing `v1.2.3` releases exactly that version.

Each release includes:

| File | What it is |
|------|------------|
| `PdfEditSetup-x.y.z.exe` | Windows installer, self-contained (.NET included) |
| `PdfEdit-x.y.z-win-x64-portable.zip` | Windows, self-contained: unzip and run |
| `PdfEdit-Desktop-x.y.z-mac-arm64.dmg` / `-mac-x64.dmg` | Mac, Apple silicon / Intel |
| `PdfEdit-Desktop-x.y.z-linux-x64.AppImage` | Linux, one file: make it executable and run |
| `PdfEdit-Desktop-x.y.z-linux-x64.tar.gz` | Linux, the same app in a folder |

The workflows live in `.github/workflows`. `ci.yml` builds every push and pull request, and `release.yml` does the packaging.
