#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif

#ifndef OutputSuffix
  #define OutputSuffix "-unsigned"
#endif

#ifndef PayloadDirectory
  #define PayloadDirectory "..\..\artifacts\installer\work\win-x64"
#endif

#define AppName "PredatorLite"
#define AppExe "PredatorLite.exe"

[Setup]
AppId={{C45ADA29-884C-471B-BBE4-7EC74A6E151C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=XYZ1024-alt
AppPublisherURL=https://github.com/XYZ1024-alt/PredatorLite
AppSupportURL=https://github.com/XYZ1024-alt/PredatorLite/issues
AppUpdatesURL=https://github.com/XYZ1024-alt/PredatorLite/releases
AppContact=https://github.com/XYZ1024-alt/PredatorLite/security

DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}

ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.26100
PrivilegesRequired=lowest

OutputDir=..\..\artifacts\installer\unsigned
OutputBaseFilename=PredatorLite-Setup-{#AppVersion}-win-x64{#OutputSuffix}
SetupIconFile=..\..\src\PredatorLite.App\Assets\PredatorLiteFluent.ico
LicenseFile=..\..\LICENSE
InfoBeforeFile=prerequisites.txt
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=PredatorLite contributors
VersionInfoDescription=PredatorLite Installer
VersionInfoCopyright=Copyright (c) 2026 PredatorLite contributors

WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=PredatorLite.exe,PredatorLite.FanGuard.exe,PredatorLite.ElevatedHelper.exe
RestartApplications=no

#ifdef SignInstaller
SignTool=PredatorLiteSign
SignToolMinimumTimeBetween=1000
SignToolRetryCount=3
SignToolRetryDelay=2000
SignedUninstaller=yes
#endif

[Files]
Source: "{#PayloadDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; When a release removes or renames a payload file, add an exact [InstallDelete]
; entry for that obsolete path. Avoid broad wildcards under {app}.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PredatorLite');
end;
