; Inno Setup 6 script for the HCC server installer (INST wave, lane server). Structure follows the ETP installer template
; (E:\ETP\Code\Worktrees\main-sync\installer\EtpReportingEngine.iss): setup logging, version info, downgrade refusal, upgrade stops the
; service first, the post-install step is mandatory and its failure ends the installer with a failure exit code and a plain message that
; names the log - never a silent success.
;
; Defines (Build-Installer.ps1 passes them with /D; names are fixed by INST-CONTRACT 4.4):
;   AppVersion      required   x.y.z
;   PublishDir      required   dotnet publish output of src\Hcc.Api (self-contained win-x64), with web\index.html inside
;   OpsDir          required   deployment\windows (all .ps1, samples, INSTALL.md)
;   TemplatesDir    required   deployment\import-templates
;   ClientSetupExe  required   HCC-Client-Setup-<ver>.exe (ends up in {app}\ClientKit for the client kit)
;   SqlMediaDir     optional   SQL Server Express media; without it the wizard offers only "use the SQL Server already on this PC"
;   OutputDir       optional   where the setup .exe is written
;
; What the wizard does: it only collects answers. The work is done by Operations\Install-HccServer.ps1 (the orchestrator), started in
; ssPostInstall with an answer file under %ProgramData%\HCC\Setup (folder locked to SYSTEM + Administrators before the file is written;
; the orchestrator deletes the file in a finally block and this script deletes it again). Passwords never go on a command line, into
; the registry, into the Inno log or into any file that survives the install.

#ifndef AppVersion
#error AppVersion is not defined. Build with installer\Build-Installer.ps1 (it passes /DAppVersion=x.y.z), or pass /DAppVersion=x.y.z.
#endif
#ifndef PublishDir
#error PublishDir is not defined (dotnet publish output of src\Hcc.Api).
#endif
#ifndef OpsDir
#error OpsDir is not defined (deployment\windows).
#endif
#ifndef TemplatesDir
#error TemplatesDir is not defined (deployment\import-templates).
#endif
#ifndef ClientSetupExe
#error ClientSetupExe is not defined (HCC-Client-Setup-<version>.exe).
#endif
#ifndef OutputDir
#define OutputDir "..\..\..\artifacts\installer"
#endif
#define AppName "Hotel Control Centre Server"
#define AppPublisher "Saagar Traders"
#define ServiceName "HccServer"

