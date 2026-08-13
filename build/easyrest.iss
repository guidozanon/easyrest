; Installer de EasyRest (Inno Setup 6). Lo compila el CI con:
;   iscc /DAppVersion=0.2.0 /DPayloadDir=<carpeta del publish> build\easyrest.iss
;
; Instala por usuario en %LocalAppData%\Programs\EasyRest: sin UAC, y con permisos de escritura
; para que el auto update pueda reemplazar la carpeta sin pedir admin.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\publish\windows-x64"
#endif

#define AppName "EasyRest"
#define AppExe "EasyRest.exe"
#define AppUrl "https://github.com/guidozanon/easyrest"

[Setup]
; el AppId identifica la app entre versiones: no cambiarlo nunca (rompe el upgrade in-place)
AppId={{5E481754-F4F8-4D9E-88DE-FACAA4B4E9B7}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
; instalación por usuario: nada de UAC ni de Program Files
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=EasyRest-Setup-{#AppVersion}
SetupIconFile=..\src\EasyRest.Avalonia\Assets\easyrest.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; si la app está abierta (upgrade), Inno la cierra en vez de fallar con "archivo en uso"
CloseApplications=yes
RestartApplications=no

; el .isl de español viene con Inno Setup 6, pero si la instalación del CI no lo trae
; mejor caer al inglés que romper el build
#if FileExists(AddBackslash(CompilerPath) + "Languages\Spanish.isl")
[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; \
  GroupDescription: "Accesos directos:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; \
  Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppName}"; \
  Flags: nowait postinstall skipifsilent
