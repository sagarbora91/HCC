# A08 bounded probe: projector poll cost on a year-sized outbox. Creates HccTest_A08outbox_<stamp> on .\SQLEXPRESS, runs
# A08_outbox_poll.sql, drops the database. Touches no other database. Run from the repo root:
#   pwsh "05 Delivery Evidence/Audit/probes/A08_outbox_poll.ps1" -Rows 365000
param([int]$Rows = 365000)
$ErrorActionPreference = 'Stop'
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
$db = "HccTest_A08outbox_$stamp"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here "..\evidence\A08\outbox_poll_$Rows.txt"
try {
  sqlcmd -S .\SQLEXPRESS -E -C -b -Q "CREATE DATABASE [$db];" | Out-Null
  sqlcmd -S .\SQLEXPRESS -E -C -b -I -d $db -v Rows=$Rows -i (Join-Path $here 'A08_outbox_poll.sql') | Out-File -Encoding utf8 $out
}
finally {
  sqlcmd -S .\SQLEXPRESS -E -C -Q "IF DB_ID('$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END" | Out-Null
}
"database $db dropped; output $out"
