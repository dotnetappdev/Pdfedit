; PdfEdit Inno Setup Script
; Produces a self-contained EXE installer that:
;   1. Detects .NET 10 Desktop Runtime (x64)
;   2. Downloads & silently installs it when missing
;   3. Installs PdfEdit to {autopf}\PdfEdit
;   4. Creates Start Menu and optional Desktop shortcuts
;
; Prerequisites:
;   - Inno Setup 6.3+            (https://jrsoftware.org/isinfo.php)
;   - Inno Download Plugin (IDP) (https://mitrichsoftware.wordpress.com/inno-setup-tools/inno-download-plugin/)
;     OR build with the bundled bootstrapper option (see DOTNET_EMBEDDED define)
;
; Build command (from repo root):
;   iscc installer\PdfEditSetup.iss
; Or with the CI script:
;   pwsh installer\build-installer.ps1

#define MyAppName      "PdfEdit"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "PdfEdit"
#define MyAppURL       "https://github.com/dotnetappdev/pdfedit"
#define MyAppExeName   "PdfEdit.exe"
#define MyAppId        "{A3F2C8E1-4B7D-4E9A-8C3F-1D5E7A9B2C4E}"

; Set DOTNET_EMBEDDED=1 when you bundle the .NET installer inside the package:
;   iscc /DDOTNET_EMBEDDED=1 installer\PdfEditSetup.iss
; Leave unset to download at install time (requires internet connection).
; #define DOTNET_EMBEDDED 1

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
; SetupIconFile=..\PdfEdit\Resources\app.ico   ; uncomment once app.ico is added to Resources\
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
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Uncomment to bundle .NET installer when building with /DDOTNET_EMBEDDED=1:
; Source: "dotnet-runtime-10-win-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not IsDotNetInstalled

#ifdef DOTNET_EMBEDDED
; Bundled .NET Desktop Runtime installer (download separately and place here)
Source: "dotnet-runtime-embedded.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
#endif

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
const
  // .NET 10 Desktop Runtime minimum version
  DotNetMajor   = 10;
  DotNetMinor   = 0;
  DotNetPatch   = 0;
  DotNetArch    = 'x64';
  DotNetDownloadUrl =
    'https://download.visualstudio.microsoft.com/download/pr/' +
    'dotnet-runtime-10.0-win-x64.exe';  // update to actual URL per release
  DotNetDownloadDesc = '.NET 10 Desktop Runtime (x64)';

// ── Helpers ──────────────────────────────────────────────────────────────────

function IsDotNetInstalled: Boolean;
var
  Key, SubKey: string;
  Names: TArrayOfString;
  i: Integer;
  Major, Minor, Patch: Integer;
  Parts: TStringList;
begin
  Result := False;
  Key := 'SOFTWARE\dotnet\Setup\InstalledVersions\' + DotNetArch + '\sharedfx\Microsoft.WindowsDesktop.App';
  if not RegGetValueNames(HKEY_LOCAL_MACHINE, Key, Names) then
    Exit;
  for i := 0 to GetArrayLength(Names) - 1 do
  begin
    // Name format is "x.y.z"
    Parts := TStringList.Create;
    try
      Parts.Delimiter := '.';
      Parts.StrictDelimiter := True;
      Parts.DelimitedText := Names[i];
      if Parts.Count >= 3 then
      begin
        Major := StrToIntDef(Parts[0], 0);
        Minor := StrToIntDef(Parts[1], 0);
        Patch := StrToIntDef(Parts[2], 0);
        if (Major > DotNetMajor) or
           ((Major = DotNetMajor) and (Minor > DotNetMinor)) or
           ((Major = DotNetMajor) and (Minor = DotNetMinor) and (Patch >= DotNetPatch)) then
        begin
          Result := True;
          Break;
        end;
      end;
    finally
      Parts.Free;
    end;
  end;
end;

// ── IDP (download plugin) integration ─────────────────────────────────────
// IDP functions are declared here so the script compiles without the plugin
// present; the actual calls only happen at runtime when IDP is loaded.
#ifndef DOTNET_EMBEDDED
procedure IDPAddFile(Url, Filename: String); external 'idpAddFile@files:idp.dll stdcall delayload';
procedure IDPDownloadAfter(Page: Integer); external 'idpDownloadAfter@files:idp.dll stdcall delayload';
#endif

procedure InitializeWizard;
begin
#ifndef DOTNET_EMBEDDED
  if not IsDotNetInstalled then
  begin
    IDPAddFile(DotNetDownloadUrl, ExpandConstant('{tmp}\dotnet-runtime.exe'));
    IDPDownloadAfter(wpReady);
  end;
#endif
end;

function InstallDotNet: Boolean;
var
  ResultCode: Integer;
  InstallerPath: string;
begin
  Result := True;
#ifdef DOTNET_EMBEDDED
  InstallerPath := ExpandConstant('{tmp}\dotnet-runtime-embedded.exe');
#else
  InstallerPath := ExpandConstant('{tmp}\dotnet-runtime.exe');
#endif
  if not FileExists(InstallerPath) then Exit;

  if not Exec(InstallerPath, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Failed to launch the .NET installer. Please install .NET 10 Desktop Runtime manually from: https://dotnet.microsoft.com/download', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  if (ResultCode <> 0) and (ResultCode <> 3010) then  // 3010 = reboot required
  begin
    MsgBox(Format('.NET installer exited with code %d. You may need to install .NET 10 manually.', [ResultCode]), mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not IsDotNetInstalled then
  begin
    if not InstallDotNet then
      Result := 'Could not install the required .NET 10 Desktop Runtime. Please install it manually and re-run this setup.';
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not IsDotNetInstalled then
  begin
    if MsgBox('.NET 10 Desktop Runtime was not found on this machine.' + #13#10 +
              'The installer will download and install it automatically.' + #13#10#13#10 +
              'An internet connection is required. Continue?',
              mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
