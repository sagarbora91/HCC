; Inno Setup 6 template for the HCC server installer (outline - compiled only if Inno Setup is installed: see Build-Installer.ps1).
; ETP-REUSE: replace/align with ETP's installer template (branding, prerequisites check, upgrade/uninstall rules) when V:\ETP is available.
;
; Expects (Build-Installer.ps1 prepares them):  {#PublishDir}  = dotnet publish output of src\Hcc.Api (framework-dependent, win-x64)
;                                               {#OpsDir}      = deployment\windows (scripts)
; The installer deliberately does NOT: install SQL Server, create the certificate or the first Owner, or start the service -
; those need owner choices and are the numbered steps in INSTALL.md. It copies files, keeps existing settings on upgrade and
; stops/starts the service around an upgrade.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\publish"
#endif
#ifndef OpsDir
  #define OpsDir ".."
#endif

[Setup]
AppId={{B6A3E3A2-6C54-4F0E-9B1A-2D8F4C7A0C11}
AppName=Hotel Control Centre Server
AppVersion={#AppVersion}
AppPublisher=Saagar Traders
DefaultDirName={autopf}\HCC
DisableDirPage=auto
DefaultGroupName=HCC
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
OutputBaseFilename=HCC-Server-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Hotel Control Centre Server

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}\Server"; Excludes: "appsettings.Production.json"; Flags: recursesubdirs ignoreversion
Source: "{#OpsDir}\*.ps1"; DestDir: "{app}\Operations"; Flags: ignoreversion
Source: "{#OpsDir}\hcc-ops.sample.json"; DestDir: "{app}\Operations"; Flags: ignoreversion
Source: "{#OpsDir}\appsettings.Production.sample.json"; DestDir: "{app}\Server"; Flags: ignoreversion
Source: "{#OpsDir}\INSTALL.md"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "{#OpsDir}\ANDROID_SERVER_ADDRESS.md"; DestDir: "{app}"; Flags: ignoreversion
; Settings are created once from the samples and never overwritten on upgrade.
Source: "{#OpsDir}\appsettings.Production.sample.json"; DestDir: "{app}\Server"; DestName: "appsettings.Production.json"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#OpsDir}\hcc-ops.sample.json"; DestDir: "{app}\Operations"; DestName: "hcc-ops.json"; Flags: onlyifdoesntexist uninsneveruninstall

[Dirs]
Name: "{app}\Server\logs"
Name: "{app}\Server\web"

[Run]
; Upgrade path: if the service already exists it was stopped in PrepareToInstall; start it again.
Filename: "{sys}\sc.exe"; Parameters: "start HccServer"; Flags: runhidden; Check: ServiceExists
; Fresh install: the owner follows INSTALL.md (database, service account, certificate, first Owner, backups).

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop HccServer"; Flags: runhidden; RunOnceId: "StopHcc"
; The service entry, the database and the backups are never removed by the uninstaller.

[Code]
function ServiceExists: Boolean;
var rc: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query HccServer', '', SW_HIDE, ewWaitUntilTerminated, rc) and (rc = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var rc: Integer;
begin
  Result := '';
  if ServiceExists then
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop HccServer', '', SW_HIDE, ewWaitUntilTerminated, rc);
end;
