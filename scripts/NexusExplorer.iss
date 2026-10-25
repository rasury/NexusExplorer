#ifndef Payload
  #error Payload is required
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif
[Setup]
AppId={{F47D2E21-63F4-44CB-9B91-6CEB32B0459B}
AppName=NexusExplorer
AppVersion={#AppVersion}
AppPublisher=rasury
AppPublisherURL=https://github.com/rasury/NexusExplorer
DefaultDirName={localappdata}\Programs\NexusExplorer
DefaultGroupName=NexusExplorer
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#Output}
OutputBaseFilename=NexusExplorer-{#AppVersion}-win-x64-Setup
SetupIconFile=..\src\NexusExplorer\Assets\AppIcon.ico
UninstallDisplayIcon={app}\NexusExplorer.exe
LicenseFile=..\LICENSE
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Excludes: "appsettings.json"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{group}\NexusExplorer"; Filename: "{app}\NexusExplorer.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\NexusExplorer"; Filename: "{app}\NexusExplorer.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\NexusExplorer.exe"; Description: "Launch NexusExplorer"; Flags: nowait postinstall skipifsilent

; Runtime-created data/Storage/cache/logs are not installer-owned and survive uninstall.
; Never add wildcard UninstallDelete entries.
