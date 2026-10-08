#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the PdfEdit Desktop (Avalonia) Windows installer: dist\PdfEdit-Desktop-Setup-<version>.exe,
    plus a portable ZIP (dist\PdfEdit-Desktop-<version>-win-x64.zip).

.EXAMPLE
    pwsh installer\desktop\build-windows.ps1
    pwsh installer\desktop\build-windows.ps1 -Version 1.3.1
#>
[CmdletBinding()]
param(
    [string] $Version      = "",
    [string] $InnoCompiler = ""
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root    = Resolve-Path "$PSScriptRoot\..\.."
$csproj  = "$root\PdfEdit.Avalonia\PdfEdit.Avalonia.csproj"
$publish = "$root\publish_desktop"
$dist    = "$root\dist"

if (-not $Version) {
    [xml]$proj = Get-Content $csproj
    $Version = @($proj.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    if (-not $Version) { $Version = "1.0.0" }
}
Write-Host "=== PdfEdit Desktop for Windows  v$Version ===" -ForegroundColor Cyan

# 1. Self-contained publish: .NET and the web app's page files are included.
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& dotnet publish $csproj -c Release -r win-x64 --self-contained true -o $publish `
    /p:Version=$Version /p:DebugType=none /p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
if (-not (Test-Path "$publish\PdfEdit.exe")) { throw "PdfEdit.exe missing from the publish folder" }
if (-not (Test-Path "$publish\wwwroot")) { throw "wwwroot missing from the publish folder" }

New-Item -ItemType Directory -Force -Path $dist | Out-Null

# 2. Portable ZIP.
$zip = "$dist\PdfEdit-Desktop-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$publish\*" -DestinationPath $zip
Write-Host "Portable ZIP: $zip"

# 3. Installer.
if (-not $InnoCompiler) {
    $InnoCompiler = @("C:\Program Files (x86)\Inno Setup 6\ISCC.exe", "C:\Program Files\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $InnoCompiler) { $InnoCompiler = "ISCC.exe" }
}
& $InnoCompiler "$PSScriptRoot\PdfEditDesktopSetup.iss" "/DMyAppVersion=$Version" "/DAppSource=$publish"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
Write-Host "Installer: $dist\PdfEdit-Desktop-Setup-$Version.exe" -ForegroundColor Green
