; Inno Setup script for the WorriorVex Windows installer.
;
;   iscc /DAppVersion=0.1.0 /DSourceDir=C:\path\to\publish /DOutputDir=C:\path\to\dist worriorvex.iss
;
; Installs for the current user by default (no administrator rights needed).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

[Setup]
AppId={{8E6C3B52-6C1F-4B7B-9E55-3F2B6C0E6B11}
AppName=WorriorVex
AppVersion={#AppVersion}
AppVerName=WorriorVex {#AppVersion}
AppPublisher=Musa Consulting
AppPublisherURL=https://www.cloudworrior.com
AppSupportURL=https://github.com/cloudworrior-labs/worriorvex/issues
AppUpdatesURL=https://github.com/cloudworrior-labs/worriorvex/releases
AppCopyright=(c) 2026 Musa Consulting
DefaultDirName={autopf}\WorriorVex
DefaultGroupName=WorriorVex
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\..\LICENSE
SetupIconFile=..\..\assets\icon\worriorvex.ico
UninstallDisplayIcon={app}\WorriorVex.exe
OutputDir={#OutputDir}
OutputBaseFilename=WorriorVex-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\WorriorVex"; Filename: "{app}\WorriorVex.exe"
Name: "{autodesktop}\WorriorVex"; Filename: "{app}\WorriorVex.exe"; Tasks: desktopicon

[Registry]
; "Open with WorriorVex" for packages exported from another computer.
Root: HKA; Subkey: "Software\Classes\.worriorvex"; ValueType: string; ValueName: ""; ValueData: "WorriorVex.Package"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\WorriorVex.Package"; ValueType: string; ValueName: ""; ValueData: "WorriorVex package"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\WorriorVex.Package\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\WorriorVex.exe,0"
Root: HKA; Subkey: "Software\Classes\WorriorVex.Package\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\WorriorVex.exe"" ""%1"""

[Run]
Filename: "{app}\WorriorVex.exe"; Description: "{cm:LaunchProgram,WorriorVex}"; Flags: nowait postinstall skipifsilent

[Code]
// Notes are kept in %LOCALAPPDATA%\WorriorVex and are never touched by setup or uninstall.

const
  WebView2ClientKey = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function HasWebView2Runtime(): Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\' + WebView2ClientKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM, 'SOFTWARE\' + WebView2ClientKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'Software\' + WebView2ClientKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not HasWebView2Runtime()) and (not WizardSilent()) then
    MsgBox('WorriorVex needs the Microsoft Edge WebView2 Runtime, which was not found on this computer.' + #13#10 + #13#10 +
           'It is part of Windows 11. On Windows 10, install the "Evergreen" runtime from' + #13#10 +
           'https://developer.microsoft.com/microsoft-edge/webview2/ and then start WorriorVex.',
           mbInformation, MB_OK);
end;
