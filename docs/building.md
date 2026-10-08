# Building from source and releases

[← Back to README](../README.md)

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
window can use, and shows it in the system's own web view — WebView2 on Windows, WebKit on macOS,
WebKitGTK on Linux. The program is called **PdfEdit**, like the WPF app. It builds on any of the three:

```bash
dotnet run --project PdfEdit.Avalonia                 # run from source
dotnet run --project PdfEdit.Avalonia -- form.pdf     # and open a PDF
dotnet publish PdfEdit.Avalonia -c Release -r win-x64     # or osx-arm64, osx-x64, linux-x64
```

Publishing puts the web app's page files (`wwwroot`) next to the program. What it needs on each
system: Windows — the WebView2 runtime (part of Windows 10 and 11); macOS — nothing extra; Linux —
WebKitGTK (`sudo apt install libwebkit2gtk-4.1-0` on Ubuntu and Debian). Save and Save As use the
system's Save dialog, Print opens the PDF in your PDF viewer, and links open in your browser.
Signatures, stamps and settings are kept in your app data folder (`PdfEdit/pdfedit-desktop.db`).

To run the tests:

```powershell
dotnet test PdfEdit.Tests
```

## Building the installers locally

```powershell
pwsh installer\build-installer.ps1              # app, EXE installer and MSIX
pwsh installer\build-installer.ps1 -SkipMsix    # EXE installer only
```

The EXE needs [Inno Setup 6](https://jrsoftware.org/isinfo.php), and the MSIX needs the Windows SDK (`makeappx.exe`).

## How releases are published

GitHub Actions builds and publishes every release. There's nothing to do by hand.

- **From `devmain`**: when the `<Version>` in `PdfEdit/PdfEdit.csproj` hasn't been released yet, the next push builds it and publishes it as a release. To ship a new version, bump that number.
- **From `main`**: every push releases the next patch version automatically (for example 1.0.4 after 1.0.3).
- **From a tag**: pushing `v1.2.3` releases exactly that version.

Each release includes:

| File | What it is |
|------|------------|
| `PdfEditSetup-x.y.z.exe` | Installer, self-contained (.NET included) |
| `PdfEdit-x.y.z-win-x64-portable.zip` | Self-contained, unzip and run |
| `PdfEdit-x.y.z-win-x64.zip` | Smaller, needs the .NET 10 Desktop Runtime |
| `PdfEdit-x.y.z.0.msix` + `PdfEdit-TestCert.cer` | MSIX package with its test certificate |

The MSIX is signed with a self-signed certificate. To install it, first import `PdfEdit-TestCert.cer` into **Trusted People** from an administrator PowerShell:

```powershell
Import-Certificate -FilePath PdfEdit-TestCert.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

The workflows live in `.github/workflows`. `ci.yml` builds every push and pull request, and `release.yml` does the packaging.
