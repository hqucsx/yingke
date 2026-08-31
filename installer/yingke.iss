; 映刻 for Windows 安装器脚本（Inno Setup 6.3+，含简体中文语言包）

#define MyAppName "映刻"
#define MyAppVersion "0.1.1"
#define MyAppExeName "YingKe.exe"

[Setup]
AppId={{7A1C3E5D-9B2F-4C8A-A6D4-E5F7B8C9D0E1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=映刻
DefaultDirName={localappdata}\Programs\YingKe
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=output
OutputBaseFilename=YingKe-setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableDirPage=no
DisableProgramGroupPage=no
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\YingKe.App\yingke.ico

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "..\dist\YingKe.exe"; DestDir: "{app}"; DestName: "YingKe.exe"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\映刻"; Filename: "{app}\{#MyAppExeName}"; AppUserModelID: "YingKe.App"
Name: "{autodesktop}\映刻"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; AppUserModelID: "YingKe.App"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："
Name: "autostart"; Description: "开机自动启动映刻"; GroupDescription: "其他："

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "YingKe"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动映刻"; Flags: nowait postinstall skipifsilent
