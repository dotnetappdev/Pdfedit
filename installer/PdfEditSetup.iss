; PdfEdit Inno Setup Script
; Produces an EXE installer that installs PdfEdit to {autopf}\PdfEdit with Start Menu and optional
; Desktop shortcuts.
;
; Two flavours:
;   - Self-contained (what GitHub Actions releases):  .NET is bundled, nothing else to install.
;       dotnet publish PdfEdit\PdfEdit.csproj -c Release -r win-x64 --self-contained true -o publish_portable
;       iscc /DSELF_CONTAINED=1 /DAppSource=..\publish_portable installer\PdfEditSetup.iss
;   - Framework-dependent (smaller): needs the .NET 10 Desktop Runtime; setup offers the download page.
;       dotnet publish PdfEdit\PdfEdit.csproj -c Release -r win-x64 --self-contained false -o publish
;       iscc installer\PdfEditSetup.iss
;
; Prerequisite: Inno Setup 6.3+ (https://jrsoftware.org/isinfo.php)

#define MyAppName      "PdfEdit"
; Overridden by CI with /DMyAppVersion=x.y.z (a plain #define would win over the command line)
#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif
#define MyAppPublisher "PdfEdit"
#define MyAppURL       "https://github.com/dotnetappdev/pdfedit"
#define MyAppExeName   "PdfEdit.exe"
#define MyAppId        "{A3F2C8E1-4B7D-4E9A-8C3F-1D5E7A9B2C4E}"

; Folder with the published app, relative to this script (override with /DAppSource=...).
#ifndef AppSource
  #define AppSource "..\publish"
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
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Require elevation so the app writes to Program Files
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
OutputDir=..\dist
OutputBaseFilename=PdfEditSetup-{#MyAppVersion}
SetupIconFile=..\PdfEdit\Resources\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardResizable=yes
DisableWelcomePage=no
LicenseFile=..\LICENSE
; Digital signing — fill in when you have a code-signing cert:
; SignTool=signtool
; SignedUninstaller=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.19041
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";  Description: "{cm:CreateDesktopIcon}";     GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunch"; Description: "Pin to taskbar after install"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main application — build output from: dotnet publish -c Release -r win-x64 --self-contained false
; The publish path is relative to the ISS file location (installer\), so ..\ points to repo root.
; CI passes /O for output dir; the publish folder is always at repo-root\publish\.
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; The bundled Tesseract OCR engine (x64\tesseract50.dll, x64\leptonica-1.82.0.dll) needs the
; Microsoft Visual C++ 2015-2022 x64 runtime. Most PCs have it; to ship it, download
; vc_redist.x64.exe next to this script and uncomment:
; Source: "vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
; and in [Run]: Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing Visual C++ runtime..."

[Icons]
Name: "{group}\{#MyAppName}";                  Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}";          Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Launch the app after install
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Registry]
; App path so "PdfEdit" works from Run dialog
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; \
    ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; \
    ValueType: string; ValueName: "Path"; ValueData: "{app}"

; File association: .pdf opens with PdfEdit (secondary handler — does not override the default)
Root: HKCU; Subkey: "SOFTWARE\Classes\PdfEdit.Document"; ValueType: string; \
    ValueName: ""; ValueData: "PDF Document"; Flags: uninsdeletekey
Root: HKCU; Subkey: "SOFTWARE\Classes\PdfEdit.Document\DefaultIcon"; ValueType: string; \
    ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKCU; Subkey: "SOFTWARE\Classes\PdfEdit.Document\shell\open\command"; ValueType: string; \
    ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Code]
#ifndef SELF_CONTAINED
const
  DotNetMajor = 10;
  DotNetDownloadUrl = 'https://dotnet.microsoft.com/download/dotnet/10.0';

// True when a .NET Desktop Runtime 10 or later (x64) is installed.
function IsDotNetInstalled: Boolean;
var
  Names: TArrayOfString;
  i: Integer;
begin
  Result := False;
  if not RegGetValueNames(HKEY_LOCAL_MACHINE,
       'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    Exit;
  for i := 0 to GetArrayLength(Names) - 1 do
    if StrToIntDef(Copy(Names[i], 1, Pos('.', Names[i]) - 1), 0) >= DotNetMajor then
    begin
      Result := True;
      Exit;
    end;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNetInstalled then
    if MsgBox('PdfEdit needs the .NET 10 Desktop Runtime (x64), which was not found.' + #13#10#13#10 +
              'Open the download page now? Install "Desktop Runtime x64", then PdfEdit will start.' + #13#10 +
              '(Tip: the self-contained installer or portable ZIP from the Releases page needs nothing extra.)',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', DotNetDownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;
#endif
