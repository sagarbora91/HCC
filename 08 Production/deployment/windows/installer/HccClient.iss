; Inno Setup 6 - the HCC CLIENT setup (wave INST, lane client). Installs NO HCC program. It
;   1. trusts the hotel CA certificate (that one certificate only) in Local Machine > Trusted Root Certification Authorities,
;   2. puts an "HCC" icon on the common desktop and in the Start menu that opens the server address in an Edge/Chrome app window,
;   3. checks that the server answers (a message only, never a failure).
; It reads HCC-Hotel-CA.cer and hcc-client.json from its own folder (the client kit made by New-HccClientKit.ps1), else asks.
; Build:  ISCC /DAppVersion=x.y.z.w [/DOutputDir=<folder>] HccClient.iss      (Build-Installer.ps1 passes the same names)
; Rules: INST-CONTRACT 4.3. The setup never runs on the development PC (INST-CONTRACT rule 2).

#ifndef AppVersion
  #error AppVersion is not defined. Pass /DAppVersion=x.y.z (Build-Installer.ps1 does).
#endif
#ifndef OutputDir
  #define OutputDir SourcePath + "Output"
#endif
#define TrustScript "..\Hcc-ClientTrust.ps1"

[Setup]
; Its own AppId and folder: never the server installer's (a client setup may even be run on the server PC).
AppId={{5E0C7D41-93B8-4B7A-A6F2-7C1D0B3E8A52}
AppName=HCC (client)
AppVersion={#AppVersion}
AppPublisher=Saagar Traders
DefaultDirName={autopf}\HCC Client
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=HCC-Client-Setup-{#AppVersion}
SetupIconFile=hcc.ico
UninstallDisplayIcon={app}\hcc.ico
UninstallDisplayName=HCC (client)
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
VersionInfoVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription=HCC client setup (certificate and icon, no program)
CloseApplications=no

[Messages]
WelcomeLabel1=HCC on this PC
WelcomeLabel2=This sets up this PC to use the hotel's HCC server.%n%nIt installs no program. It trusts the hotel's own security certificate and puts an HCC icon on the desktop and in the Start menu.%n%nAn administrator of this PC must run it (Windows asks for permission once).

[Files]
Source: "{#TrustScript}"; Flags: dontcopy
Source: "{#TrustScript}"; DestDir: "{app}"; Flags: ignoreversion
Source: "Launch-HCC.vbs"; DestDir: "{app}"; Flags: ignoreversion
Source: "hcc.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{commondesktop}\HCC"; Filename: "{sys}\wscript.exe"; Parameters: """{app}\Launch-HCC.vbs"""; IconFilename: "{app}\hcc.ico"; Comment: "Open HCC"
Name: "{commonprograms}\HCC"; Filename: "{sys}\wscript.exe"; Parameters: """{app}\Launch-HCC.vbs"""; IconFilename: "{app}\hcc.ico"; Comment: "Open HCC"

[UninstallDelete]
Type: files; Name: "{app}\server-url.txt"
Type: files; Name: "{app}\ca-sha256.txt"

[Code]
const
  UrlChars = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-:/';

var
  AskPage: TInputQueryWizardPage;
  CerPage: TInputFileWizardPage;
  ConfirmPage: TWizardPage;
  ConfirmMemo: TNewMemo;
  ConfirmCheck: TNewCheckBox;
  KitCer, KitCfg: String;
  HaveCer, HaveCfg: Boolean;
  Inspected, InspectOk: Boolean;
  InspectError, ApprovedSha, ApprovedUrl, ApprovedHotel, ApprovedSubject, ApprovedDisplay, ApprovedUntil: String;
  ExpectedKnown: Boolean;
  PsExe: String;

function CleanUrlChars(const S: String): Boolean;
var I: Integer;
begin
  Result := S <> '';
  for I := 1 to Length(S) do
    if Pos(S[I], UrlChars) = 0 then Result := False;
end;

function RunTrustScript(const Params, OutFile: String): Boolean;
var Rc: Integer;
begin
  ExtractTemporaryFile('Hcc-ClientTrust.ps1');
  DeleteFile(OutFile);
  Result := Exec(PsExe, '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Hcc-ClientTrust.ps1') + '" ' + Params + ' -OutFile "' + OutFile + '"',
    '', SW_HIDE, ewWaitUntilTerminated, Rc);
  Log('Hcc-ClientTrust ' + Copy(Params, 1, 40) + ' exit=' + IntToStr(Rc));
  Result := Result and (Rc = 0);
end;

function ReadResult(const OutFile, Key: String): String;
var L: TStringList;
    I: Integer;
begin
  Result := '';
  if not FileExists(OutFile) then Exit;
  L := TStringList.Create;
  try
    L.LoadFromFile(OutFile);
    for I := 0 to L.Count - 1 do
      if Pos(Key + '=', L[I]) = 1 then
      begin
        Result := Copy(L[I], Length(Key) + 2, Length(L[I]));
        Break;
      end;
  finally
    L.Free;
  end;
end;

function CurrentCer: String;
begin
  if HaveCer then Result := KitCer else Result := CerPage.Values[0];
end;

function CurrentUrl: String;
begin
  Result := Trim(AskPage.Values[0]);
end;

// Checks the files (changes nothing). Sets InspectOk / InspectError and the approved values.
procedure DoInspect;
var OutF, P: String;
begin
  Inspected := True;
  InspectOk := False;
  OutF := ExpandConstant('{tmp}\inspect.txt');
  P := '-Action Inspect -CerPath "' + CurrentCer + '"';
  if HaveCfg then P := P + ' -ConfigPath "' + KitCfg + '"'
  else begin
    if not CleanUrlChars(CurrentUrl) then
    begin
      InspectError := 'The server address may only contain letters, digits, dots, dashes, a colon and slashes.';
      Exit;
    end;
    P := P + ' -ServerUrl "' + CurrentUrl + '"';
  end;
  RunTrustScript(P, OutF);
  InspectOk := ReadResult(OutF, 'ok') = '1';
  InspectError := ReadResult(OutF, 'error');
  if (not InspectOk) and (InspectError = '') then InspectError := 'The certificate and the address could not be checked.';
  ApprovedSha := ReadResult(OutF, 'sha256');
  ApprovedDisplay := ReadResult(OutF, 'sha256display');
  ApprovedUrl := ReadResult(OutF, 'url');
  ApprovedHotel := ReadResult(OutF, 'hotel');
  ApprovedSubject := ReadResult(OutF, 'subject');
  ApprovedUntil := ReadResult(OutF, 'notAfter');
  ExpectedKnown := ReadResult(OutF, 'expectedKnown') = '1';
  // The address goes into a script and a shortcut: only plain characters, whatever the checker said.
  if InspectOk and not CleanUrlChars(ApprovedUrl) then
  begin
    InspectOk := False;
    InspectError := 'The server address contains characters that are not allowed.';
  end;
end;

procedure UpdateConfirmButtons;
begin
  if WizardForm.CurPageID = ConfirmPage.ID then
    WizardForm.NextButton.Enabled := InspectOk and ConfirmCheck.Checked;
end;

procedure ConfirmCheckClick(Sender: TObject);
begin
  UpdateConfirmButtons;
end;

procedure FillConfirmPage;
var T: String;
begin
  DoInspect;
  ConfirmCheck.Checked := False;
  if not InspectOk then
  begin
    T := 'STOP. Nothing was installed and nothing will be.' + #13#10 + #13#10 + InspectError + #13#10 + #13#10 +
         'Go back and choose the right files, or copy the HCC client kit from the server again.';
    ConfirmCheck.Enabled := False;
  end
  else begin
    T := 'Hotel: ';
    if ApprovedHotel <> '' then T := T + ApprovedHotel else T := T + '(not given)';
    T := T + #13#10 + 'Server address: ' + ApprovedUrl + #13#10 + #13#10 +
         'Certificate: ' + ApprovedSubject + #13#10 + 'Valid until: ' + ApprovedUntil + #13#10 +
         'SHA-256 fingerprint:' + #13#10 + ApprovedDisplay + #13#10 + #13#10;
    if ExpectedKnown then
      T := T + 'The fingerprint is the one written in hcc-client.json. '
    else
      T := T + 'No fingerprint came with the files, so this setup cannot compare it. Compare the fingerprint above with the one printed on the server PC (READ-ME.txt of the kit) before you go on. ';
    T := T + #13#10 + #13#10 +
         'This certificate will be added to "Trusted Root Certification Authorities" of this PC, and nothing else will be. It is limited to the HCC server''s own names and addresses (name-constrained), so it cannot vouch for other websites.';
    ConfirmCheck.Enabled := True;
  end;
  ConfirmMemo.Text := T;
  UpdateConfirmButtons;
end;

procedure InitializeWizard;
begin
  PsExe := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  KitCer := ExpandConstant('{src}\HCC-Hotel-CA.cer');
  KitCfg := ExpandConstant('{src}\hcc-client.json');
  HaveCer := FileExists(KitCer);
  HaveCfg := FileExists(KitCfg);

  AskPage := CreateInputQueryPage(wpWelcome, 'HCC server address', 'The file hcc-client.json was not found next to this setup.',
    'Type the address of the HCC server (for example https://HOTEL-PC:5443/). It must start with https://');
  AskPage.Add('Server address:', False);

  CerPage := CreateInputFilePage(AskPage.ID, 'Hotel certificate', 'The file HCC-Hotel-CA.cer was not found next to this setup.',
    'Choose the hotel certificate file from the HCC client kit.');
  CerPage.Add('Certificate file (HCC-Hotel-CA.cer):', 'Certificate files|*.cer;*.crt|All files|*.*', '.cer');

  ConfirmPage := CreateCustomPage(CerPage.ID, 'Check before you continue', 'Make sure this is your hotel''s server and certificate.');
  ConfirmMemo := TNewMemo.Create(ConfirmPage);
  ConfirmMemo.Parent := ConfirmPage.Surface;
  ConfirmMemo.Left := 0;
  ConfirmMemo.Top := 0;
  ConfirmMemo.Width := ConfirmPage.SurfaceWidth;
  ConfirmMemo.Height := ConfirmPage.SurfaceHeight - ScaleY(32);
  ConfirmMemo.ReadOnly := True;
  ConfirmMemo.ScrollBars := ssVertical;
  ConfirmCheck := TNewCheckBox.Create(ConfirmPage);
  ConfirmCheck.Parent := ConfirmPage.Surface;
  ConfirmCheck.Left := 0;
  ConfirmCheck.Top := ConfirmPage.SurfaceHeight - ScaleY(24);
  ConfirmCheck.Width := ConfirmPage.SurfaceWidth;
  ConfirmCheck.Caption := 'Yes, this is my hotel''s server. Trust this certificate on this PC.';
  ConfirmCheck.OnClick := @ConfirmCheckClick;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = AskPage.ID then Result := HaveCfg;
  if PageID = CerPage.ID then Result := HaveCer;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = ConfirmPage.ID then FillConfirmPage;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = AskPage.ID then
  begin
    if (Lowercase(Copy(CurrentUrl, 1, 8)) <> 'https://') or not CleanUrlChars(CurrentUrl) then
    begin
      MsgBox('The server address must start with https:// and contain only letters, digits, dots, dashes, a colon and slashes.', mbError, MB_OK);
      Result := False;
    end;
  end;
  if CurPageID = CerPage.ID then
  begin
    if not FileExists(CerPage.Values[0]) then
    begin
      MsgBox('Choose the certificate file (HCC-Hotel-CA.cer).', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

// The certificate is trusted BEFORE any file is copied: if it fails nothing is left on the PC.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var OutF: String;
begin
  Result := '';
  // A silent setup shows no confirmation page: it is allowed only with a kit whose json carries the fingerprint.
  if not Inspected then
  begin
    DoInspect;
    if InspectOk and not ExpectedKnown then
    begin
      InspectOk := False;
      InspectError := 'A silent setup needs hcc-client.json with the certificate fingerprint next to it.';
    end;
  end;
  if not InspectOk then
  begin
    Result := 'The HCC setup stopped and changed nothing. ' + InspectError;
    Exit;
  end;
  OutF := ExpandConstant('{tmp}\trust.txt');
  if not RunTrustScript('-Action Trust -CerPath "' + CurrentCer + '" -ExpectedSha256 "' + ApprovedSha + '"', OutF) then
  begin
    Result := 'The hotel certificate could not be trusted on this PC, so nothing was installed. ' + ReadResult(OutF, 'error');
    Exit;
  end;
  Log('Hotel CA ' + ApprovedSha + ': ' + ReadResult(OutF, 'result'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var OutF, Msg: String;
begin
  if CurStep = ssPostInstall then
  begin
    SaveStringToFile(ExpandConstant('{app}\server-url.txt'), ApprovedUrl + #13#10, False);
    SaveStringToFile(ExpandConstant('{app}\ca-sha256.txt'), ApprovedSha + #13#10, False);
    OutF := ExpandConstant('{tmp}\check.txt');
    RunTrustScript('-Action Check -ServerUrl "' + ApprovedUrl + '"', OutF);
    Msg := ReadResult(OutF, 'message');
    if Msg = '' then Msg := 'The server check did not run. The setup is complete.';
    Log('Server check: ' + ReadResult(OutF, 'kind') + ' - ' + Msg);
    SuppressibleMsgBox('HCC is set up on this PC.' + #13#10 + #13#10 + Msg, mbInformation, MB_OK, IDOK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Sha, OutF: String;
    Lines: TStringList;
    Rc: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Sha := '';
    if FileExists(ExpandConstant('{app}\ca-sha256.txt')) then
    begin
      Lines := TStringList.Create;
      try
        Lines.LoadFromFile(ExpandConstant('{app}\ca-sha256.txt'));
        if Lines.Count > 0 then Sha := Trim(Lines[0]);
      finally
        Lines.Free;
      end;
    end;
    if Sha <> '' then
    begin
      // Silent uninstall keeps the certificate (default answer No): only a person who answers Yes removes it.
      if SuppressibleMsgBox('The HCC icon is removed.' + #13#10 + #13#10 + 'Also remove the hotel certificate from this PC? Choose No if other HCC tools on this PC still need it.',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      begin
        OutF := ExpandConstant('{tmp}\untrust.txt');
        Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
          '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Hcc-ClientTrust.ps1') + '" -Action Untrust -Sha256 "' + Sha + '" -OutFile "' + OutF + '"',
          '', SW_HIDE, ewWaitUntilTerminated, Rc);
      end;
    end;
  end;
end;
