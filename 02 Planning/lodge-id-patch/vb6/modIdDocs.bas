Attribute VB_Name = "modIdDocs"
Option Explicit
'==========================================================================================================
' modIdDocs.bas  -  NEW module for Hotel.vbp (Lodge).   DESIGN DRAFT written 2 Nov 2026. NOT COMPILED, NOT RUN.
' Purpose : one place that stores, finds, opens and deletes guest ID documents and photos.
'           Replaces the 46 App.Path & "\IDProof\..." and 64 App.Path & "\Photo\..." strings in the forms.
' Needs   : ADO 2.8 (already referenced). Works against LodgeIdDocs on SQL Server 2008 R2 (stored procedures from script 03)
'           or against LodgeIdDocs.mdb in Access mode (same table names; the procedures are replaced by the SQL in the *_Jet branches).
' Config  : file IdDocs.cfg next to the EXE, 4 lines:  1) SQL or ACC   2) server name or full path of LodgeIdDocs.mdb
'           3) login (SQL mode only)  4) password (SQL mode only; stored encrypted with the existing Encrypt()/Decrypt() of Module1)
'           If IdDocs.cfg is missing the module stays OFF (IdDocsEnabled = False) and every form behaves exactly as today.
'==========================================================================================================
Public dbDocs As ADODB.Connection
Public IdDocsEnabled As Boolean
Private mRoot As String
Private mMaxBytes As Long
Private mAllowed As String
Private mFan As Long

Private Declare Function MakeSureDirectoryPathExists Lib "imagehlp.dll" (ByVal lpPath As String) As Long
Private Declare Function ShellExecute Lib "shell32.dll" Alias "ShellExecuteA" (ByVal hWnd As Long, ByVal lpOperation As String, ByVal lpFile As String, ByVal lpParameters As String, ByVal lpDirectory As String, ByVal nShowCmd As Long) As Long
Private Declare Function CryptAcquireContext Lib "advapi32.dll" Alias "CryptAcquireContextA" (phProv As Long, ByVal pszContainer As String, ByVal pszProvider As String, ByVal dwProvType As Long, ByVal dwFlags As Long) As Long
Private Declare Function CryptCreateHash Lib "advapi32.dll" (ByVal hProv As Long, ByVal Algid As Long, ByVal hKey As Long, ByVal dwFlags As Long, phHash As Long) As Long
Private Declare Function CryptHashData Lib "advapi32.dll" (ByVal hHash As Long, pbData As Any, ByVal dwDataLen As Long, ByVal dwFlags As Long) As Long
Private Declare Function CryptGetHashParam Lib "advapi32.dll" (ByVal hHash As Long, ByVal dwParam As Long, pbData As Any, pdwDataLen As Long, ByVal dwFlags As Long) As Long
Private Declare Function CryptDestroyHash Lib "advapi32.dll" (ByVal hHash As Long) As Long
Private Declare Function CryptReleaseContext Lib "advapi32.dll" (ByVal hProv As Long, ByVal dwFlags As Long) As Long
Private Const PROV_RSA_AES As Long = 24
Private Const CRYPT_VERIFYCONTEXT As Long = &HF0000000
Private Const CALG_SHA_256 As Long = &H800C
Private Const HP_HASHVAL As Long = 2

'---------------------------------------------------------------------------------------------------
' Called once from Module1.Main after Call DatabaseConnection (before frmlogin.Show).
'---------------------------------------------------------------------------------------------------
Public Sub IdDocsOpen()
    Dim f As Integer, kind As String, target As String, usr As String, pwd As String, rs As ADODB.Recordset
    On Error GoTo Fail
    IdDocsEnabled = False
    If Dir(App.Path & "\IdDocs.cfg") = "" Then Exit Sub
    f = FreeFile
    Open App.Path & "\IdDocs.cfg" For Input As #f
    Line Input #f, kind: Line Input #f, target
    If Not EOF(f) Then Line Input #f, usr
    If Not EOF(f) Then Line Input #f, pwd
    Close #f
    Set dbDocs = New ADODB.Connection
    dbDocs.CursorLocation = adUseClient
    If UCase$(Trim$(kind)) = "SQL" Then
        dbDocs.Open "Provider=SQLOLEDB.1;Data Source=" & Trim$(target) & ";Initial Catalog=LodgeIdDocs;User ID=" & Trim$(usr) & ";Password=" & Decrypt(Trim$(pwd))
    Else
        dbDocs.Open "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" & Trim$(target) & ";Jet OLEDB:Database Password=" & Decrypt(Trim$(pwd))
    End If
    Set rs = dbDocs.Execute("SELECT SettingKey, SettingValue FROM IdDocSetting")
    mFan = 1000: mMaxBytes = 8388608: mAllowed = ",jpg,jpeg,png,bmp,gif,pdf,"
    Do While Not rs.EOF
        Select Case rs!SettingKey
            Case "RootPath": mRoot = rs!SettingValue
            Case "MaxBytes": mMaxBytes = CLng(rs!SettingValue)
            Case "AllowedExt": mAllowed = "," & LCase$(rs!SettingValue) & ","
            Case "FolderFan": mFan = CLng(rs!SettingValue)
        End Select
        rs.MoveNext
    Loop
    rs.Close
    If Right$(mRoot, 1) = "\" Then mRoot = Left$(mRoot, Len(mRoot) - 1)
    If Dir(mRoot, vbDirectory) = "" Then Err.Raise 76, , "ID store folder not reachable: " & mRoot
    IdDocsEnabled = True
    Exit Sub
