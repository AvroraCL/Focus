; Inno Setup 7. Build with: pwsh -File build-installer.ps1
#ifndef PayloadDir
  #define PayloadDir SourcePath + "..\artifacts\installer-payload\app"
#endif
#ifndef InstallerAppId
  #define InstallerAppId "{6F0C2C31-5B8B-4F67-9E5B-6C2F1E7A1C11}"
#endif
#ifndef DataFolder
  #define DataFolder "Focus"
#endif
#ifndef GroupName
  #define GroupName "Focus"
#endif
#define FullVersion GetFileVersion(PayloadDir + "\Focus.exe")
#define MyAppVersion Copy(FullVersion, 1, RPos(".", FullVersion) - 1)

[Setup]
AppId={{#InstallerAppId}
AppName=Focus
AppVersion={#MyAppVersion}
AppPublisher=AvroraCL
AppPublisherURL=https://github.com/AvroraCL/Focus
AppSupportURL=https://github.com/AvroraCL/Focus/issues
AppUpdatesURL=https://github.com/AvroraCL/Focus/releases
LicenseFile=..\LICENSE
DefaultDirName={localappdata}\Programs\Focus
DefaultGroupName={#GroupName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=Focus-Setup-{#MyAppVersion}-win-x64
SetupIconFile=..\src\Focus.App\Assets\focus-app-icon-v1.ico
#ifdef InstallerTestBuild
Compression=none
SolidCompression=no
#else
Compression=lzma2
SolidCompression=yes
#endif
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
UninstallDisplayIcon={app}\Focus.exe
UninstallDisplayName=Focus
AppMutex=Focus.App.SingleInstance.v1,Global\Focus.App.SingleInstance.v1,Focus.Overlay.SingleInstance.v1,Global\Focus.Overlay.SingleInstance.v1
CloseApplications=no
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimp.UninstallShortcut=卸载 Focus
english.UninstallShortcut=Uninstall Focus
chinesesimp.PurgeQuestion=是否同时彻底清除 Focus 的配置、HUD、会话、日志和更新缓存？%n%n是：完全卸载，不可恢复。%n否：仅卸载程序，保留个人数据以便重装。%n取消：取消卸载。%n%n数据目录：%1
english.PurgeQuestion=Also permanently delete Focus settings, HUDs, sessions, logs and update cache?%n%nYes: completely uninstall, including personal data. This cannot be undone.%nNo: uninstall the program and keep personal data for reinstalling.%nCancel: cancel uninstall.%n%nData directory: %1
chinesesimp.PurgeFailed=部分个人数据未能删除，可能正在被占用。请关闭相关进程后手动清理：%n%1
english.PurgeFailed=Some personal data could not be removed, possibly because it is in use. Close related processes and remove the remaining files at:%n%1

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Focus"; Filename: "{app}\Focus.exe"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Focus"; Filename: "{app}\Focus.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Focus.exe"; Description: "{cm:LaunchProgram,Focus}"; Flags: nowait postinstall skipifsilent

[Code]
var
  PurgeData: Boolean;
  PreviousFiles, OwnedFiles: TArrayOfString;

function GetFileAttributesW(lpFileName: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function SetFileAttributesW(lpFileName: String; dwFileAttributes: LongWord): Boolean;
  external 'SetFileAttributesW@kernel32.dll stdcall';

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  LoadStringsFromFile(ExpandConstant('{app}\focus-installed-files.txt'), PreviousFiles);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Preserve ownership of files added by earlier ZIP updates when repairing/upgrading.
  if (CurStep = ssPostInstall) and (GetArrayLength(PreviousFiles) > 0) then
    if not SaveStringsToUTF8File(ExpandConstant('{app}\focus-installed-files.txt'), PreviousFiles, True) then
      RaiseException('Could not preserve installed-file ownership.');
end;

procedure RemoveOwnedFile(Relative: String);
var
  Current, Remaining, Part, Parent, AppRoot: String;
  Slash, I: Integer;
  Attr: LongWord;
begin
  // A damaged manifest must never escape the install directory or follow links.
  if (Relative = '') or (Pos('\', Relative) > 0) or
    (CompareText(Copy(Relative, 1, 5), 'unins') = 0) then exit;
  for I := 1 to Length(Relative) do
    if (Ord(Relative[I]) < 32) or (Pos(Relative[I], ':*?"<>|') > 0) then exit;
  AppRoot := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  Current := AppRoot;
  Remaining := Relative;
  repeat
    Attr := GetFileAttributesW(Current);
    if (Attr <> $FFFFFFFF) and ((Attr and FILE_ATTRIBUTE_REPARSE_POINT) <> 0) then exit;
    Slash := Pos('/', Remaining);
    if Slash = 0 then Part := Remaining else Part := Copy(Remaining, 1, Slash - 1);
    if (Part = '') or (Part = '.') or (Part = '..') or
      (Part[Length(Part)] = '.') or (Part[Length(Part)] = ' ') then exit;
    Current := Current + '\' + Part;
    if Slash > 0 then Delete(Remaining, 1, Slash);
  until Slash = 0;
  Attr := GetFileAttributesW(Current);
  if Attr <> $FFFFFFFF then begin
    if (Attr and FILE_ATTRIBUTE_DIRECTORY) <> 0 then exit;
    if (Attr and FILE_ATTRIBUTE_REPARSE_POINT) = 0 then
      SetFileAttributesW(Current, Attr and not FILE_ATTRIBUTE_READONLY);
    if not DeleteFile(Current) then Log('Could not remove installed update file: ' + Current);
  end;
  Parent := ExtractFileDir(Current);
  while (CompareText(Parent, AppRoot) <> 0) and (Length(Parent) > Length(AppRoot)) do begin
    if not RemoveDir(Parent) then break;
    Parent := ExtractFileDir(Parent);
  end;
end;

function InitializeUninstall(): Boolean;
var
  Choice: Integer;
begin
  Result := True;
  PurgeData := ExpandConstant('{param:PURGEDATA|0}') = '1';
  if not UninstallSilent and not PurgeData then begin
    Choice := MsgBox(FmtMessage(CustomMessage('PurgeQuestion'), [ExpandConstant('{userappdata}\{#DataFolder}')]), mbConfirmation,
      MB_YESNOCANCEL or MB_DEFBUTTON2);
    Result := Choice <> IDCANCEL;
    PurgeData := Choice = IDYES;
  end;
end;

// Never follow junctions or symbolic links into unrelated user directories.
function RemoveDataTree(const Path: String): Boolean;
var
  Attr: LongWord;
  Entry: TFindRec;
begin
  Result := True;
  Attr := GetFileAttributesW(Path);
  if Attr = $FFFFFFFF then exit;
  if (Attr and FILE_ATTRIBUTE_DIRECTORY) <> 0 then begin
    if (Attr and FILE_ATTRIBUTE_REPARSE_POINT) = 0 then begin
      if FindFirst(Path + '\*', Entry) then begin
        try
          repeat
            if (Entry.Name <> '.') and (Entry.Name <> '..') then
              if not RemoveDataTree(Path + '\' + Entry.Name) then Result := False;
          until not FindNext(Entry);
        finally
          FindClose(Entry);
        end;
      end;
    end;
    if not RemoveDir(Path) then Result := False;
  end else begin
    if (Attr and FILE_ATTRIBUTE_REPARSE_POINT) = 0 then
      SetFileAttributesW(Path, Attr and not FILE_ATTRIBUTE_READONLY);
    Result := DeleteFile(Path);
  end;
  if not Result then Log('Could not remove personal data: ' + Path);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataPath: String;
  I: Integer;
begin
  if CurUninstallStep = usUninstall then
    LoadStringsFromFile(ExpandConstant('{app}\focus-installed-files.txt'), OwnedFiles);
  if CurUninstallStep = usPostUninstall then begin
    for I := 0 to GetArrayLength(OwnedFiles) - 1 do RemoveOwnedFile(OwnedFiles[I]);
    RemoveDir(ExpandConstant('{app}'));
  end;
  if (CurUninstallStep = usPostUninstall) and PurgeData then begin
    // Only this user's production data directory. Ignore environment overrides.
    DataPath := ExpandConstant('{userappdata}\{#DataFolder}');
    if not RemoveDataTree(DataPath) then
      SuppressibleMsgBox(FmtMessage(CustomMessage('PurgeFailed'), [DataPath]),
        mbError, MB_OK, IDOK);
  end;
end;
