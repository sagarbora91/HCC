' Opens HCC (the hotel server address in server-url.txt, written by the client setup) in its own browser app window.
' Order: Microsoft Edge --app, then Google Chrome --app, then the default browser. Own profile folder, so HCC never mixes with the
' person's normal browsing. Per-user data (sign-in, cached pages) stays in that profile. Installed in a folder only administrators can change.
Option Explicit
Dim sh, fso, here, url, profile, exe, cmd
Set sh = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
here = fso.GetParentFolderName(WScript.ScriptFullName)
url = ""
If fso.FileExists(here & "\server-url.txt") Then
  With fso.OpenTextFile(here & "\server-url.txt", 1)
    url = Trim(.ReadLine)
    .Close
  End With
End If
If LCase(Left(url, 8)) <> "https://" Or InStr(url, """") > 0 Or InStr(url, " ") > 0 Then
  MsgBox "HCC cannot start: the server address is missing. Run the HCC client setup again.", 48, "HCC"
  WScript.Quit 1
End If
profile = sh.ExpandEnvironmentStrings("%LOCALAPPDATA%") & "\HCC\Browser"

Function RegPath(name)
  On Error Resume Next
  RegPath = ""
  RegPath = sh.RegRead("HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" & name & "\")
  If Err.Number <> 0 Then RegPath = ""
  If RegPath <> "" Then If Not fso.FileExists(RegPath) Then RegPath = ""
End Function

exe = RegPath("msedge.exe")
If exe = "" Then exe = RegPath("chrome.exe")
If exe <> "" Then
  cmd = """" & exe & """ --app=" & url & " --user-data-dir=""" & profile & """ --no-first-run --no-default-browser-check"
  sh.Run cmd, 1, False
Else
  sh.Run url, 1, False
End If
