<#
.SYNOPSIS
  First-time hotel setup: creates the hotel (property) and the first Owner account on an EMPTY HCC installation, in one step.
.DESCRIPTION
  Run on the server PC itself, after Initialize-HccDatabase.ps1, "Hcc.Api.exe migrate" and "Start-Service HccServer".
  It calls the server's setup command on http://127.0.0.1:5080 (the server only accepts it from this PC and only while no hotel
  exists yet) with: hotel name, address, GSTIN (optional), state (default Maharashtra), business date (default today),
  time zone (default Asia/Kolkata), standard check-in / checkout times, and the Owner's sign-in name and display name.
  The Owner password is asked for twice and never shown, written to a file or kept in the PowerShell history.
  The same values fill Master configuration (hotel name, address, GSTIN, check-in/checkout), which the Owner can change later.
  Running it again after success is refused by the server ("already set up"); nothing is changed.
  -WhatIf prints the request (password hidden) and sends nothing.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$HotelName,
    [Parameter(Mandatory)][string]$Address,
    [string]$Gstin = '',
    [string]$State = 'Maharashtra',
    [string]$BusinessDate = '',                       # yyyy-MM-dd; empty = today in the hotel's time zone
    [string]$TimeZone = 'Asia/Kolkata',
    [string]$CheckInTime = '14:00',
    [string]$CheckOutTime = '11:00',
    [string]$OwnerLogin = 'owner',
    [Parameter(Mandatory)][string]$OwnerDisplayName,
    [string]$Code = 'MAIN',
    [string]$ServerUrl = 'http://127.0.0.1:5080'
)
$ErrorActionPreference = 'Stop'
$uri = [Uri]$ServerUrl
if (-not ($uri.IsLoopback)) { throw "First-time setup only works on the server PC itself: use http://127.0.0.1:5080, not $ServerUrl." }
if ($BusinessDate -and $BusinessDate -notmatch '^\d{4}-\d{2}-\d{2}$') { throw 'BusinessDate must be yyyy-MM-dd, for example 2026-11-01.' }
$base = $ServerUrl.TrimEnd('/') + '/api/v1/setup'

$body = [ordered]@{
    name = $HotelName; address = $Address; ownerLoginName = $OwnerLogin; ownerDisplayName = $OwnerDisplayName; ownerPassword = '********'
    code = $Code; state = $State; timeZone = $TimeZone; checkInTime = $CheckInTime; checkOutTime = $CheckOutTime
}
if ($Gstin) { $body.gstin = $Gstin.ToUpperInvariant() }
if ($BusinessDate) { $body.businessDate = $BusinessDate }

if (-not $PSCmdlet.ShouldProcess("$base/SetupProperty", "Set up hotel '$HotelName' with first Owner '$OwnerLogin'")) {
    Write-Host "DRY RUN: GET $base/status (must say setupRequired)"
    Write-Host ("DRY RUN: POST $base/SetupProperty " + ($body | ConvertTo-Json -Compress))
    return
}

$status = Invoke-RestMethod -Method Get -Uri "$base/status"
if (-not $status.setupRequired) { throw 'This server is already set up (a hotel exists). Sign in as the Owner and change details in Master configuration.' }

function Read-Secret([string]$Prompt) {
    $s = Read-Host -Prompt $Prompt -AsSecureString
    $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}
$p1 = Read-Secret "Owner password for '$OwnerLogin' (12 or more characters)"
$p2 = Read-Secret 'Type the same password again'
if ($p1 -ne $p2) { throw 'The two passwords are different. Nothing was changed; run the script again.' }
$body.ownerPassword = $p1
$json = $body | ConvertTo-Json -Compress
$key = 'setup-' + [Guid]::NewGuid().ToString('N')
try {
    $r = Invoke-RestMethod -Method Post -Uri "$base/SetupProperty" -ContentType 'application/json; charset=utf-8' -Headers @{ 'Idempotency-Key' = $key } -Body ([Text.Encoding]::UTF8.GetBytes($json))
}
catch {
    $detail = if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { $_.Exception.Message }
    throw "Setup was refused: $detail"
}
finally { $p1 = $null; $p2 = $null; $body.ownerPassword = $null; $json = $null }
Write-Host $r.message
Write-Host ("Hotel number {0} ({1}), business date {2}, Owner sign-in name '{3}'." -f $r.propertyId, $r.code, $r.businessDate, $r.ownerLoginName)
