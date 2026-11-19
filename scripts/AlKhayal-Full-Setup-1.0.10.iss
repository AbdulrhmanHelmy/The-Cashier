[Setup]
AppId={{A11CCDB9-E2D4-4A63-B20E-0A8E4B7A1010}
AppName=AlKhayal POS Full Setup
AppVersion=1.0.10
OutputDir=D:\AlKhayal-Update-1.0.10
OutputBaseFilename=AlKhayal-Production-Full-Setup-1.0.10
SetupIconFile=D:\projects\AlKhayal\src\AlKhayal.UI\Assets\alkhayal.ico
DefaultDirName={tmp}\AlKhayalSetup
DisableDirPage=yes
DisableProgramGroupPage=yes
CreateUninstallRegKey=no
Uninstallable=no
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes

[Files]
Source: "D:\AlKhayal-Production-Setup-1.0.9.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsBaseInstall
Source: "D:\AlKhayal-Update-1.0.10\AlKhayal-Production-Setup-1.0.10.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
Filename: "{tmp}\AlKhayal-Production-Setup-1.0.9.exe"; Description: "Install original runtime prerequisites and application"; Check: NeedsBaseInstall; Flags: waituntilterminated
Filename: "{tmp}\AlKhayal-Production-Setup-1.0.10.exe"; Description: "Apply AlKhayal POS 1.0.10"; Flags: waituntilterminated

[Code]
function HasInstalledAppInRoot(Root: Integer; Key: String): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  DisplayName: String;
  InstallLocation: String;
begin
  Result := False;
  if not RegGetSubkeyNames(Root, Key, Names) then Exit;
  for I := 0 to GetArrayLength(Names) - 1 do begin
    if RegQueryStringValue(Root, Key + '\' + Names[I], 'DisplayName', DisplayName) then begin
      if (Pos('AlKhayal', DisplayName) > 0) or (Pos('الخيال', DisplayName) > 0) then begin
        if RegQueryStringValue(Root, Key + '\' + Names[I], 'InstallLocation', InstallLocation) then begin
          if FileExists(AddBackslash(InstallLocation) + 'AlKhayal.UI.exe') then begin
            Result := True;
            Exit;
          end;
        end;
      end;
    end;
  end;
end;

function NeedsBaseInstall: Boolean;
begin
  Result := not (
    HasInstalledAppInRoot(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall') or
    HasInstalledAppInRoot(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall') or
    HasInstalledAppInRoot(HKCU, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall') or
    FileExists(ExpandConstant('{autopf}\AlKhayal POS\AlKhayal.UI.exe'))
  );
end;