Fail:
    IdDocsEnabled = False
    MsgBox "ID document store is OFF: " & Err.Description & vbCrLf & "Guest ID pictures will be kept the old way until this is fixed.", vbExclamation
End Sub

Public Function IdDocsRoot() As String
    IdDocsRoot = mRoot
End Function

'---------------------------------------------------------------------------------------------------
' Save one document. Returns the new IdDocumentId, or 0 on failure (message already shown).
'   srcFile  = the file the user picked or the webcam temp file (the staging name is no longer shared between PCs: see TempWork())
'   docType  = 'AADHAAR','DL','VOTER','PAN','PASSPORT','VISA','OFFICE-ID','OTHER','PHOTO'
'   via      = 'FILE','WEBCAM','SCAN'
'---------------------------------------------------------------------------------------------------
Public Function IdDocSave(ByVal GuestID As Long, ByVal BookingID As Long, ByVal TransactionID As Long, ByVal docType As String, _
                          ByVal SrNo As Integer, ByVal srcFile As String, ByVal via As String) As Long
    Dim ext As String, folderRel As String, fileName As String, dst As String, tmp As String, hash As String
    Dim size As Long, newId As Long, k As Integer, cmd As ADODB.Command
    On Error GoTo Fail
    If Not IdDocsEnabled Then Exit Function
    If GuestID <= 0 Then Err.Raise 5, , "Guest must be saved before an ID can be attached."
    If Dir(srcFile) = "" Then Err.Raise 53, , "Source file not found."
    ext = LCase$(Mid$(srcFile, InStrRev(srcFile, ".") + 1))
    If InStr(mAllowed, "," & ext & ",") = 0 Then Err.Raise 5, , "File type ." & ext & " is not allowed."
    size = FileLen(srcFile)
    If size < 1 Then Err.Raise 5, , "The picture file is empty (0 bytes)."
    If size > mMaxBytes Then Err.Raise 5, , "The file is larger than " & (mMaxBytes \ 1048576) & " MB."

    folderRel = CStr(GuestID \ mFan) & "\" & CStr(GuestID)
    ' next free version number so an older file is never overwritten
    For k = 1 To 99
        fileName = GuestID & "-" & docType & "-" & SrNo & "-" & k & "." & ext
        If Dir(mRoot & "\" & folderRel & "\" & fileName) = "" Then Exit For
    Next k
    dst = mRoot & "\" & folderRel & "\" & fileName
    MakeSureDirectoryPathExists dst            ' needs the trailing file part to create the folders above it
    tmp = dst & ".part"
    FileCopy srcFile, tmp
    If FileLen(tmp) <> size Then Kill tmp: Err.Raise 57, , "Copy to the ID store was incomplete."
    hash = IdDocHashFile(tmp)
    Name tmp As dst                            ' atomic rename: no half-written file ever has the final name

    Set cmd = New ADODB.Command
    Set cmd.ActiveConnection = dbDocs
    cmd.CommandType = adCmdStoredProc
    cmd.CommandText = "usp_IdDocument_Add"      ' Access mode: replace with the INSERT/UPDATE pair in one dbDocs.BeginTrans ... CommitTrans
    cmd.Parameters.Append cmd.CreateParameter("@RETURN", adInteger, adParamReturnValue)
    cmd.Parameters.Append cmd.CreateParameter("@GuestID", adInteger, adParamInput, , GuestID)
    cmd.Parameters.Append cmd.CreateParameter("@FYCode", adVarChar, adParamInput, 7, IdDocFYCode())
    cmd.Parameters.Append cmd.CreateParameter("@BookingID", adInteger, adParamInput, , IIf(BookingID = 0, Null, BookingID))
    cmd.Parameters.Append cmd.CreateParameter("@TransactionID", adInteger, adParamInput, , IIf(TransactionID = 0, Null, TransactionID))
    cmd.Parameters.Append cmd.CreateParameter("@DocTypeCode", adVarChar, adParamInput, 12, docType)
    cmd.Parameters.Append cmd.CreateParameter("@SrNo", adSmallInt, adParamInput, , SrNo)
    cmd.Parameters.Append cmd.CreateParameter("@SideNo", adTinyInt, adParamInput, , 1)
    cmd.Parameters.Append cmd.CreateParameter("@FolderRel", adVarWChar, adParamInput, 200, folderRel)
    cmd.Parameters.Append cmd.CreateParameter("@FileName", adVarWChar, adParamInput, 200, fileName)
    cmd.Parameters.Append cmd.CreateParameter("@OriginalName", adVarWChar, adParamInput, 260, Mid$(srcFile, InStrRev(srcFile, "\") + 1))
    cmd.Parameters.Append cmd.CreateParameter("@FileExt", adVarChar, adParamInput, 8, ext)
    cmd.Parameters.Append cmd.CreateParameter("@SizeBytes", adInteger, adParamInput, , size)
    cmd.Parameters.Append cmd.CreateParameter("@Sha256Hex", adChar, adParamInput, 64, hash)
    cmd.Parameters.Append cmd.CreateParameter("@CapturedVia", adVarChar, adParamInput, 10, via)
    cmd.Parameters.Append cmd.CreateParameter("@User", adVarWChar, adParamInput, 60, CurrentLoginName())
    cmd.Parameters.Append cmd.CreateParameter("@Workstation", adVarWChar, adParamInput, 60, Environ$("COMPUTERNAME"))
    cmd.Parameters.Append cmd.CreateParameter("@NewId", adInteger, adParamOutput)
    cmd.Execute , , adExecuteNoRecords
    newId = cmd.Parameters("@NewId").Value
    IdDocSave = newId
    Exit Function
Fail:
    Dim msg As String: msg = Err.Description
    On Error Resume Next
    If Len(dst) > 0 Then If Dir(dst) <> "" And newId = 0 Then Kill dst            ' no file without a row
    If Len(tmp) > 0 Then If Dir(tmp) <> "" Then Kill tmp
    MsgBox "The ID could not be saved: " & msg, vbExclamation
    IdDocSave = 0
End Function

'---------------------------------------------------------------------------------------------------
' Full path of the current document of a guest (used by the viewer, the check-in screen and the bill / registration-card printing
' in frmSalesBillReport.filldata and filldata_Work instead of App.Path & "\IDProof\" & rsdisp!IDProofPath).
' Returns "" if there is none. Writes a VIEW (or PRINT) audit row.
'---------------------------------------------------------------------------------------------------
Public Function IdDocPath(ByVal GuestID As Long, ByVal docType As String, ByVal SrNo As Integer, ByVal purpose As String) As String
    Dim rs As ADODB.Recordset, id As Long
    On Error GoTo Fail
    If Not IdDocsEnabled Then Exit Function
    Set rs = dbDocs.Execute("SELECT TOP 1 IdDocumentId, FolderRel, FileName FROM IdDocument WHERE GuestID=" & CLng(GuestID) & _
        " AND DocTypeCode='" & Replace(docType, "'", "''") & "' AND SrNo=" & SrNo & " AND IsCurrent=1 AND IsDeleted=0 ORDER BY IdDocumentId DESC")
    If rs.EOF Then rs.Close: Exit Function
    id = rs!IdDocumentId: IdDocPath = mRoot & "\" & rs!FolderRel & "\" & rs!FileName
    rs.Close
    If Dir(IdDocPath) = "" Then IdDocAudit id, GuestID, "ERROR", "file missing on disk": IdDocPath = "": Exit Function
    IdDocAudit id, GuestID, IIf(purpose = "PRINT", "PRINT", "VIEW"), purpose
    Exit Function
Fail:
    IdDocPath = ""
End Function

Public Sub IdDocShow(ByVal GuestID As Long, ByVal docType As String, ByVal SrNo As Integer)
    Dim p As String
    p = IdDocPath(GuestID, docType, SrNo, "CHECKIN")
    If p = "" Then MsgBox "No ID picture on file.", vbInformation: Exit Sub
    ShellExecute 0&, vbNullString, p, vbNullString, vbNullString, 1       ' was: ShellExecute ... txtItProff.text (frmRoomBooking / frmCustomerMaster cmdView_Click)
End Sub

Public Sub IdDocSoftDelete(ByVal IdDocumentId As Long, ByVal reason As String)
    ' Owner / manager only: check the existing rights table (UserMaster / right flags) in the calling form before showing the button.
    Dim cmd As ADODB.Command
    Set cmd = New ADODB.Command
    Set cmd.ActiveConnection = dbDocs
    cmd.CommandType = adCmdStoredProc: cmd.CommandText = "usp_IdDocument_SoftDelete"
    cmd.Parameters.Append cmd.CreateParameter("@RETURN", adInteger, adParamReturnValue)
    cmd.Parameters.Append cmd.CreateParameter("@IdDocumentId", adInteger, adParamInput, , IdDocumentId)
    cmd.Parameters.Append cmd.CreateParameter("@User", adVarWChar, adParamInput, 60, CurrentLoginName())
    cmd.Parameters.Append cmd.CreateParameter("@Reason", adVarWChar, adParamInput, 200, reason)
    cmd.Parameters.Append cmd.CreateParameter("@Workstation", adVarWChar, adParamInput, 60, Environ$("COMPUTERNAME"))
    cmd.Execute , , adExecuteNoRecords
End Sub

'---------------------------------------------------------------------------------------------------
' helpers
'---------------------------------------------------------------------------------------------------
Private Sub IdDocAudit(ByVal id As Long, ByVal GuestID As Long, ByVal act As String, ByVal detail As String)
    On Error Resume Next
    dbDocs.Execute "INSERT INTO IdDocumentAudit (IdDocumentId, GuestID, Action, ActionBy, Workstation, Detail) VALUES (" & id & "," & GuestID & ",'" & act & "','" & _
        Replace(CurrentLoginName(), "'", "''") & "','" & Replace(Environ$("COMPUTERNAME"), "'", "''") & "','" & Replace(Left$(detail, 200), "'", "''") & "')"
    ' SQL mode: the app login has no INSERT right on the table (script 02), so this line is replaced by usp_IdDocument_Open / usp_IdDocument_LogPrint calls.
End Sub

Private Function CurrentLoginName() As String
    ' Module1.bas line 156: Public Pusercode As Long, PuserName As String  (signed-in user). strUserName is the SQL login, NOT this.
    CurrentLoginName = Trim$(PuserName)
End Function

Private Function IdDocFYCode() As String
    ' DatabaseName is "Lodge26-27.mdb" in Access mode or "Lodge26-27" in SQL mode  ->  "2026-27"
    Dim s As String: s = DatabaseName
    s = Replace(Replace(LCase$(s), "lodge", ""), ".mdb", "")
    If Len(s) >= 5 Then IdDocFYCode = "20" & Left$(s, 2) & "-" & Mid$(s, 4, 2)
End Function

' per-PC private staging file so two reception PCs never overwrite each other's CustomerIDProof.jpg (today they can, on a shared EXE folder)
Public Function TempWork(ByVal tag As String) As String
    TempWork = Environ$("TEMP") & "\LodgeId_" & Environ$("COMPUTERNAME") & "_" & tag & ".jpg"
End Function

Public Function IdDocHashFile(ByVal path As String) As String
    Dim f As Integer, buf() As Byte, n As Long, hProv As Long, hHash As Long, out(0 To 31) As Byte, outLen As Long, i As Integer, s As String
    f = FreeFile
    Open path For Binary Access Read As #f
    n = LOF(f)
    If n = 0 Then Close #f: Exit Function
    ReDim buf(0 To n - 1)
    Get #f, , buf
    Close #f
    If CryptAcquireContext(hProv, vbNullString, vbNullString, PROV_RSA_AES, CRYPT_VERIFYCONTEXT) = 0 Then Err.Raise 5, , "CryptoAPI not available"
    If CryptCreateHash(hProv, CALG_SHA_256, 0, 0, hHash) = 0 Then CryptReleaseContext hProv, 0: Err.Raise 5, , "SHA-256 not available on this Windows"
    CryptHashData hHash, buf(0), n, 0
    outLen = 32
    CryptGetHashParam hHash, HP_HASHVAL, out(0), outLen, 0
    For i = 0 To 31: s = s & Right$("0" & Hex$(out(i)), 2): Next i
    CryptDestroyHash hHash: CryptReleaseContext hProv, 0
    IdDocHashFile = LCase$(s)
End Function
'==========================================================================================================
' HOW THE FORMS CALL IT (exact edit list is in HCC_Lodge_ID_Storage_Analysis_and_Patch_Design.md section 6)
'   Pick / capture button   : FileCopy oldfile, TempWork("ID")         instead of  newfile = App.Path & "\IDProof\CustomerIDProof.jpg"
'   Save of the guest       : If IdDocsEnabled Then id = IdDocSave(GuestID, BookingID, 0, "AADHAAR", 1, TempWork("ID"), "FILE")
'                             (and keep the old FileCopy + IDProofPath write while IdDocsEnabled = False, so the patch can be switched off)
'   View button             : IdDocShow GuestID, "AADHAAR", 1
'   Bill / card print       : path = IdDocPath(GuestID, type, sr, "PRINT")  in frmSalesBillReport.filldata / filldata_Work
'==========================================================================================================
