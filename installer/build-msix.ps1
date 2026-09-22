#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds an MSIX package for PdfEdit.

.DESCRIPTION
    1. Publishes the .NET app (self-contained, win-x64)
    2. Copies assets and the appxmanifest into a staging folder
    3. Generates placeholder tile images if none exist
    4. Calls makeappx.exe to produce the .msix
    5. Signs the package (test cert or real cert via $CertThumbprint)

.PARAMETER Configuration
    Build configuration (default: Release)

.PARAMETER Version
    Package version, e.g. "1.0.1.0" (default: reads from csproj)

.PARAMETER CertThumbprint
    SHA-1 thumbprint of a code-signing certificate already in the certificate store.
    When omitted, a self-signed test cert is created automatically.

.PARAMETER OutputDir
    Output directory for the .msix file (default: .\dist)

.EXAMPLE
    # Quick test build
    pwsh installer\build-msix.ps1

    # Signed release build
    pwsh installer\build-msix.ps1 -CertThumbprint "AB12CD..."
#>
[CmdletBinding()]
param(
    [string] $Configuration   = "Release",
    [string] $Version         = "",
    [string] $CertThumbprint  = "",
    [string] $OutputDir       = "dist"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Locate tools ─────────────────────────────────────────────────────────────

function Find-Tool([string]$Name) {
    # Check PATH first
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    # Search Windows SDK versions newest first
    $sdkRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    if (Test-Path $sdkRoot) {
        $exe = Get-ChildItem "$sdkRoot\*\x64\$Name" -ErrorAction SilentlyContinue |
               Sort-Object { [version]($_.Directory.Parent.Name) } -Descending |
               Select-Object -First 1
        if ($exe) { return $exe.FullName }
    }
    throw "Cannot find '$Name'. Install the Windows SDK or add it to PATH."
}

$makeappx = Find-Tool "makeappx.exe"
$signtool  = Find-Tool "signtool.exe"
Write-Host "makeappx : $makeappx"
Write-Host "signtool : $signtool"

# ── Resolve paths ────────────────────────────────────────────────────────────

$root      = Resolve-Path "$PSScriptRoot\.."
$csproj    = "$root\PdfEdit\PdfEdit.csproj"
$manifest  = "$PSScriptRoot\Package.appxmanifest"
$publishDir = "$root\publish"
$stageDir  = "$root\_msix_stage"
$distDir   = "$root\$OutputDir"

if (-not $Version) {
    [xml]$proj = Get-Content $csproj
    $Version = ($proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1) + ".0"
    if ($Version -eq ".0") { $Version = "1.0.0.0" }
}
Write-Host "Version  : $Version"

# ── Publish the app ──────────────────────────────────────────────────────────

Write-Host "`n==> dotnet publish"
& dotnet publish $csproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $publishDir `
    /p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# ── Stage package layout ─────────────────────────────────────────────────────

Write-Host "`n==> Staging layout"
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory $stageDir | Out-Null

# Copy app files
Copy-Item "$publishDir\*" $stageDir -Recurse -Force

# Patch version in manifest and copy
$manifestText = Get-Content $manifest -Raw
$manifestText = $manifestText -replace 'Version="[\d.]+"', "Version=`"$Version`""
Set-Content "$stageDir\AppxManifest.xml" $manifestText

# ── Generate placeholder tile assets ─────────────────────────────────────────

$assetsDir = "$stageDir\Assets"
if (-not (Test-Path $assetsDir)) { New-Item -ItemType Directory $assetsDir | Out-Null }

$tiles = @{
    "Square44x44Logo.png"   = @(44,  44)
    "Square150x150Logo.png" = @(150, 150)
    "Wide310x150Logo.png"   = @(310, 150)
    "Square310x310Logo.png" = @(310, 310)
    "StoreLogo.png"         = @(50,  50)
    "SplashScreen.png"      = @(620, 300)
}

# Try to use .NET System.Drawing to generate real tiles
Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue
foreach ($tile in $tiles.GetEnumerator()) {
    $dest = "$assetsDir\$($tile.Key)"
    # Copy from project assets if they exist
    $src = "$root\PdfEdit\Resources\$($tile.Key)"
    if (Test-Path $src) {
        Copy-Item $src $dest -Force
        continue
    }
    # Generate a simple colored placeholder
    try {
        $w, $h = $tile.Value
        $bmp = New-Object System.Drawing.Bitmap($w, $h)
        $g   = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::FromArgb(40, 80, 160))
        $br  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $fnt = New-Object System.Drawing.Font("Segoe UI", [Math]::Max(8, $w/8), [System.Drawing.FontStyle]::Bold)
        $sf  = New-Object System.Drawing.StringFormat
        $sf.Alignment = [System.Drawing.StringAlignment]::Center
        $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
        $g.DrawString("PDF", $fnt, $br, [System.Drawing.RectangleF]::new(0,0,$w,$h), $sf)
        $bmp.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
    } catch {
        # Fallback: write a 1×1 transparent PNG header
        [IO.File]::WriteAllBytes($dest, [byte[]]@(
            0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
            0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
            0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
            0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,
            0x89,0x00,0x00,0x00,0x0A,0x49,0x44,0x41,
            0x54,0x78,0x9C,0x62,0x00,0x00,0x00,0x02,
            0x00,0x01,0xE2,0x21,0xBC,0x33,0x00,0x00,
            0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,0x42,
            0x60,0x82))
    }
    Write-Host "  Generated placeholder: $($tile.Key)"
}

# ── Build MSIX ───────────────────────────────────────────────────────────────

Write-Host "`n==> makeappx pack"
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory $distDir | Out-Null }

$msixPath = "$distDir\PdfEdit-$Version.msix"
& $makeappx pack /d $stageDir /p $msixPath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }
Write-Host "Created  : $msixPath"

# ── Sign ─────────────────────────────────────────────────────────────────────

Write-Host "`n==> Signing"
if ($CertThumbprint) {
    & $signtool sign /fd SHA256 /sha1 $CertThumbprint /tr http://timestamp.sectigo.com /td SHA256 $msixPath
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
    Write-Host "Signed with certificate: $CertThumbprint"
} else {
    # Create a self-signed test certificate (not trusted by Windows unless imported)
    $certSubject = "CN=PdfEdit"
    $existing = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1
    if (-not $existing) {
        Write-Host "  Creating self-signed test certificate..."
        $existing = New-SelfSignedCertificate `
            -Subject $certSubject `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -KeyUsage DigitalSignature `
            -Type CodeSigningCert `
            -HashAlgorithm SHA256 `
            -NotAfter (Get-Date).AddYears(1)
    }
    & $signtool sign /fd SHA256 /sha1 $existing.Thumbprint $msixPath
    if ($LASTEXITCODE -ne 0) { throw "signtool sign failed" }
    Write-Host "Self-signed (test only). Import the cert to trust on this machine:"
    Write-Host "  Import-PfxCertificate -FilePath <exported.pfx> -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
}

# ── Cleanup ──────────────────────────────────────────────────────────────────

Remove-Item $stageDir -Recurse -Force
Write-Host "`n==> Done"
Write-Host "Package  : $msixPath"
Write-Host "Size     : $([Math]::Round((Get-Item $msixPath).Length / 1MB, 1)) MB"
