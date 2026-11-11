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
  InspectOk: Boolean;
  ShownSha, ShownUrl: String;
  InspectError, ApprovedSha, ApprovedUrl, ApprovedHotel, ApprovedSubject, ApprovedDisplay, ApprovedUntil, ApprovedPermitted, TrustResult: String;
  ExpectedKnown: Boolean;
  PsExe: String;

function CleanUrlChars(const S: String): Boolean;
var I: Integer;
begin
  Result := S <> '';
  for I := 1 to Length(S) do
    if Pos(S[I], UrlChars) = 0 then Result := False;
end;

function RunTrustScript(const ScriptPath, Params, OutFile: String): Boolean;
var Rc: Integer;
begin
  DeleteFile(OutFile);
  Result := Exec(PsExe, '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '" ' + Params + ' -OutFile "' + OutFile + '"',
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

function ShortFp(const Sha: String): String;
begin
  Result := Copy(Sha, 1, 8) + '...' + Copy(Sha, 57, 8);
end;

// Removes one certificate (by fingerprint) with the installed script. Returns removed / absent / a failure text.
function UntrustSha(const Sha: String): String;
var OutF: String;
begin
  OutF := ExpandConstant('{tmp}\untrust.txt');
  if RunTrustScript(ExpandConstant('{app}\Hcc-ClientTrust.ps1'), '-Action Untrust -Sha256 "' + Sha + '"', OutF) then
    Result := ReadResult(OutF, 'result')
  else
    Result := 'failed: ' + ReadResult(OutF, 'error');
  if Result = '' then Result := 'failed: no answer';
  Log('Untrust ' + ShortFp(Sha) + ': ' + Result);
end;

function CurrentCer: String;
begin
  if HaveCer then Result := KitCer else Result := CerPage.Values[0];
end;

function CurrentUrl: String;
begin
  Result := Trim(AskPage.Values[0]);
end;

// Checks the files (changes nothing). Sets InspectOk / InspectError and the approved values. Always re-runs the checker.
procedure DoInspect;
var OutF, P: String;
begin
  InspectOk := False;
  ExtractTemporaryFile('Hcc-ClientTrust.ps1');
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
  RunTrustScript(ExpandConstant('{tmp}\Hcc-ClientTrust.ps1'), P, OutF);
  InspectOk := ReadResult(OutF, 'ok') = '1';
  InspectError := ReadResult(OutF, 'error');
  if (not InspectOk) and (InspectError = '') then InspectError := 'The certificate and the address could not be checked.';
  ApprovedSha := ReadResult(OutF, 'sha256');
  ApprovedDisplay := ReadResult(OutF, 'sha256display');
  ApprovedUrl := ReadResult(OutF, 'url');
  ApprovedHotel := ReadResult(OutF, 'hotel');
  ApprovedSubject := ReadResult(OutF, 'subject');
  ApprovedUntil := ReadResult(OutF, 'notAfter');
  ApprovedPermitted := ReadResult(OutF, 'permitted');
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
         'It can vouch only for: ' + ApprovedPermitted + #13#10 + #13#10 +
         'SHA-256 fingerprint:' + #13#10 + ApprovedDisplay + #13#10 + #13#10 +
         'CHECK THIS FIRST: the files on the USB stick cannot prove they are your hotel''s. Compare this fingerprint with the one shown on the SERVER PC (the last page of the HCC server setup, or its install log), not with a file on the stick. If they differ, press Cancel.';
    if ExpectedKnown then
      T := T + #13#10 + '(hcc-client.json carries the same fingerprint, so the files agree with each other.)'
    else
      T := T + #13#10 + '(No fingerprint came with the files, so nothing was compared by this setup.)';
    T := T + #13#10 + #13#10 +
         'If you go on, this certificate is added to "Trusted Root Certification Authorities" of this PC, and nothing else is. It is limited to the HCC server''s own names and addresses listed above (name-constrained), so it cannot vouch for other websites.';
    ConfirmCheck.Enabled := True;
  end;
  ConfirmMemo.Text := T;
  // What the person is shown and ticks: PrepareToInstall must trust exactly this, nothing else.
  if InspectOk then begin ShownSha := ApprovedSha; ShownUrl := ApprovedUrl; end else begin ShownSha := ''; ShownUrl := ''; end;
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
  ConfirmCheck.Caption := 'The fingerprint matches the server PC. Trust this certificate on this PC.';
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
  // The decision itself, not only the button state. A silent setup has no tick box: PrepareToInstall decides there.
  if (CurPageID = ConfirmPage.ID) and not WizardSilent then
    if not (InspectOk and ConfirmCheck.Checked) then Result := False;
end;

// The certificate is trusted BEFORE any file is copied: if it fails nothing is left on the PC.
// Whatever happened on the pages, everything is checked again here.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var OutF: String;
begin
  Result := '';
  DoInspect;
  if not InspectOk then
  begin
    Result := 'The HCC setup stopped and changed nothing. ' + InspectError;
    Exit;
  end;
  if WizardSilent then
  begin
    // No person confirms a silent setup, so the kit must carry the fingerprint (an empty caSha256 counts as none).
    if not ExpectedKnown then
    begin
      Result := 'The HCC setup stopped and changed nothing. A silent setup needs hcc-client.json with the certificate fingerprint (caSha256) next to it.';
      Exit;
    end;
  end
  else begin
    if not ConfirmCheck.Checked then
    begin
      Result := 'The HCC setup stopped and changed nothing. The certificate was not confirmed.';
      Exit;
    end;
    if (ShownSha = '') or (ApprovedSha <> ShownSha) or (ApprovedUrl <> ShownUrl) then
    begin
      Result := 'The files changed after you checked them. Nothing was installed. Run the setup again and check the fingerprint again.';
      Exit;
    end;
  end;
  OutF := ExpandConstant('{tmp}\trust.txt');
  if not RunTrustScript(ExpandConstant('{tmp}\Hcc-ClientTrust.ps1'), '-Action Trust -CerPath "' + CurrentCer + '" -ExpectedSha256 "' + ApprovedSha + '"', OutF) then
  begin
    Result := 'The hotel certificate could not be trusted on this PC, so nothing was installed. ' + ReadResult(OutF, 'error');
    Exit;
  end;
  TrustResult := ReadResult(OutF, 'result');
  Log('Hotel CA ' + ApprovedSha + ': ' + TrustResult);
end;

// {app}\ca-trusted.txt: one line per certificate this setup knows: "<sha256> added" (this setup put it there) or "<sha256> already".
procedure RecordTrusted;
var List: TStringList;
    I, Found: Integer;
    Sha, State, ListFile: String;
begin
  ListFile := ExpandConstant('{app}\ca-trusted.txt');
  List := TStringList.Create;
  try
    if FileExists(ListFile) then List.LoadFromFile(ListFile);
    // A different hotel CA than before: offer to remove the previous one that this setup added.
    for I := List.Count - 1 downto 0 do
    begin
      Sha := Copy(List[I], 1, 64);
      State := Copy(List[I], 66, Length(List[I]));
      if (Sha <> ApprovedSha) and (State = 'added') then
        if SuppressibleMsgBox('A different hotel certificate was installed on this PC earlier (' + ShortFp(Sha) + ').' + #13#10 + #13#10 +
          'Remove the previous hotel certificate? Choose Yes if the hotel now uses the new one.', mbConfirmation, MB_YESNO, IDYES) = IDYES then
        begin
          State := UntrustSha(Sha);
          if (State = 'removed') or (State = 'absent') then List.Delete(I)
          else SuppressibleMsgBox('The previous certificate could not be removed (' + State + '). You can remove it later by uninstalling or in certlm.msc.', mbError, MB_OK, IDOK);
        end;
    end;
    Found := -1;
    for I := 0 to List.Count - 1 do
      if Copy(List[I], 1, 64) = ApprovedSha then Found := I;
    if Found < 0 then List.Add(ApprovedSha + ' ' + TrustResult)
    else if (TrustResult = 'added') and (Copy(List[Found], 66, Length(List[Found])) <> 'added') then List[Found] := ApprovedSha + ' added';
    List.SaveToFile(ListFile);
  finally
    List.Free;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var OutF, Msg: String;
begin
  if CurStep = ssPostInstall then
  begin
    SaveStringToFile(ExpandConstant('{app}\server-url.txt'), ApprovedUrl + #13#10, False);
    RecordTrusted;
    OutF := ExpandConstant('{tmp}\check.txt');
    RunTrustScript(ExpandConstant('{tmp}\Hcc-ClientTrust.ps1'), '-Action Check -ServerUrl "' + ApprovedUrl + '"', OutF);
    Msg := ReadResult(OutF, 'message');
    if Msg = '' then Msg := 'The server check did not run. The setup is complete.';
    Log('Server check: ' + ReadResult(OutF, 'kind') + ' - ' + Msg);
    SuppressibleMsgBox('HCC is set up on this PC.' + #13#10 + #13#10 + Msg, mbInformation, MB_OK, IDOK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Lines: TStringList;
    I, Added: Integer;
    Sha, Report, R: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    PsExe := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
    Lines := TStringList.Create;
    try
      if FileExists(ExpandConstant('{app}\ca-trusted.txt')) then Lines.LoadFromFile(ExpandConstant('{app}\ca-trusted.txt'));
      Added := 0;
      for I := 0 to Lines.Count - 1 do
        if Copy(Lines[I], 66, Length(Lines[I])) = 'added' then Added := Added + 1;
      // Only certificates this setup added are offered. One that was already trusted before stays.
      // A silent uninstall keeps them (default answer No): only a person who answers Yes removes them.
      // The list stays while any added certificate is still trusted, so a later reinstall can offer it again.
      if Added = 0 then DeleteFile(ExpandConstant('{app}\ca-trusted.txt'))
      else if SuppressibleMsgBox('The HCC icon is removed.' + #13#10 + #13#10 + 'Also remove the hotel certificate that this setup added to this PC? Choose No if other HCC tools on this PC still need it.',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      begin
        Report := '';
        Added := 0;
        for I := Lines.Count - 1 downto 0 do
          if Copy(Lines[I], 66, Length(Lines[I])) = 'added' then
          begin
            Sha := Copy(Lines[I], 1, 64);
            R := UntrustSha(Sha);
            Report := Report + ShortFp(Sha) + ': ' + R + #13#10;
            if (R = 'removed') or (R = 'absent') then Lines.Delete(I) else Added := Added + 1;
          end;
        if Added = 0 then DeleteFile(ExpandConstant('{app}\ca-trusted.txt'))
        else Lines.SaveToFile(ExpandConstant('{app}\ca-trusted.txt'));
        SuppressibleMsgBox('Hotel certificate result:' + #13#10 + Report, mbInformation, MB_OK, IDOK);
      end;
    finally
      Lines.Free;
    end;
  end;
end;
