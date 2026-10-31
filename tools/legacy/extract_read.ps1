#requires -Version 7
<#
  extract_read.ps1 - read-only dump of the legacy Lodge tables of ONE Access file to UTF-8 CSV.
  Called by extract.py (one call per financial-year file). One SELECT * per table (SELECT * so
  that older files with fewer columns still read; extract.py picks columns by name).
  The database password is read at run time from the legacy Module1.bas and is never printed,
  logged or written anywhere.
#>
param(
  [Parameter(Mandatory)][string]$MdbPath,
  [Parameter(Mandatory)][string]$OutDir,
  [string]$PasswordSource = 'E:\Projects\HCC\Existing Software\Forms\Forms\Module1.bas',
  [string[]]$Tables = @('CustomerInfo','RoomBooking','RoomBookingDetail','RoomMaster','PackageMaster',
    'TransactionData','TransactionEnd','TransactionRoomShift','RateChangeDetail','CustomerDeposit',
    'CustomerRefund','CustomerOpening','PaidOut','ExpensesMaster','GroupMaster','IdentityType',
    'Citymaster','Statemaster','PurposeOFVisitMaster','GSTR1')
)
$ErrorActionPreference = 'Stop'
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$text = [System.IO.File]::ReadAllText($PasswordSource)
$m = [regex]::Match($text, 'LodgeDatabasePassword\s*=\s*"([^"]+)"')
if (-not $m.Success) { throw 'password constant not found in the legacy source' }
$pw = $m.Groups[1].Value
if (-not (Test-Path -LiteralPath $MdbPath)) { throw "database not found: $MdbPath" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Format-Cell($v) {
  if ($null -eq $v -or $v -is [System.DBNull]) { return '' }
  if ($v -is [byte[]]) { return '' }
  if ($v -is [datetime]) { return $v.ToString('yyyy-MM-dd HH:mm:ss', $inv) }
  if ($v -is [bool]) { return $(if ($v) { '1' } else { '0' }) }
  if ($v -is [single] -or $v -is [double]) { return $v.ToString('R', $inv) }
  if ($v -is [IFormattable]) { return $v.ToString($null, $inv) }
  return [string]$v
}
function Quote($s) {
  if ($s.IndexOfAny([char[]]@(',','"',"`r","`n")) -ge 0) { return '"' + $s.Replace('"','""') + '"' }
  return $s
}

$cn = New-Object -ComObject ADODB.Connection
try {
  $cn.Open("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$MdbPath;Mode=Read;Jet OLEDB:Database Password=$pw")
} catch {
  throw ('cannot open ' + (Split-Path $MdbPath -Leaf) + ': ' + $_.Exception.Message.Replace($pw, '***'))
}
$pw = $null
foreach ($t in $Tables) {
  $rs = New-Object -ComObject ADODB.Recordset
  try {
    $rs.Open("SELECT * FROM [$t]", $cn, 0, 1)   # forward-only, read-only
  } catch {
    Write-Output "MISSING`t$t"
    continue
  }
  $path = Join-Path $OutDir ($t + '.csv')
  $sw = New-Object System.IO.StreamWriter($path, $false, $utf8)
  $sw.NewLine = "`n"
  $n = $rs.Fields.Count
  $names = New-Object string[] $n
  for ($i = 0; $i -lt $n; $i++) { $names[$i] = $rs.Fields.Item($i).Name }
  $sw.WriteLine((($names | ForEach-Object { Quote $_ }) -join ','))
  $rows = 0
  $cells = New-Object string[] $n
  while (-not $rs.EOF) {
    for ($i = 0; $i -lt $n; $i++) { $cells[$i] = Quote (Format-Cell $rs.Fields.Item($i).Value) }
    $sw.WriteLine(($cells -join ','))
    $rows++
    $rs.MoveNext()
  }
  $sw.Close()
  $rs.Close()
  Write-Output "OK`t$t`t$rows"
}
$cn.Close()
[void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($cn)
[GC]::Collect(); [GC]::WaitForPendingFinalizers()
exit 0
