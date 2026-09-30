#ifndef PackageSource
  #error PackageSource is required
#endif
#ifndef ReleaseTag
  #error ReleaseTag is required
#endif
[Setup]
AppId={{EAACF1DA-2231-4E70-A901-30D01AEECA30}
AppName=PoteHunter
AppVersion={#Copy(ReleaseTag,8,100)}
AppVerName=PoteHunter {#ReleaseTag}
AppPublisher=theblusmurf
AppPublisherURL=https://github.com/theblusmurf/PoteHunter
DefaultDirName={localappdata}\Programs\PoteHunter
DefaultGroupName=PoteHunter
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#InstallerOutput}
OutputBaseFilename=PoteHunter-{#ReleaseTag}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
AppMutex=Local\PoteHunter.SingleInstance
UninstallDisplayIcon={app}\PoteHunter.exe
DisableProgramGroupPage=yes
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
[Files]
Source: "{#PackageSource}\*"; DestDir: "{app}"; Excludes: "settings.json"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageSource}\settings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
[Icons]
Name: "{autoprograms}\PoteHunter"; Filename: "{app}\PoteHunter.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\PoteHunter"; Filename: "{app}\PoteHunter.exe"; WorkingDir: "{app}"; Tasks: desktopicon
[Run]
Filename: "{app}\PoteHunter.exe"; Description: "Launch PoteHunter"; Flags: nowait postinstall skipifsilent
