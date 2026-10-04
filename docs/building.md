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
