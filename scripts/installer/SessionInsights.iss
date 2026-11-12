; Per-user installer for Session Insights. It lays down the same self-contained app that the release zip holds,
; in the same folder, so the tray updater keeps working unchanged (it swaps that folder from the zip asset).
; Build: ISCC /DAppVersion=1.2.3 /DSourceDir=<folder with app\ and scripts\> SessionInsights.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\release"
#endif

[Setup]
AppId={{8D1B7E52-5C0A-4B53-9E2B-6F1C3A4D7E90}
AppName=Session Insights
AppVersion={#AppVersion}
AppPublisher=qvotaxon
DefaultDirName={localappdata}\SessionInsights\app
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
CloseApplications=no
; Outside {app}, because the updater replaces that whole folder and would delete the uninstaller with it.
UninstallFilesDir={localappdata}\SessionInsights\uninstall
UninstallDisplayName=Session Insights
OutputDir=output
OutputBaseFilename=SessionInsights-Setup

[Files]
Source: "{#SourceDir}\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\scripts\uninstall-task.ps1"; Flags: dontcopy
Source: "{#SourceDir}\scripts\install-task.ps1"; Flags: dontcopy
Source: "{#SourceDir}\scripts\install-tray.ps1"; Flags: dontcopy
Source: "{#SourceDir}\scripts\tray.ps1"; Flags: dontcopy

[Icons]
; conhost --headless keeps PowerShell from flashing a console window, as in the scheduled tasks.
Name: "{autoprograms}\Session Insights"; Filename: "{sys}\conhost.exe"; Parameters: "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""{app}\start-session-insights.ps1"""; WorkingDir: "{app}"; IconFilename: "{app}\SessionInsights.Web.exe"; Comment: "Start Session Insights and open the dashboard"

[Run]
Filename: "http://localhost:5180"; Description: "Open Session Insights"; Flags: shellexec postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall-task.ps1"" -InstallPath ""{app}"""; Flags: runhidden waituntilterminated; RunOnceId: "StopSessionInsights"

[UninstallDelete]
; The updater adds files Setup never logged.
Type: filesandordirs; Name: "{app}"

[Code]
function RunScript(const Name, Arguments: String): Integer;
var
  ResultCode: Integer;
begin
  ExtractTemporaryFile(Name);
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\') + Name + '" ' + Arguments,
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Result := ResultCode;
end;

// An earlier install must stop before its files are replaced, and install-task.ps1 refuses to run over an existing task.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if RunScript('uninstall-task.ps1', '-InstallPath "' + ExpandConstant('{app}') + '"') <> 0 then
    Result := 'The running Session Insights could not be stopped. Close it and run Setup again.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Args: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  Args := '-InstallPath "' + ExpandConstant('{app}') + '"';
  ExtractTemporaryFile('tray.ps1'); // install-tray.ps1 copies it from its own folder
  if (RunScript('install-task.ps1', Args + ' -PublishedApp') <> 0) or
     (RunScript('install-tray.ps1', Args) <> 0) then
  begin
    MsgBox('Session Insights was copied but could not be registered to start at logon. Run Setup again, or see the README.', mbError, MB_OK);
    Abort;
  end;
end;
