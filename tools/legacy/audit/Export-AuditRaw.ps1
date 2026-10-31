<#
 lg-audit: independent raw reader. Dumps the tables the audit needs from each compacted
 Lodge MDB to TSV files in an output folder OUTSIDE the repository (LG-EXTRACT\audit-<date>\raw).
 ID-like columns (IDNumber, PassportNo, VisaNo) are never written: they are replaced by the
 SHA-256 of the normalised value (upper-case, alphanumerics only), or empty when a placeholder.
 Binary columns are skipped. The database password is read at run time from Module1.bas and
 is never printed. One MDB is open at a time.
#>
param(
  [Parameter(Mandatory)] [string] $MdbDir,
  [Parameter(Mandatory)] [string] $OutDir,
  [string] $Module1 = 'E:\Projects\HCC\Existing Software\Forms\Forms\Module1.bas'
)
$ErrorActionPreference = 'Stop'
if ([IO.Path]::GetFullPath($OutDir) -like 'E:\Projects\HCC\worktrees*') { throw 'output must be outside the repositories' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$m = [regex]::Match((Get-Content -LiteralPath $Module1 -Raw), 'LodgeDatabasePassword\s*=\s*"([^"]+)"')
if (-not $m.Success) { throw 'password not found in Module1.bas' }
$pw = $m.Groups[1].Value
$inv = [Globalization.CultureInfo]::InvariantCulture
$sha = [Security.Cryptography.SHA256]::Create()
$hashCols = @('IDNumber','PassportNo','VisaNo')
$tables = @('CustomerInfo','RoomBooking','TransactionEnd','CustomerDeposit','CustomerRefund','SalesBill')

function Norm-Hash([object]$v) {
  if ($null -eq $v -or $v -is [DBNull]) { return '' }
  $n = ([regex]::Replace(([string]$v).ToUpperInvariant(), '[^A-Z0-9]', ''))
  if ($n.Length -lt 4 -or $n -in @('NIL','NONE')) { return '' }
  if ($n -match '^0+$') { return '' }
  return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($n))) -replace '-','').ToLowerInvariant()
}
function Fmt([object]$v) {
  if ($null -eq $v -or $v -is [DBNull]) { return '' }
  if ($v -is [datetime]) { return $v.ToString('yyyy-MM-dd HH:mm:ss', $inv) }
  if ($v -is [single] -or $v -is [double]) { return $v.ToString('R', $inv) }
  if ($v -is [bool]) { return $(if ($v) {'True'} else {'False'}) }
  return ([string]$v) -replace "[`t`r`n]", ' '
}

$files = Get-ChildItem -LiteralPath $MdbDir -Filter 'Lodge??-??.mdb' | Sort-Object Name
foreach ($f in $files) {
  $fy = '20' + $f.BaseName.Substring(5,2) + '-' + $f.BaseName.Substring(8,2)
  $cn = New-Object -ComObject ADODB.Connection
  $cn.Open("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$($f.FullName);Jet OLEDB:Database Password=$pw;Mode=Read")
  try {
    foreach ($t in $tables) {
      $rs = $cn.Execute("SELECT TOP 1 * FROM [$t]")
      $cols = @(); foreach ($fl in $rs.Fields) { if ($fl.Type -notin 128,204,205) { $cols += $fl.Name } }
      $rs.Close()
      $rs = New-Object -ComObject ADODB.Recordset
      $rs.Open("SELECT " + (($cols | ForEach-Object { "[$_]" }) -join ',') + " FROM [$t]", $cn, 0, 1)
      $sb = [Text.StringBuilder]::new()
      [void]$sb.Append(($cols -join "`t") + "`n")
      $n = 0
      while (-not $rs.EOF) {
        $vals = foreach ($c in $cols) { $v = $rs.Fields.Item($c).Value; if ($c -in $hashCols) { Norm-Hash $v } else { Fmt $v } }
        [void]$sb.Append(($vals -join "`t") + "`n"); $n++
        $rs.MoveNext()
      }
      $rs.Close()
      [IO.File]::WriteAllText((Join-Path $OutDir "$fy.$t.tsv"), $sb.ToString(), [Text.UTF8Encoding]::new($false))
      Write-Output "$fy $t rows=$n"
    }
  } finally { $cn.Close() }
}
