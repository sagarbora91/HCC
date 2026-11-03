<#
.SYNOPSIS
  F5 gate step: proves HCC makes ZERO outbound internet connections with net.internetEnabled = false (EGRESS-CONTRACT §3, F5-E1).
  NOT EXECUTED HERE: written in the cloud lane (Linux); run on the hotel / work PC by the F5 integrator (tools/Start-Gate.ps1 -Wave f5) as
  Administrator. Output is pasted into the gate report.

.DESCRIPTION
  1. Sink: the adapter hosts (-Hosts) are pointed at 127.0.0.1 in the hosts file (backup kept, restored at the end) and a local TCP listener on
     -Port (443 by default) counts every connection attempt. Any attempt = a call that got past the gate.
  2. Runs the end-to-end command (-E2E, default: the server test suites that exercise messaging / OTA / documents / web booking) with the
     master switch OFF (the default of a fresh test database).
  3. Logs Get-NetFirewallRule -DisplayName 'HCC Egress Block' and a blocked probe (Test-NetConnection to a public address from this PC is NOT
     the service's view; it is logged for the record only).
  4. Exit 0 only when the listener counted 0 connections and the e2e command succeeded.
#>
[CmdletBinding()]
param(
    [string[]]$Hosts = @('api.whatsapp.example', 'graph.facebook.com', 'api.booking.com', 'supply-xml.booking.com', 'api.expediapartnercentral.com',
                         'api.razorpay.com', 'vision.googleapis.com', 'smtp.gmail.com', 'updates.hcc.example'),
    [int]$Port = 443,
    [string]$E2E = 'dotnet test "08 Production/Hcc.slnx" -maxcpucount:1 --filter "FullyQualifiedName~Messaging|FullyQualifiedName~Ota|FullyQualifiedName~WebBooking|FullyQualifiedName~Documents|FullyQualifiedName~Egress"',
    [string]$Report = (Join-Path $PSScriptRoot ('..\05 Delivery Evidence\F5\egress-proof-' + (Get-Date -Format 'yyyyMMdd-HHmm') + '.txt'))
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$hostsFile = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$backup = $hostsFile + '.hcc-egress-proof.bak'
$lines = New-Object System.Collections.Generic.List[string]
function Say([string]$m) { $l = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss\Z') + ' ' + $m; Write-Host $l; $lines.Add($l) }

Copy-Item -LiteralPath $hostsFile -Destination $backup -Force
$listener = $null
$count = [ref]0
try {
    Add-Content -LiteralPath $hostsFile -Value ("`r`n# HCC egress proof (temporary)`r`n" + (($Hosts | ForEach-Object { "127.0.0.1 $_" }) -join "`r`n"))
    Clear-DnsClientCache -ErrorAction SilentlyContinue
    Say ("Sink: " + ($Hosts -join ', ') + " -> 127.0.0.1:$Port")

    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    $listener.Start()
    $job = Start-ThreadJob -ArgumentList $listener, $count -ScriptBlock {
        param($l, $c)
        while ($true) { try { $client = $l.AcceptTcpClient(); $c.Value++; $client.Dispose() } catch { break } }
    }

    Say "net.internetEnabled = false (fresh test databases: default OFF, migration 4730 guard)"
    Say "E2E: $E2E"
    $e2eOut = Invoke-Expression $E2E 2>&1 | Out-String
    $e2eOk = $LASTEXITCODE -eq 0
    Say ("E2E exit: " + $LASTEXITCODE + " (" + ($(if ($e2eOk) { 'ok' } else { 'FAILED' })) + ")")
    ($e2eOut -split "`r?`n" | Where-Object { $_ -match 'Passed!|Failed!' }) | ForEach-Object { Say "  $_" }

    Start-Sleep -Seconds 2
    Say ("Sink connections counted: " + $count.Value)

    $rule = Get-NetFirewallRule -DisplayName 'HCC Egress Block' -ErrorAction SilentlyContinue
    if ($rule) { Say ("Firewall rule 'HCC Egress Block': Enabled=" + $rule.Enabled + " Action=" + $rule.Action + " Direction=" + $rule.Direction) }
    else { Say "Firewall rule 'HCC Egress Block': MISSING (run deployment\windows\Set-HccEgress.ps1 -State Install)" }
    $probe = Test-NetConnection -ComputerName 1.1.1.1 -Port 443 -WarningAction SilentlyContinue
    Say ("Probe 1.1.1.1:443 from this shell (not the service): TcpTestSucceeded=" + $probe.TcpTestSucceeded)

    $pass = $e2eOk -and $count.Value -eq 0
    Say ("EGRESS PROOF: " + $(if ($pass) { 'PASS (0 outbound connections)' } else { 'FAIL' }))
    if (-not $pass) { $script:exit = 1 } else { $script:exit = 0 }
} finally {
    if ($listener) { $listener.Stop() }
    Copy-Item -LiteralPath $backup -Destination $hostsFile -Force
    Remove-Item -LiteralPath $backup -Force
    Clear-DnsClientCache -ErrorAction SilentlyContinue
    $dir = Split-Path -Parent $Report
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $lines | Set-Content -LiteralPath $Report -Encoding UTF8
    Write-Host "Report: $Report"
}
exit $script:exit
