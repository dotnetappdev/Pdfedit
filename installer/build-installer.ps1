#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Full build pipeline: publish → Inno Setup EXE → MSIX package.

.DESCRIPTION
    Runs in order:
      1. dotnet publish (Release, win-x64, framework-dependent)
      2. Inno Setup (produces dist\PdfEditSetup-<version>.exe)
      3. build-msix.ps1  (produces dist\PdfEdit-<version>.msix)

    Any step can be skipped with the -Skip* switches.

.PARAMETER Version
    Override the version string (e.g. "1.2.0").  Reads from csproj if omitted.

.PARAMETER SkipPublish
    Skip dotnet publish (use existing .\publish\ folder).

.PARAMETER SkipInno
    Skip building the EXE installer.

.PARAMETER SkipMsix
    Skip building the MSIX package.

.PARAMETER InnoCompiler
    Path to ISCC.exe (Inno Setup compiler).  Searched in PATH and default install locations if omitted.

.PARAMETER CertThumbprint
    SHA-1 thumbprint for code-signing.  Self-signed test cert used when omitted.

.EXAMPLE
    # Full release build (both installers)
    pwsh installer\build-installer.ps1

    # EXE only, signed
    pwsh installer\build-installer.ps1 -SkipMsix -CertThumbprint "AB12..."

    # Quick MSIX rebuild (skip publish + EXE)
    pwsh installer\build-installer.ps1 -SkipPublish -SkipInno
#>
[CmdletBinding()]
param(
    [string] $Version         = "",
    [switch] $SkipPublish,
    [switch] $SkipInno,
    [switch] $SkipMsix,
    [string] $InnoCompiler    = "",
    [string] $CertThumbprint  = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root    = Resolve-Path "$PSScriptRoot\.."
$csproj  = "$root\PdfEdit\PdfEdit.csproj"
$distDir = "$root\dist"

# ── Read version from csproj if not provided ─────────────────────────────────
if (-not $Version) {
    [xml]$proj = Get-Content $csproj
    $Version = $proj.Project.PropertyGroup.Version |
               Where-Object { $_ } |
               Select-Object -First 1
    if (-not $Version) { $Version = "1.0.0" }
}
Write-Host "=== PdfEdit Build Pipeline  v$Version ===" -ForegroundColor Cyan

# ── Step 1: dotnet publish ────────────────────────────────────────────────────
if (-not $SkipPublish) {
    Write-Host "`n[1/3] dotnet publish" -ForegroundColor Yellow
    $publishDir = "$root\publish"
    & dotnet publish $csproj `
        -c Release `
        -r win-x64 `
        --self-contained false `
        -o $publishDir `
        /p:Version=$Version `
        /p:PublishSingleFile=false `
        /p:DebugType=none `
        /p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
    Write-Host "  Published to: $publishDir" -ForegroundColor Green
} else {
    Write-Host "`n[1/3] dotnet publish — SKIPPED" -ForegroundColor DarkGray
}

# ── Step 2: Inno Setup EXE ───────────────────────────────────────────────────
if (-not $SkipInno) {
    Write-Host "`n[2/3] Inno Setup EXE installer" -ForegroundColor Yellow

    # Locate ISCC.exe
    if (-not $InnoCompiler) {
        $candidates = @(
            "ISCC.exe",  # PATH
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
        )
        foreach ($c in $candidates) {
            $cmd = Get-Command $c -ErrorAction SilentlyContinue
            if ($cmd) { $InnoCompiler = $cmd.Source; break }
            if (Test-Path $c) { $InnoCompiler = $c; break }
        }
    }
    if (-not $InnoCompiler) {
        Write-Warning "ISCC.exe not found. Skipping EXE installer. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php"
    } else {
        if (-not (Test-Path $distDir)) { New-Item -ItemType Directory $distDir | Out-Null }
        & $InnoCompiler `
            "$PSScriptRoot\PdfEditSetup.iss" `
            /DMyAppVersion=$Version `
            /O"$distDir"
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup compiler failed" }
        $exePath = "$distDir\PdfEditSetup-$Version.exe"
        Write-Host "  EXE installer: $exePath" -ForegroundColor Green
        # Sign if cert provided
        if ($CertThumbprint) {
            $st = Find-Tool "signtool.exe" -ErrorAction SilentlyContinue
            if ($st) {
                & $st sign /fd SHA256 /sha1 $CertThumbprint /tr http://timestamp.sectigo.com /td SHA256 $exePath
                Write-Host "  Signed EXE." -ForegroundColor Green
            }
        }
    }
} else {
    Write-Host "`n[2/3] Inno Setup — SKIPPED" -ForegroundColor DarkGray
}

# ── Step 3: MSIX ─────────────────────────────────────────────────────────────
if (-not $SkipMsix) {
    Write-Host "`n[3/3] MSIX package" -ForegroundColor Yellow
    $msixArgs = @("-Version", $Version, "-OutputDir", "dist")
    if ($SkipPublish) { $msixArgs += "-SkipPublish" }  # re-use existing publish output
    if ($CertThumbprint) { $msixArgs += @("-CertThumbprint", $CertThumbprint) }
    & pwsh "$PSScriptRoot\build-msix.ps1" @msixArgs
    if ($LASTEXITCODE -ne 0) { throw "build-msix.ps1 failed" }
} else {
    Write-Host "`n[3/3] MSIX — SKIPPED" -ForegroundColor DarkGray
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host "`n=== Build complete ===" -ForegroundColor Cyan
Get-ChildItem $distDir -ErrorAction SilentlyContinue | ForEach-Object {
    $kb = [Math]::Round($_.Length / 1KB)
    Write-Host "  $($_.Name)  ($kb KB)"
}
