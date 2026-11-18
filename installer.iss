; Instalador do MiniPlayer (Inno Setup). Instala só para o usuário atual: não pede administrador
; e fica numa pasta que o Windows sempre libera na inicialização (%LocalAppData%\Programs\MiniPlayer).
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{7B6E2C4A-3D1F-4C8E-9A51-6F2D8B0C4E17}
AppName=MiniPlayer
AppVersion={#AppVersion}
AppPublisher=Budart
AppPublisherURL=https://github.com/Budart/MiniPlayer
DefaultDirName={localappdata}\Programs\MiniPlayer
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=installer-out
OutputBaseFilename=MiniPlayer-Setup
SetupIconFile=app.ico
UninstallDisplayIcon={app}\MiniPlayer.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; fecha o MiniPlayer aberto antes de atualizar
AppMutex=Local\MiniPlayer.SingleInstance
CloseApplications=yes
ShowLanguageDialog=auto

[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
pt.StartWithWindows=Iniciar com o Windows
en.StartWithWindows=Start with Windows
es.StartWithWindows=Iniciar con Windows
fr.StartWithWindows=Lancer avec Windows
de.StartWithWindows=Mit Windows starten

[Tasks]
Name: "startup"; Description: "{cm:StartWithWindows}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "out\MiniPlayer.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\MiniPlayer"; Filename: "{app}\MiniPlayer.exe"
Name: "{autodesktop}\MiniPlayer"; Filename: "{app}\MiniPlayer.exe"; Tasks: desktopicon

[Registry]
; mesma entrada que a opção "Iniciar com o Windows" do app usa
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MiniPlayer"; ValueData: """{app}\MiniPlayer.exe"""; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueType: binary; ValueName: "MiniPlayer"; ValueData: "02 00 00 00 00 00 00 00 00 00 00 00"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\MiniPlayer.exe"; Description: "{cm:LaunchProgram,MiniPlayer}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/IM MiniPlayer.exe /F"; Flags: runhidden; RunOnceId: "KillMiniPlayer"

[UninstallDelete]
Type: files; Name: "{userappdata}\MiniPlayer.txt"
