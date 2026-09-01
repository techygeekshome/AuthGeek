; AuthGeek installer
;
; Same shape as installer\CleanGeek.iss and installer\PDFGeek.iss. Two things here are
; decisions rather than defaults, and both are explained where they appear: PrivilegesRequired,
; and what the uninstaller does and does not remove.
;
; Build it locally with:  build.cmd installer
; CI builds it in .github\workflows\release.yml.

#define AppName        "AuthGeek"
#define AppSourceDir   "..\publish\app"
#define AppExeName     "AuthGeek.exe"
#define AppPublisher   "TechyGeeksHome"
#define AppURL         "https://techygeekshome.info/authgeek/"
#define AppSupportURL  "https://github.com/techygeekshome/AuthGeek/issues"
#define AppUpdatesURL  "https://github.com/techygeekshome/AuthGeek/releases"
#define FirstYear      "2026"
#define CurrentYear    GetDateTimeString('yyyy', '', '')

; Read straight off the executable that is about to be packaged, so the installer can never
; claim a different version from the thing inside it.
#define AppVersion GetVersionNumbersString(AppSourceDir + "\" + AppExeName)

[Setup]
; NEVER regenerate this. Windows uses the AppId to tell an upgrade from a second parallel
; install; a new one means the next version installs alongside this one instead of over it.
AppId={{9D5E4A17-72C3-4E80-B6F1-8C42D07A3B95}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppSupportURL}
AppUpdatesURL={#AppUpdatesURL}
AppCopyright=Copyright (C) {#FirstYear}-{#CurrentYear} {#AppPublisher}

VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup

WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
LicenseFile=..\LICENSE
SetupIconFile=..\icons\authgeek.ico

OutputDir=..\dist
OutputBaseFilename={#AppName}Setup

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
AllowNoIcons=yes

; The app's own manifest is asInvoker - it reads and writes one file under the user's own
; profile, and there is nothing in that which needs administrator rights. Installing it somewhere
; only an administrator can write would be pretending otherwise, so this is a per-user install
; with no UAC prompt. Anyone who wants it machine-wide can pass /ALLUSERS.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog

Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.CreateDesktopShortcut=Create a &desktop shortcut
english.LaunchApp=Open {#AppName}
english.WebSite={#AppName} on the web

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopShortcut}"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#AppSourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE";   DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "README.md";   Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";                       Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:WebSite}";                     Filename: "{#AppURL}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";                 Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; THE VAULT IS NEVER TOUCHED. It holds two-factor secrets that exist nowhere else, and an
; uninstaller that deleted them would lock somebody out of every account they have, silently,
; for having pressed Uninstall. Only the folders go, and only if they are already empty.
Type: dirifempty; Name: "{localappdata}\TechyGeeksHome\AuthGeek"
Type: dirifempty; Name: "{localappdata}\TechyGeeksHome"