[Setup]
AppId={{B6A3E3A2-6C54-4F0E-9B1A-2D8F4C7A0C11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Hotel Control Centre server setup
; The install folder is locked (A21): the scripts' defaults, the backup tasks and Hcc:Operations:BackupScript all use C:\Program Files\HCC.
DefaultDirName={autopf}\HCC
DisableDirPage=yes
DefaultGroupName=HCC
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=HCC-Server-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
SetupMutex=HccServerSetupMutex
UninstallDisplayName={#AppName}
UninstallLogMode=append

[InstallDelete]
; A renamed or retired program file or script must not linger beside its replacement. Settings (*.json) and the data are never listed here.
Type: files; Name: "{app}\Server\*.dll"
Type: files; Name: "{app}\Server\*.pdb"
Type: files; Name: "{app}\Server\*.deps.json"
Type: files; Name: "{app}\Server\*.runtimeconfig.json"
Type: filesandordirs; Name: "{app}\Server\runtimes"
Type: files; Name: "{app}\Operations\*.ps1"
Type: files; Name: "{app}\ClientKit\HCC-Client-Setup-*.exe"
Type: files; Name: "{app}\SETUP-INCOMPLETE.txt"

[Files]
; settings are created once from the samples and never replaced (an upgrade merges new settings in, Install-HccServer.ps1 -Upgrade)
Source: "{#OpsDir}\appsettings.Production.sample.json"; DestDir: "{app}\Server"; DestName: "appsettings.Production.json"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#OpsDir}\hcc-ops.sample.json"; DestDir: "{app}\Operations"; DestName: "hcc-ops.json"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#PublishDir}\*"; DestDir: "{app}\Server"; Excludes: "appsettings.Production.json,appsettings.Production.sample.json"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#OpsDir}\*.ps1"; DestDir: "{app}\Operations"; Flags: ignoreversion
Source: "{#OpsDir}\hcc-ops.sample.json"; DestDir: "{app}\Operations"; Flags: ignoreversion
Source: "{#OpsDir}\appsettings.Production.sample.json"; DestDir: "{app}\Server"; Flags: ignoreversion
Source: "{#OpsDir}\INSTALL.md"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "{#OpsDir}\ANDROID_SERVER_ADDRESS.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#TemplatesDir}\*"; DestDir: "{app}\ImportTemplates"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#ClientSetupExe}"; DestDir: "{app}\ClientKit"; Flags: ignoreversion
#ifdef SqlMediaDir
; The SQL media runs with the administrator's full token, so it sits where only administrators can change it (inside the protected
; install folder, never in setup's {tmp}). Copied only when "install the bundled SQL Server" was chosen; deleted as soon as the
; orchestrator returns, whatever the outcome.
Source: "{#OpsDir}\sql-media.manifest.json"; DestDir: "{app}\Operations"; Flags: ignoreversion
; The two small client MSIs (ODBC driver, sqlcmd) are needed even when the SQL Server already on this PC is used (a PC with an instance but no sqlcmd).
Source: "{#SqlMediaDir}\msodbcsql17.msi"; DestDir: "{app}\SqlSetupMedia"; Check: WantsSqlMedia; Flags: ignoreversion deleteafterinstall
Source: "{#SqlMediaDir}\MsSqlCmdLnUtils.msi"; DestDir: "{app}\SqlSetupMedia"; Check: WantsSqlMedia; Flags: ignoreversion deleteafterinstall
Source: "{#SqlMediaDir}\*"; DestDir: "{app}\SqlSetupMedia"; Excludes: "msodbcsql17.msi,MsSqlCmdLnUtils.msi"; Check: WantsBundledSql; Flags: recursesubdirs createallsubdirs ignoreversion deleteafterinstall
#endif

[Dirs]
Name: "{app}\Server\logs"
Name: "{app}\Server\web"

[Icons]
Name: "{group}\HCC"; Filename: "{sys}\cmd.exe"; Parameters: "/c start """" ""{code:ServerUrl}"""; Flags: runminimized; Check: ShowOpenIcon and (not IsTrialInstall)
Name: "{group}\HCC - TRIAL (not for real guest data)"; Filename: "{sys}\cmd.exe"; Parameters: "/c start """" ""{code:ServerUrl}"""; Flags: runminimized; Check: ShowOpenIcon and IsTrialInstall
Name: "{group}\HCC client kit (copy to a USB stick)"; Filename: "{app}\ClientKit"
Name: "{group}\HCC install logs"; Filename: "{commonappdata}\HCC\Logs"
Name: "{group}\Uninstall HCC server"; Filename: "{uninstallexe}"

[Run]
; A09-05: register the Windows Event Log source HCC (alerts of the server and the backups); idempotent, also done again by Install-HccService.ps1 in the orchestrator.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\Operations\Install-HccService.ps1"" -EventSourceOnly"; Flags: runhidden; StatusMsg: "Registering the Windows Event Log source HCC..."
Filename: "{sys}\cmd.exe"; Parameters: "/c start """" ""{code:ServerUrl}"""; Description: "Open HCC now"; Flags: postinstall nowait skipifsilent runhidden; Check: ShowOpenIcon

[UninstallRun]
; Removes the service, its firewall rules and the scheduled tasks. Never SQL Server, the databases, the backups, the keys,
; the certificates or the settings files (INST-CONTRACT rule 9).
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\Operations\Install-HccServer.ps1"" -Uninstall"; RunOnceId: "RemoveHccServer"; Flags: runhidden waituntilterminated skipifdoesntexist

[UninstallDelete]
Type: filesandordirs; Name: "{app}\ClientKit"
Type: filesandordirs; Name: "{app}\SqlSetupMedia"
Type: filesandordirs; Name: "{app}\Server\logs"
Type: files; Name: "{app}\SETUP-INCOMPLETE.txt"

[Code]
// ---------------------------------------------------------------------------------------------------------------------------
// Windows API (the exit codes of a failed or restart-needed setup are chosen by us; Inno has no supported way to set them)
// ---------------------------------------------------------------------------------------------------------------------------
procedure ExitProcess(uExitCode: UINT);
  external 'ExitProcess@kernel32.dll stdcall';
function OpenProcess(dwDesiredAccess: Cardinal; bInheritHandle: Integer; dwProcessId: Cardinal): Cardinal;
  external 'OpenProcess@kernel32.dll stdcall';
function GetExitCodeProcess(hProcess: Cardinal; var lpExitCode: Cardinal): Integer;
  external 'GetExitCodeProcess@kernel32.dll stdcall';
function CloseHandle(hObject: Cardinal): Integer;
  external 'CloseHandle@kernel32.dll stdcall';

// Message pump for the progress page (Inno exposes no Application.ProcessMessages): setup is a 32-bit process, so a MSG is 28 bytes.
type
  TWinMsg = record
    Wnd: Cardinal;
    Msg: Cardinal;
    WParam: Cardinal;
    LParam: Cardinal;
    Time: Cardinal;
    PtX: Integer;
    PtY: Integer;
  end;

function PeekMessage(var lpMsg: TWinMsg; hWnd: Cardinal; wMsgFilterMin, wMsgFilterMax, wRemoveMsg: Cardinal): Integer;
  external 'PeekMessageW@user32.dll stdcall';
function TranslateMessage(var lpMsg: TWinMsg): Integer;
  external 'TranslateMessage@user32.dll stdcall';
function DispatchMessage(var lpMsg: TWinMsg): Integer;
  external 'DispatchMessageW@user32.dll stdcall';

procedure PumpMessages;
var
  M: TWinMsg;
begin
  while PeekMessage(M, 0, 0, 0, 1) <> 0 do   // PM_REMOVE
  begin
    TranslateMessage(M);
    DispatchMessage(M);
  end;
end;

const
  AppIdKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B6A3E3A2-6C54-4F0E-9B1A-2D8F4C7A0C11}_is1';
  SetupFailedExitCode = 1603;       // the post-install step failed: the installation is not usable yet
  RestartNeededExitCode = 3010;     // Windows must restart, then run this setup again (it continues where it stopped)
  StillActive = 259;

var
  ModePage: TInputOptionWizardPage;
  HotelPage: TInputQueryWizardPage;
  BackupPage: TInputDirWizardPage;
  OwnerPage: TInputQueryWizardPage;
  SqlPage: TInputOptionWizardPage;
  CaPage: TInputQueryWizardPage;
  UsbPage: TInputDirWizardPage;
  UpgradeRun: Boolean;
  ResumeRun: Boolean;
  SqlDetected: Boolean;
  SetupSucceeded: Boolean;
  UpgradeServerUrl: String;

// ---------------------------------------------------------------------------------------------------------------------------
// Small helpers
// ---------------------------------------------------------------------------------------------------------------------------
function DataDir: String;
begin
  Result := ExpandConstant('{commonappdata}\HCC');
end;

function SetupDir: String;
begin
  Result := DataDir + '\Setup';
end;

function AnswerFileName: String;
begin
  Result := SetupDir + '\answers.json';
end;

function PowerShellExe: String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

// value of key=value in a small text file written by the orchestrator ('' when missing)
function ReadSetting(const FileName, Key: String): String;
var
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := '';
  if not LoadStringsFromFile(FileName, Lines) then Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
    if Pos(Key + '=', Lines[I]) = 1 then
    begin
      Result := Trim(Copy(Lines[I], Length(Key) + 2, 2000));
      Exit;
    end;
end;

function WarningsText: String;
var
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := '';
  if LoadStringsFromFile(SetupDir + '\warnings.txt', Lines) then
    for I := 0 to GetArrayLength(Lines) - 1 do
      if Trim(Lines[I]) <> '' then Result := Result + '  - ' + Trim(Lines[I]) + #13#10;
end;

function ServiceExists: Boolean;
var
  rc: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, rc) and (rc = 0);
end;

function JsonEscape(const S: String): String;
var
  I: Integer;
  C: Char;
  R: String;
  Hex: String;
begin
  Hex := '0123456789abcdef';
  R := '';
  for I := 1 to Length(S) do
  begin
    C := S[I];
    if C = '\' then R := R + '\\'
    else if C = '"' then R := R + '\"'
    else if Ord(C) < 32 then R := R + '\u00' + Hex[(Ord(C) div 16) + 1] + Hex[(Ord(C) mod 16) + 1]
    else R := R + C;
  end;
  Result := R;
end;

// "a, b;c" -> ["a","b","c"] (JSON array text)
function IpListJson(const Text: String): String;
var
  I: Integer;
  C: Char;
  Cur, R: String;
begin
  R := '';
  Cur := '';
  for I := 1 to Length(Text) + 1 do
  begin
    if I <= Length(Text) then C := Text[I] else C := ',';
    if (C = ',') or (C = ';') or (C = ' ') then
    begin
      if Cur <> '' then
      begin
        if R <> '' then R := R + ',';
        R := R + '"' + JsonEscape(Cur) + '"';
        Cur := '';
      end;
    end
    else Cur := Cur + C;
  end;
  Result := '[' + R + ']';
end;

function IsDigits(const S: String): Boolean;
var
  I: Integer;
begin
  Result := Length(S) > 0;
  for I := 1 to Length(S) do
    if (S[I] < '0') or (S[I] > '9') then Result := False;
end;

function IsHostNameOk(const S: String): Boolean;
var
  I: Integer;
  C: Char;
begin
  Result := (Length(S) >= 1) and (Length(S) <= 63);
  for I := 1 to Length(S) do
  begin
    C := S[I];
    if not (((C >= 'a') and (C <= 'z')) or ((C >= 'A') and (C <= 'Z')) or ((C >= '0') and (C <= '9')) or (C = '-') or (C = '.')) then Result := False;
  end;
end;

function IsLoginOk(const S: String): Boolean;
var
  I: Integer;
  C: Char;
begin
  Result := (Length(S) >= 3) and (Length(S) <= 40);
  for I := 1 to Length(S) do
  begin
    C := S[I];
    if not (((C >= 'a') and (C <= 'z')) or ((C >= 'A') and (C <= 'Z')) or ((C >= '0') and (C <= '9')) or (C = '-') or (C = '.') or (C = '_')) then Result := False;
  end;
end;

// the server's password rules (DEC-062): 12-128 characters, not containing the sign-in name, not one repeated character
function PasswordProblem(const Password, Login: String): String;
var
  I, Distinct: Integer;
  Seen: String;
begin
  Result := '';
  if (Length(Password) < 12) or (Length(Password) > 128) then
  begin
    Result := 'The password must be 12 to 128 characters.';
    Exit;
  end;
  if Pos(Lowercase(Login), Lowercase(Password)) > 0 then
  begin
    Result := 'The password must not contain the sign-in name.';
    Exit;
  end;
  Seen := '';
  Distinct := 0;
  for I := 1 to Length(Password) do
    if Pos(Password[I], Seen) = 0 then
    begin
      Seen := Seen + Password[I];
      Distinct := Distinct + 1;
    end;
  if Distinct < 4 then Result := 'The password is too simple (use more different characters).';
end;

function IsFullFolderPath(const S: String): Boolean;
begin
  Result := (Length(S) >= 4) and (((S[1] >= 'A') and (S[1] <= 'Z')) or ((S[1] >= 'a') and (S[1] <= 'z'))) and (S[2] = ':') and (S[3] = '\');
end;

// ---------------------------------------------------------------------------------------------------------------------------
// Versions (ETP's rule: an older setup never goes over a newer install)
// ---------------------------------------------------------------------------------------------------------------------------
function VersionPart(Version: String; Index: Integer): Integer;
var
  I, Part: Integer;
  Text: String;
begin
  I := Pos('+', Version);
  if I > 0 then Version := Copy(Version, 1, I - 1);
  I := Pos('-', Version);
  if I > 0 then Version := Copy(Version, 1, I - 1);
  Result := 0;
  for Part := 0 to Index do
  begin
    I := Pos('.', Version);
    if I > 0 then
    begin
      Text := Copy(Version, 1, I - 1);
      Version := Copy(Version, I + 1, Length(Version));
    end
    else
    begin
      Text := Version;
      Version := '';
    end;
    if Part = Index then Result := StrToIntDef(Trim(Text), 0);
  end;
end;

function IsNewerVersion(First, Second: String): Boolean;
var
  Index, A, B: Integer;
begin
  Result := False;
  for Index := 0 to 2 do
  begin
    A := VersionPart(First, Index);
    B := VersionPart(Second, Index);
    if A <> B then
    begin
      Result := A > B;
      Exit;
    end;
  end;
end;

function InstalledVersion: String;
begin
  Result := '';
  if not RegQueryStringValue(HKLM64, AppIdKey, 'DisplayVersion', Result) then
    if not RegQueryStringValue(HKLM32, AppIdKey, 'DisplayVersion', Result) then
      Result := '';
end;

// ---------------------------------------------------------------------------------------------------------------------------
// What kind of run is this?
//   upgrade : the service exists and the last install finished (or there is no record: an install made by hand) - no questions
//   resume  : an earlier setup stopped (failed or Windows restart needed) - the questions again, then it continues at the failed step
//   fresh   : everything else
// ---------------------------------------------------------------------------------------------------------------------------
procedure DetectRunKind;
var
  Status: String;
begin
  Status := ReadSetting(SetupDir + '\install-summary.txt', 'status');
  UpgradeRun := ServiceExists and ((Status = '') or (Status = 'Done'));
  ResumeRun := (not UpgradeRun) and ((Status = 'InProgress') or (Status = 'Failed') or (Status = 'RestartNeeded'));
  if UpgradeRun then UpgradeServerUrl := ReadSetting(SetupDir + '\install-summary.txt', 'serverUrl');
end;

function IsTrialInstall: Boolean;
begin
  if UpgradeRun then
    Result := ReadSetting(SetupDir + '\install-summary.txt', 'mode') = 'Trial'
  else
    Result := (ModePage <> nil) and ModePage.Values[0];
end;

function ServerUrl(Param: String): String;
begin
  if UpgradeRun then
    Result := UpgradeServerUrl
  else if HotelPage <> nil then
    Result := 'https://' + Lowercase(Trim(HotelPage.Values[1])) + ':' + Trim(HotelPage.Values[3]) + '/'
  else
    Result := '';
end;

function ShowOpenIcon: Boolean;
begin
  Result := ServerUrl('') <> '';
end;

function WantsSqlMedia: Boolean;
begin
  Result := not UpgradeRun;
end;

function WantsBundledSql: Boolean;
begin
  Result := (not UpgradeRun) and (SqlPage <> nil) and SqlPage.Values[0];
end;

// ---------------------------------------------------------------------------------------------------------------------------
// Detection helpers for the wizard defaults
// ---------------------------------------------------------------------------------------------------------------------------
function LocalIpAddresses: String;
var
  Tmp, Cmd: String;
  Lines: TArrayOfString;
  rc, I: Integer;
begin
  Result := '';
  Tmp := ExpandConstant('{tmp}\hcc-ips.txt');
  Cmd := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike ''127.*'' -and $_.IPAddress -notlike ''169.254.*'' } | ForEach-Object { $_.IPAddress } | Set-Content -Path ''' + Tmp + ''' -Encoding ASCII"';
  if Exec(PowerShellExe, Cmd, '', SW_HIDE, ewWaitUntilTerminated, rc) and LoadStringsFromFile(Tmp, Lines) then
    for I := 0 to GetArrayLength(Lines) - 1 do
      if Trim(Lines[I]) <> '' then
      begin
        if Result <> '' then Result := Result + ', ';
        Result := Result + Trim(Lines[I]);
      end;
  DeleteFile(Tmp);
end;

function DetectSqlExpress: Boolean;
var
  Value: String;
begin
  Result := RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL', 'SQLEXPRESS', Value);
end;

// ---------------------------------------------------------------------------------------------------------------------------
// Start / wizard
// ---------------------------------------------------------------------------------------------------------------------------
function InitializeSetup(): Boolean;
var
  Installed: String;
begin
  Result := True;
  DetectRunKind;
  Installed := InstalledVersion;
  if (Installed <> '') and IsNewerVersion(Installed, '{#AppVersion}') then
  begin
    Result := False;
    Log('HCC server ' + Installed + ' is installed, newer than this setup ({#AppVersion}). Setup refused: no older release over a newer install.');
    if not WizardSilent then
      MsgBox('HCC server ' + Installed + ' is installed, which is newer than this setup ({#AppVersion}). Setup will not put an older release over a newer one, because the older program would then run on the newer database.' + #13#10 + #13#10 + 'Nothing was changed.', mbError, MB_OK);
    Exit;
  end;
  if WizardSilent and (not UpgradeRun) then
  begin
    Result := False;
    Log('A first install or a resume needs the setup questions: run setup without /SILENT.');
    Exit;
  end;
end;

procedure InitializeWizard;
var
  Ips: String;
begin
  if UpgradeRun then Exit;
  SqlDetected := DetectSqlExpress;

  ModePage := CreateInputOptionPage(wpWelcome, 'Kind of installation', 'Is this a trial or the real hotel PC?',
    'Choose one. The choice cannot be changed afterwards by this setup.', True, False);
  ModePage.Add('Trial: to try HCC on a spare PC. Everything is made automatically. NOT for real guest data. The pages say "TRIAL".');
  ModePage.Add('Production: the real hotel PC. You will need a USB stick for the security keys (kept in the hotel safe).');
  ModePage.Values[0] := False;
  ModePage.Values[1] := True;

  Ips := LocalIpAddresses;
  HotelPage := CreateInputQueryPage(ModePage.ID, 'Your hotel and this PC', 'Staff will open HCC at this PC''s name or address.',
    'The name and address are filled in from this PC. Change them only if you know why.');
  HotelPage.Add('Hotel name:', False);
  HotelPage.Add('This PC''s name:', False);
  HotelPage.Add('This PC''s network address(es), separated by commas (give the PC a fixed address in the router):', False);
  HotelPage.Add('Port (leave 5443):', False);
  HotelPage.Values[1] := Lowercase(GetComputerNameString);
  HotelPage.Values[2] := Ips;
  HotelPage.Values[3] := '5443';

  BackupPage := CreateInputDirPage(HotelPage.ID, 'Backup folder', 'Where should the daily backups be kept?',
    'Best: another disk or a USB/NAS disk, not the disk HCC is installed on. The folder is made private (only Administrators and SQL Server).', False, '');
  BackupPage.Add('');
  BackupPage.Values[0] := 'C:\HCC-Backups';

  OwnerPage := CreateInputQueryPage(BackupPage.ID, 'The first Owner', 'Create the account you will sign in with.',
    'The password must be 12 to 128 characters, must not contain the sign-in name, and must not be one repeated character. Write it down and keep it safe.');
  OwnerPage.Add('Owner sign-in name (3 to 40 letters, digits, dot, dash or underscore):', False);
  OwnerPage.Add('Password:', True);
  OwnerPage.Add('Type the password again:', True);
  OwnerPage.Values[0] := 'owner';

  SqlPage := CreateInputOptionPage(OwnerPage.ID, 'Database (SQL Server)', 'HCC keeps its data in Microsoft SQL Server Express.',
    'Choose where it comes from.', True, False);
#ifdef SqlMediaDir
  SqlPage.Add('Install Microsoft SQL Server 2025 Express, its ODBC drivers and Sqlcmd from the media included with this installer (accepts Microsoft''s licence terms)');
#else
  SqlPage.Add('(This setup does not contain SQL Server.)');
#endif
  SqlPage.Add('Use the SQL Server Express already on this PC (instance SQLEXPRESS, for example the one ETP uses). HCC uses its own databases and never touches others.');
  if SqlDetected then
    SqlPage.Values[1] := True
  else
  begin
#ifdef SqlMediaDir
    SqlPage.Values[0] := True;
#else
    SqlPage.Values[1] := True;
#endif
  end;

  CaPage := CreateInputQueryPage(SqlPage.ID, 'Hotel certificate password (Production)', 'This protects the hotel''s certificate key, which is saved on your USB stick only.',
    'Choose a password of at least 12 characters and put it in the safe with the stick. You need both once a year.');
  CaPage.Add('Certificate password:', True);
  CaPage.Add('Type it again:', True);

  UsbPage := CreateInputDirPage(CaPage.ID, 'USB stick for the keys (Production)', 'Plug in the keys USB stick and choose a folder on it.',
    'The certificate key and the copies of the ID and backup keys are saved here. Keep this stick in the hotel safe and make a second one for off site.', False, '');
  UsbPage.Add('');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if UpgradeRun then
  begin
    // an upgrade asks nothing: it keeps the settings and the keys
    if (ModePage <> nil) and ((PageID = ModePage.ID) or (PageID = HotelPage.ID) or (PageID = BackupPage.ID) or (PageID = OwnerPage.ID) or (PageID = SqlPage.ID) or (PageID = CaPage.ID) or (PageID = UsbPage.ID)) then Result := True;
    Exit;
  end;
  if (CaPage <> nil) and ((PageID = CaPage.ID) or (PageID = UsbPage.ID)) then
    Result := ModePage.Values[0];   // Trial: no CA password and no USB stick
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Problem, Ip, Rest: String;
  Port: Integer;
  Sys: String;
begin
  Result := True;
  if UpgradeRun or (ModePage = nil) then Exit;

  if CurPageID = HotelPage.ID then
  begin
    Port := StrToIntDef(Trim(HotelPage.Values[3]), 0);
    if (Length(Trim(HotelPage.Values[0])) < 1) or (Length(Trim(HotelPage.Values[0])) > 100) then
      Problem := 'Type the hotel name (up to 100 characters).'
    else if not IsHostNameOk(Trim(HotelPage.Values[1])) then
      Problem := 'This PC''s name may contain only letters, digits, dots and dashes.'
    else if Length(Trim(HotelPage.Values[2])) < 7 then
      Problem := 'Type at least one network address of this PC, for example 192.168.1.10.'
    else if (Port < 1024) or (Port > 65535) then
      Problem := 'The port must be a number between 1024 and 65535.'
    else
      Problem := '';
    if Problem <> '' then begin MsgBox(Problem, mbError, MB_OK); Result := False; end;
  end
  else if CurPageID = BackupPage.ID then
  begin
    if not IsFullFolderPath(BackupPage.Values[0]) then
    begin
      MsgBox('Type a full folder path on a drive, for example D:\HccBackups.', mbError, MB_OK);
      Result := False;
    end
    else if Pos(Lowercase(ExpandConstant('{app}')), Lowercase(BackupPage.Values[0])) = 1 then
    begin
      MsgBox('The backup folder must not be inside the HCC program folder.', mbError, MB_OK);
      Result := False;
    end;
  end
  else if CurPageID = OwnerPage.ID then
  begin
    if not IsLoginOk(Trim(OwnerPage.Values[0])) then
      Problem := 'The sign-in name must be 3 to 40 characters: letters, digits, dot, dash or underscore.'
    else if OwnerPage.Values[1] <> OwnerPage.Values[2] then
      Problem := 'The two passwords are different. Type the same password twice.'
    else
      Problem := PasswordProblem(OwnerPage.Values[1], Trim(OwnerPage.Values[0]));
    if Problem <> '' then begin MsgBox(Problem, mbError, MB_OK); Result := False; end;
  end
  else if CurPageID = SqlPage.ID then
  begin
#ifndef SqlMediaDir
    if SqlPage.Values[0] or (not SqlDetected) then
    begin
      MsgBox('SQL Server Express (instance SQLEXPRESS) was not found on this PC and this setup does not contain it. Install SQL Server Express first, then run this setup again.', mbError, MB_OK);
      Result := False;
    end;
#else
    if SqlPage.Values[1] and (not SqlDetected) then
    begin
      MsgBox('SQL Server Express (instance SQLEXPRESS) was not found on this PC. Choose the first option to install it.', mbError, MB_OK);
      Result := False;
    end;
#endif
  end
  else if CurPageID = CaPage.ID then
  begin
    if CaPage.Values[0] <> CaPage.Values[1] then
      Problem := 'The two passwords are different. Type the same password twice.'
    else if Length(CaPage.Values[0]) < 12 then
      Problem := 'The certificate password must be at least 12 characters.'
    else
      Problem := '';
    if Problem <> '' then begin MsgBox(Problem, mbError, MB_OK); Result := False; end;
  end
  else if CurPageID = UsbPage.ID then
  begin
    Sys := Lowercase(Copy(ExpandConstant('{sys}'), 1, 3));
    if not DirExists(UsbPage.Values[0]) then
      Problem := 'That folder was not found. Plug in the USB stick and choose a folder on it.'
    else if Lowercase(Copy(UsbPage.Values[0], 1, 3)) = Sys then
      Problem := 'Choose a folder on the USB stick, not on this PC''s system drive: the keys would stay on this PC.'
    else
      Problem := '';
    if Problem <> '' then begin MsgBox(Problem, mbError, MB_OK); Result := False; end;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  S: String;
begin
  if UpgradeRun then
  begin
    Result := 'Upgrade of the installed HCC server to {#AppVersion}.' + NewLine + NewLine +
      'Setup stops the HCC service, replaces the program files, adds any new settings (your existing settings, keys and data are kept), updates the database tables, starts the service again and checks that HCC answers.' + NewLine +
      'Nothing is asked.';
    Exit;
  end;
  S := '';
  if ModePage.Values[0] then
    S := S + 'TRIAL install - not for real guest data.' + NewLine
  else
    S := S + 'Production install.' + NewLine;
  S := S + NewLine + 'Hotel: ' + Trim(HotelPage.Values[0]) + NewLine;
  S := S + 'This PC: ' + Lowercase(Trim(HotelPage.Values[1])) + ', address(es) ' + Trim(HotelPage.Values[2]) + ', port ' + Trim(HotelPage.Values[3]) + NewLine;
  S := S + 'Staff open: ' + ServerUrl('') + NewLine;
  S := S + 'Backup folder: ' + BackupPage.Values[0] + NewLine;
  S := S + 'First Owner: ' + Trim(OwnerPage.Values[0]) + ' (password as typed)' + NewLine;
  if SqlPage.Values[0] then
    S := S + 'SQL Server: install the one that comes with this setup (Microsoft licence accepted)' + NewLine
  else
    S := S + 'SQL Server: use the one already on this PC' + NewLine;
  if ModePage.Values[0] then
    S := S + NewLine + 'The hotel certificate, its key and the key copies are made automatically and kept on this PC (Administrators only).' + NewLine
  else
    S := S + 'Keys USB folder: ' + UsbPage.Values[0] + NewLine;
  S := S + NewLine + 'Setup installs SQL Server (if chosen), creates the HCC databases and the hotel certificate, installs and starts the HCC service, sets up the hotel and the Owner, schedules the backups, blocks internet access for HCC and prepares the client kit. This takes several minutes.';
  Result := S;
end;

// ---------------------------------------------------------------------------------------------------------------------------
// Before the files: stop the service of an upgrade
// ---------------------------------------------------------------------------------------------------------------------------
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  rc: Integer;
begin
  Result := '';
  if ServiceExists then
  begin
    Log('Stopping the HCC service before the files are replaced.');
    Exec(ExpandConstant('{sys}\net.exe'), 'stop {#ServiceName} /y', '', SW_HIDE, ewWaitUntilTerminated, rc);
  end;
end;

// ---------------------------------------------------------------------------------------------------------------------------
// The mandatory post-install step
// ---------------------------------------------------------------------------------------------------------------------------
function BuildAnswerJson: String;
var
  Mode, Sql: String;
begin
  if ModePage.Values[0] then Mode := 'Trial' else Mode := 'Production';
  if SqlPage.Values[0] then Sql := 'InstallBundled' else Sql := 'UseExisting';
  Result := '{' +
    '"mode":"' + Mode + '",' +
    '"hotelName":"' + JsonEscape(Trim(HotelPage.Values[0])) + '",' +
    '"serverName":"' + JsonEscape(Lowercase(Trim(HotelPage.Values[1]))) + '",' +
    '"serverIps":' + IpListJson(HotelPage.Values[2]) + ',' +
    '"httpsPort":' + IntToStr(StrToIntDef(Trim(HotelPage.Values[3]), 5443)) + ',' +
    '"backupDirectory":"' + JsonEscape(BackupPage.Values[0]) + '",' +
    '"ownerUserName":"' + JsonEscape(Trim(OwnerPage.Values[0])) + '",' +
    '"ownerPassword":"' + JsonEscape(OwnerPage.Values[1]) + '",' +
    '"sql":"' + Sql + '",';
  if ModePage.Values[0] then
    Result := Result + '"caPfxPassword":"","offlineCopyDirectory":""}'
  else
    Result := Result + '"caPfxPassword":"' + JsonEscape(CaPage.Values[0]) + '","offlineCopyDirectory":"' + JsonEscape(UsbPage.Values[0]) + '"}';
end;

// Creates a folder WITH its permissions in one call (owner Administrators; only SYSTEM and Administrators; inheritance off), so there is
// never a moment in which it exists open to others. Used for the folder that holds the answer file (passwords) and for the SQL media
// (run as administrator). An existing folder gets the same permissions applied.
function CreatePrivateFolder(const Path: String): Boolean;
var
  ScriptFile: String;
  Lines: TArrayOfString;
  rc: Integer;
begin
  Result := False;
  ScriptFile := ExpandConstant('{tmp}\hcc-private-folder.ps1');
  SetArrayLength(Lines, 11);
  Lines[0] := 'param([string]$Path)';
  Lines[1] := '$ErrorActionPreference = ''Stop''';
  Lines[2] := '$admins = New-Object Security.Principal.SecurityIdentifier(''S-1-5-32-544'')';
  Lines[3] := '$system = New-Object Security.Principal.SecurityIdentifier(''S-1-5-18'')';
  Lines[4] := '$sec = New-Object Security.AccessControl.DirectorySecurity';
  Lines[5] := '$sec.SetAccessRuleProtection($true, $false)';
  Lines[6] := 'foreach ($sid in @($system, $admins)) { $sec.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, ''FullControl'', ''ContainerInherit,ObjectInherit'', ''None'', ''Allow''))) }';
  Lines[7] := 'if (Test-Path -LiteralPath $Path) { Set-Acl -LiteralPath $Path -AclObject $sec }';
  Lines[8] := 'else { $sec.SetOwner($admins); [void][IO.Directory]::CreateDirectory($Path, $sec) }';
  Lines[9] := 'exit 0';
  Lines[10] := '';
  if not SaveStringsToUTF8File(ScriptFile, Lines, False) then Exit;
  Result := Exec(PowerShellExe, '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptFile + '" -Path "' + Path + '"', '', SW_HIDE, ewWaitUntilTerminated, rc) and (rc = 0);
  DeleteFile(ScriptFile);
end;

function PrepareSetupFolder: Boolean;
begin
  Result := CreatePrivateFolder(SetupDir);
end;

function ProcessIsRunning(Pid: Cardinal): Boolean;
var
  H, Code: Cardinal;
begin
  Result := False;
  H := OpenProcess($1000, 0, Pid);   // PROCESS_QUERY_LIMITED_INFORMATION
  if H = 0 then Exit;
  Code := 0;
  if GetExitCodeProcess(H, Code) <> 0 then Result := (Code = StillActive);
  CloseHandle(H);
end;

// 0 = done, 1 = failed, 2 = Windows must restart
function RunOrchestrator: Integer;
var
  Params, ProgressFile, Status, Title, Pid, Step, Of_: String;
  rc, Waited, N, Total: Integer;
  PidValue: Cardinal;
begin
  Result := 1;
  ProgressFile := SetupDir + '\progress.txt';
  DeleteFile(ProgressFile);
  Params := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Operations\Install-HccServer.ps1') + '"';
  if UpgradeRun then
    Params := Params + ' -Upgrade'
  else
  begin
    Params := Params + ' -AnswerFile "' + AnswerFileName + '"';
    if ResumeRun then Params := Params + ' -Resume';
  end;
  WizardForm.CancelButton.Enabled := False;
  WizardForm.StatusLabel.Caption := 'Setting up HCC...';
  WizardForm.FilenameLabel.Caption := 'This takes several minutes. Please do not switch the PC off.';
  if not Exec(PowerShellExe, Params, '', SW_HIDE, ewNoWait, rc) then
  begin
    Log('Could not start PowerShell for the HCC setup step.');
    Exit;
  end;
  Waited := 0;
  PidValue := 0;
  while True do
  begin
    Sleep(500);
    PumpMessages;
    Status := ReadSetting(ProgressFile, 'status');
    Pid := ReadSetting(ProgressFile, 'pid');
    Step := ReadSetting(ProgressFile, 'step');
    Of_ := ReadSetting(ProgressFile, 'of');
    Title := ReadSetting(ProgressFile, 'title');
    if (Pid <> '') and (PidValue = 0) then PidValue := StrToIntDef(Pid, 0);
    N := StrToIntDef(Step, 0);
    Total := StrToIntDef(Of_, 0);
    if (Total > 0) and (N > 0) then
    begin
      WizardForm.StatusLabel.Caption := 'Step ' + IntToStr(N) + ' of ' + IntToStr(Total) + ': ' + Title;
      WizardForm.ProgressGauge.Max := Total;
      WizardForm.ProgressGauge.Position := N - 1;
    end;
    if FileExists(SetupDir + '\warnings.txt') then WizardForm.FilenameLabel.Caption := Trim(WarningsText);
    if Status = 'done' then begin Result := 0; Break; end;
    if Status = 'restart' then begin Result := 2; Break; end;
    if Status = 'failed' then begin Result := 1; Break; end;
    Waited := Waited + 1;
    if (PidValue = 0) and (Waited > 240) then
    begin
      Log('The HCC setup step did not report back within two minutes.');
      Result := 1;
      Break;
    end;
    if (PidValue <> 0) and (not ProcessIsRunning(PidValue)) then
    begin
      // it ended without a final status: re-read once, otherwise it crashed
      Status := ReadSetting(ProgressFile, 'status');
      if Status = 'done' then Result := 0
      else if Status = 'restart' then Result := 2
      else Result := 1;
      Break;
    end;
  end;
  WizardForm.ProgressGauge.Position := WizardForm.ProgressGauge.Max;
  WizardForm.CancelButton.Enabled := True;
end;

procedure RemoveAnswerFile;
begin
  if FileExists(AnswerFileName) then DeleteFile(AnswerFileName);
end;

procedure FailSetup(Outcome: Integer);
var
  LogFile, Msg: String;
begin
  RemoveAnswerFile;
  DelTree(ExpandConstant('{app}\SqlSetupMedia'), True, True, True);
  LogFile := ReadSetting(SetupDir + '\install-summary.txt', 'logFile');
  if LogFile = '' then LogFile := DataDir + '\Logs';
  if Outcome = 2 then
    Msg := 'Windows must restart before HCC setup can finish.' + #13#10 + #13#10 + 'Restart this PC, then run the HCC setup again. It continues where it stopped.' + #13#10 + #13#10 + 'Log: ' + LogFile
  else
    Msg := 'The HCC setup did NOT finish. HCC is not ready to use.' + #13#10 + #13#10 + 'The log says which step failed: ' + LogFile + #13#10 + #13#10 + 'Fix the problem named there and run this setup again: it continues at the step that failed. Nothing already made (databases, keys, certificates) is thrown away.';
  SaveStringToFile(ExpandConstant('{app}\SETUP-INCOMPLETE.txt'), Msg + #13#10, False);
  Log(Msg);
  if not WizardSilent then MsgBox(Msg, mbError, MB_OK);
  // Inno offers no supported way to choose its exit code: end the process (ETP does the same).
  if Outcome = 2 then ExitProcess(RestartNeededExitCode) else ExitProcess(SetupFailedExitCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Outcome: Integer;
  Lines: TArrayOfString;
begin
  if CurStep = ssInstall then
  begin
    // C:\ProgramData\HCC is created (or repaired) with administrator-only permissions BEFORE anything is written into it
    if not CreatePrivateFolder(DataDir) then
      RaiseException('Could not make the folder ' + DataDir + ' private to administrators.');
  end;
  if (CurStep = ssInstall) and WantsSqlMedia then
  begin
    // the SQL media runs elevated: its folder must be writable by Administrators and SYSTEM only BEFORE the files land
    if not CreatePrivateFolder(ExpandConstant('{app}\SqlSetupMedia')) then
      RaiseException('Could not make the SQL media folder private to administrators.');
  end;
  if CurStep <> ssPostInstall then Exit;

  SaveStringToFile(ExpandConstant('{app}\VERSION.txt'), '{#AppVersion}', False);
  if not UpgradeRun then
  begin
    if not PrepareSetupFolder then
    begin
      Log('Could not lock the Setup folder, so no answers were written.');
      FailSetup(1);
    end;
    SetArrayLength(Lines, 1);
    Lines[0] := BuildAnswerJson;
    if not SaveStringsToUTF8File(AnswerFileName, Lines, False) then
    begin
      Log('Could not write the answer file.');
      FailSetup(1);
    end;
    Lines[0] := '';
  end;
  Outcome := RunOrchestrator;
  RemoveAnswerFile;
  DelTree(ExpandConstant('{app}\SqlSetupMedia'), True, True, True);   // the media has done its job whatever the outcome
  if Outcome <> 0 then FailSetup(Outcome);
  DeleteFile(ExpandConstant('{app}\SETUP-INCOMPLETE.txt'));
  SetupSucceeded := True;
end;

// Formats AABBCC... as AA:BB:CC...
function GroupFingerprint(const Hex: String): String;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(Hex) do
  begin
    if (I > 1) and ((I mod 2) = 1) then Result := Result + ':';
    Result := Result + Hex[I];
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  Extra, Fp, W: String;
begin
  if (CurPageID = wpFinished) and SetupSucceeded then
  begin
    Extra := '';
    Fp := ReadSetting(SetupDir + '\install-summary.txt', 'caSha256');
    if Fp <> '' then
      Extra := Extra + #13#10 + #13#10 + 'Hotel certificate (SHA-256 fingerprint):' + #13#10 + GroupFingerprint(Fp) + #13#10 +
        'Compare this with what the client setup shows on each PC before you confirm.';
    W := WarningsText;
    if W <> '' then Extra := Extra + #13#10 + #13#10 + 'Please read (also in the log):' + #13#10 + W;
    if IsTrialInstall then Extra := Extra + #13#10 + #13#10 + 'TRIAL install - not for real guest data.';
    if Extra <> '' then WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + Extra;
  end;
end;

procedure DeinitializeSetup();
begin
  RemoveAnswerFile;
end;

// ---------------------------------------------------------------------------------------------------------------------------
// Uninstall: says on screen what is kept (INST-CONTRACT rule 9)
// ---------------------------------------------------------------------------------------------------------------------------
function InitializeUninstall(): Boolean;
begin
  Result := True;
  if UninstallSilent then Exit;
  Result := MsgBox('This removes the HCC service, its firewall rules, its scheduled tasks (including the daily backups) and the program files.' + #13#10 + #13#10 +
    'It does NOT remove, and never will:' + #13#10 +
    '  - SQL Server' + #13#10 +
    '  - the HCC databases (Hcc and HccDocuments)' + #13#10 +
    '  - the backups' + #13#10 +
    '  - the keys (guest ID key, backup key, hotel certificate)' + #13#10 +
    '  - your settings files (appsettings.Production.json, hcc-ops.json)' + #13#10 + #13#10 +
    'Without the daily backup task no new backups are made. Continue?', mbConfirmation, MB_YESNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    MsgBox('HCC was removed from this PC.' + #13#10 + #13#10 +
      'Kept on purpose: SQL Server, the HCC databases, the backups, the keys and certificates, and the settings files (C:\Program Files\HCC\Server\appsettings.Production.json and C:\Program Files\HCC\Operations\hcc-ops.json). The hotel certificate stays trusted on this PC.' + #13#10 +
      'Installing HCC again later finds them and keeps using them.', mbInformation, MB_OK);
end;
