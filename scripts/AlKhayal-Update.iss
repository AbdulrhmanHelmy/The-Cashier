#define AppVersion "1.0.10"
#define Stage "D:\AlKhayal-Update-1.0.10\stage"

[Setup]
AppId={{06BDEFFB-C2C6-4076-9C66-22DE1F59EBAE}
AppName=AlKhayal POS
AppVersion={#AppVersion}
AppVerName=AlKhayal POS {#AppVersion}
DefaultDirName={autopf}\AlKhayal POS
DefaultGroupName=AlKhayal POS
OutputDir=D:\AlKhayal-Update-1.0.10
OutputBaseFilename=AlKhayal-Production-Setup-{#AppVersion}
SetupIconFile=D:\projects\AlKhayal\src\AlKhayal.UI\Assets\alkhayal.ico
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
DisableDirPage=auto
SetupLogging=yes
UninstallDisplayIcon={app}\AlKhayal.UI.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#Stage}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml,frontend\tests\*,*.db,*.sqlite,*.pfx,*.pem,*.key"

[Icons]
Name: "{group}\AlKhayal POS"; Filename: "{app}\AlKhayal.UI.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\AlKhayal POS"; Filename: "{app}\AlKhayal.UI.exe"; WorkingDir: "{app}"; Tasks: desktopicon
