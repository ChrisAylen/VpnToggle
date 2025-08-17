; ===== VpnToggle Inno Setup script =====
; Save as: VpnToggleInstaller.iss (repo root)

#define MyAppName      "VpnToggle"
#define MyCompany      "Your Company"
#define MyExeName      "VpnToggle.exe"
#define MyAppId        "{{C6B3F9E4-6D7B-4D26-8E6E-5D9E18E7A9F0}}"  ; generate once and keep stable
#define PubX64         "publish\win-x64"                           ; repo-root publish folder
#define ProjectIcon    "VpnToggle\vpn_on.ico"
#define DisplayIcon    PubX64 + "\VpnToggle.exe"
#define MyVersion      GetCmdParam("MyVersion", "1.0.0")

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyVersion}
AppVerName={#MyAppName} {#MyVersion}
AppPublisher={#MyCompany}
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=VpnToggle-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
SetupIconFile={#ProjectIcon}
UninstallDisplayIcon={#DisplayIcon}

[Files]
; Install the published single-file exe and icons
Source: "{#PubX64}\{#MyExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "VpnToggle\vpn_on.ico";   DestDir: "{app}"; Flags: ignoreversion
Source: "VpnToggle\vpn_off.ico";  DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyExeName}"; WorkingDir: "{app}"

[Tasks]
; Checked by default, user can untick; state is remembered
Name: "autostart"; Description: "Run at Windows sign in (elevated)"; Flags: checkedonce
Name: "runnow";    Description: "Launch VpnToggle now (elevated)";   Flags: checkedonce

[Run]
; 1) Create scheduled task (logon, highest, 20s delay)
Filename: "schtasks.exe"; \
  Parameters: "/Create /TN ""{#MyAppName}"" /TR ""\""{app}\{#MyExeName}\"""" /SC ONLOGON /RL HIGHEST /DELAY 0000:20 /F"; \
  Flags: runhidden; \
  StatusMsg: "Creating elevated start-up task..."; \
  Tasks: autostart

; 2) Add working dir + 'Run only if network available'
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$wd = '{app}'; $exe = Join-Path $wd '{#MyExeName}'; $a = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $wd; $t = Get-ScheduledTask -TaskName '{#MyAppName}'; $s = $t.Settings; $s.RunOnlyIfNetworkAvailable = $true; Set-ScheduledTask -TaskName '{#MyAppName}' -Action $a -Settings $s"""; \
  Flags: runhidden; \
  Tasks: autostart

; 3) Run the elevated task once now (so first run is elevated too)
Filename: "schtasks.exe"; \
  Parameters: "/Run /TN ""{#MyAppName}"""; \
  Flags: runhidden; \
  StatusMsg: "Starting VpnToggle (elevated)..."; \
  Tasks: runnow

[UninstallRun]
; Remove the scheduled task on uninstall
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""{#MyAppName}"" /F"; Flags: runhidden; RunOnceId: "RemoveVpnToggleTask"


