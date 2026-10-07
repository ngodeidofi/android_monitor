; Script generated for PhoneMonitor Windows Installer
; Installs PhoneMonitor Server, ADB tools, and IddCx Virtual Secondary Monitor Driver

#define MyAppName "PhoneMonitor"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "PhoneMonitor"
#define MyAppExeName "PhoneMonitor.Server.exe"

[Setup]
AppId={{E68A0B2C-1E4D-4F16-B0E7-9D8B2C4F1A2B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=output
OutputBaseFilename=PhoneMonitor-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "installdriver"; Description: "Install and activate IddCx Virtual Secondary Display Driver (Recommended)"; GroupDescription: "Display Driver Setup:"

[Files]
Source: "payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\Driver\vdd_settings.xml"; DestDir: "C:\IddSampleDriver"; Flags: ignoreversion uninsneveruninstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Copy configuration and install the Virtual Display Driver locally
Filename: "{app}\Driver\devcon.exe"; Parameters: "install ""{app}\Driver\MttVDD.inf"" Root\MttVDD"; StatusMsg: "Installing and activating Virtual Secondary Display Driver locally..."; Tasks: installdriver; Flags: runhidden
; Add Windows Firewall rules for video (59100) and touch (59101) streaming
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""PhoneMonitor Video"" dir=in action=allow protocol=TCP localport=59100"; StatusMsg: "Configuring Windows Firewall rules..."; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""PhoneMonitor Touch"" dir=in action=allow protocol=TCP localport=59101"; StatusMsg: "Configuring Windows Firewall rules..."; Flags: runhidden
; Launch application after setup completes
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove virtual display driver device node from Windows Device Manager
Filename: "{app}\Driver\devcon.exe"; Parameters: "remove ""Root\MttVDD"""; Flags: runhidden
; Restore Windows display topology to PC screen only
Filename: "{sys}\DisplaySwitch.exe"; Parameters: "/internal"; Flags: runhidden
; Remove Windows Firewall rules
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""PhoneMonitor Video"""; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""PhoneMonitor Touch"""; Flags: runhidden

[UninstallDelete]
Type: filesandordirs; Name: "C:\IddSampleDriver"
Type: filesandordirs; Name: "{app}"
