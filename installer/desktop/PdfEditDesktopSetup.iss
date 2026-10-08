; PdfEdit Desktop (the cross-platform Avalonia app) — Windows installer.
; A separate product from the WPF app's installer (PdfEditSetup.iss): its own AppId, folder and
; Start menu entry, so both can be installed side by side. The program is PdfEdit.exe in both.
;
; Build (from the repo root):
;   dotnet publish PdfEdit.Avalonia -c Release -r win-x64 --self-contained true -o publish_desktop
;   iscc /DMyAppVersion=1.3.0 installer\desktop\PdfEditDesktopSetup.iss
; or simply:  pwsh installer\desktop\build-windows.ps1
;
; Needs Inno Setup 6.3+ (https://jrsoftware.org/isinfo.php).

#define MyAppName      "PdfEdit Desktop"
#ifndef MyAppVersion
  #define MyAppVersion "1.3.0"
#endif
#define MyAppPublisher "PdfEdit"
#define MyAppURL       "https://github.com/dotnetappdev/pdfedit"
#define MyAppExeName   "PdfEdit.exe"
; Not the WPF app's AppId: a different product, installed alongside it.
#define MyAppId        "{7C1E5B92-3D4A-4F60-9E21-B8A4C6D0F3E7}"

#ifndef AppSource
  #define AppSource "..\..\publish_desktop"
#endif

[Setup]
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\PdfEdit Desktop
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline dialog
OutputDir=..\..\dist
OutputBaseFilename=PdfEdit-Desktop-Setup-{#MyAppVersion}
SetupIconFile=..\..\PdfEdit.Avalonia\Assets\pdfedit.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\..\LICENSE
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
; Digital signing — fill in when you have a code-signing certificate:
; SignTool=signtool
; SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "pdfopenwith"; Description: "Add PdfEdit Desktop to ""Open with"" for PDF files"; GroupDescription: "File types:"

[Files]
; Self-contained publish of PdfEdit.Avalonia: .NET is included, plus the web app's page files (wwwroot).
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; "Open with" for PDFs (doesn't take over the default PDF app).
Root: HKA; Subkey: "Software\Classes\PdfEditDesktop.Document"; ValueType: string; ValueName: ""; ValueData: "PDF Document"; Flags: uninsdeletekey; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\PdfEditDesktop.Document\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\PdfEditDesktop.Document\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "PdfEditDesktop.Document"; ValueData: ""; Flags: uninsdeletevalue; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".pdf"; ValueData: ""; Tasks: pdfopenwith

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing the Microsoft Edge WebView2 runtime…"; Check: NeedsWebView2; Flags: waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// PdfEdit Desktop shows its pages in Microsoft Edge WebView2. Windows 11 always has it and most
// Windows 10 PCs do; when it's missing, setup downloads Microsoft's small installer and runs it.
const
  WebView2Key = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2KeyUser = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2Bootstrapper = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

var
  WebView2Missing: Boolean;

function HasWebView2: Boolean;
var
  Version: String;
begin
  Result := (RegQueryStringValue(HKLM, WebView2Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'))
         or (RegQueryStringValue(HKCU, WebView2KeyUser, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function NeedsWebView2: Boolean;
begin
  Result := WebView2Missing;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  WebView2Missing := not HasWebView2;
  if WebView2Missing then
  try
    DownloadTemporaryFile(WebView2Bootstrapper, 'MicrosoftEdgeWebview2Setup.exe', '', nil);
  except
    Result := 'PdfEdit Desktop needs the Microsoft Edge WebView2 runtime, which could not be downloaded. ' +
              'Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and run setup again.';
  end;
end;
