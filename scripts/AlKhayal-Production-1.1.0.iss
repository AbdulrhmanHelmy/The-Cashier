#define AppVersion "1.1.0"
#define ReleaseRoot "D:\AlKhayal-Release-1.1.0"

[Setup]
AppId={{C6042F26-9F4C-42D4-B1BE-377779271777}
AppName=الخيال POS
AppPublisher=AlKhayal
AppVersion={#AppVersion}
UninstallDisplayName=الخيال POS
DefaultDirName={autopf32}\AlKhayal POS
DefaultGroupName=الخيال POS
OutputDir={#ReleaseRoot}\output
OutputBaseFilename=AlKhayal_Setup_{#AppVersion}
SetupIconFile=..\src\AlKhayal.UI\Assets\alkhayal.ico
PrivilegesRequired=admin
MinVersion=6.1sp1
ArchitecturesAllowed=x86 x64compatible
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
DisableDirPage=auto
DisableProgramGroupPage=yes
WizardStyle=modern light
SetupLogging=yes
UninstallDisplayIcon={app}\AlKhayal.UI.exe

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "إنشاء اختصار على سطح المكتب"; GroupDescription: "اختصارات إضافية:"; Flags: unchecked

[Files]
Source: "{#ReleaseRoot}\prerequisites\NDP48-x86-x64-AllOS-ENU.exe"; Flags: dontcopy
Source: "{#ReleaseRoot}\prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX86.exe"; Flags: dontcopy
Source: "{#ReleaseRoot}\prerequisites\MicrosoftEdge_X86_109.0.1518.140.exe"; Flags: dontcopy
Source: "{#ReleaseRoot}\prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX86-109.exe"; Flags: dontcopy
Source: "{#ReleaseRoot}\stage\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml,frontend\tests\*,*.db,*.sqlite,*.pfx,*.pem,*.key"

[Icons]
Name: "{autoprograms}\الخيال POS"; Filename: "{app}\AlKhayal.UI.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\الخيال POS"; Filename: "{app}\AlKhayal.UI.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "http add urlacl url=http://127.0.0.1:5137/ sddl=""D:(A;;GX;;;S-1-5-32-545)"""; StatusMsg: "إعداد صلاحية الخادم المحلي..."; Flags: runhidden

[Code]
var
  PrerequisiteRestart: Boolean;

function HasNet48: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
end;

function HasWebView2: Boolean;
var
  Version: String;
  Key: String;
begin
  Key := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := (RegQueryStringValue(HKLM, Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function RunPrerequisite(Name, Parameters: String): String;
var
  Code: Integer;
begin
  Result := '';
  ExtractTemporaryFile(Name);
  if not Exec(ExpandConstant('{tmp}\' + Name), Parameters, '', SW_HIDE, ewWaitUntilTerminated, Code) then begin
    Result := Name + ': could not start (' + IntToStr(Code) + ')';
    Exit;
  end;
  if Code = 3010 then PrerequisiteRestart := True
  else if Code <> 0 then Result := Name + ': installation failed (' + IntToStr(Code) + ')';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not HasNet48 then begin
    Result := RunPrerequisite('NDP48-x86-x64-AllOS-ENU.exe', '/q /norestart');
    if Result <> '' then Exit;
    if not HasNet48 then begin
      Result := '.NET Framework 4.8 was not detected after installation.';
      Exit;
    end;
  end;
  if not HasWebView2 then begin
    if GetWindowsVersion < $0A000000 then begin
      Result := RunPrerequisite('MicrosoftEdge_X86_109.0.1518.140.exe', '/silent /install');
      if Result <> '' then Exit;
      Result := RunPrerequisite('MicrosoftEdgeWebView2RuntimeInstallerX86-109.exe', '/silent /install');
    end else
      Result := RunPrerequisite('MicrosoftEdgeWebView2RuntimeInstallerX86.exe', '/silent /install');
    if Result <> '' then Exit;
    if not HasWebView2 then begin
      Result := 'WebView2 Runtime was not detected after installation.';
      Exit;
    end;
  end;
  NeedsRestart := PrerequisiteRestart;
end;

