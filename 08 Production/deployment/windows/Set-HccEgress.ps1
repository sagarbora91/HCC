<#
.SYNOPSIS
  HCC internet egress firewall rule (EGRESS-CONTRACT §4, F5-E1). NOT EXECUTED HERE: written and reviewed in the cloud lane (Linux, no
  Windows firewall); first run is on the hotel PC at the F5 gate (tools/egress-proof.ps1) or by the installer.

.DESCRIPTION
  -State Install : creates the outbound rule "HCC Egress Block" for the Hcc.Api service executable (ENABLED = internet blocked; only LAN /
                   loopback ranges stay reachable) and the scheduled task \HCC\ToggleEgress (SYSTEM, on demand, DISABLED until the owner
                   uses the Internet switch). Idempotent.
  -State On      : internet allowed for HCC  -> the block rule is DISABLED.
  -State Off     : internet blocked for HCC  -> the block rule is ENABLED.
  -State Apply   : what the scheduled task runs: reads ops.egress_firewall_state.wanted_state (written by the API when the master switch
                   changes) and applies On / Off, then records applied_state / applied_utc / detail (System health shows "applied").
  -State Status  : prints the rule and task state and the database row.
  The software gate (EgressGateHandler) holds whatever happens here; a missing or failing task only shows "firewall rule not applied".
  Windows Firewall cannot express "everything except these ranges" in one block rule, so the block rule's RemoteAddress is the complement of
  10/8, 172.16/12, 192.168/16, 127/8 (IPv4) and ::1, fe80::/10 (IPv6).
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('On', 'Off', 'Install', 'Status', 'Apply')][string]$State,
    [string]$ServiceExe = (Join-Path $env:ProgramFiles 'HCC\Server\Hcc.Api.exe'),
    [string]$ConfigPath,
    [string]$Instance,
    [string]$Database
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Hcc-Common.ps1"

# -WhatIf without -State previews the Install (the only action that creates anything); a real run still needs -State.
if (-not $State) {
    if ($WhatIfPreference) { $State = 'Install' } else { throw 'Parameter -State is required (On, Off, Install, Status or Apply).' }
}

$RuleName = 'HCC Egress Block'
$TaskPath = '\HCC\'
$TaskName = 'ToggleEgress'

# Complement of the allowed LAN / loopback ranges (IPv4 + IPv6), so "block these" = "allow only LAN".
$BlockedRemote = @(
    '0.0.0.0-9.255.255.255', '11.0.0.0-126.255.255.255', '128.0.0.0-172.15.255.255', '172.32.0.0-192.167.255.255', '192.169.0.0-255.255.255.255',
    '::-::0', '::2-fe7f:ffff:ffff:ffff:ffff:ffff:ffff:ffff', 'fec0::-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff')

function Get-Target {
    $cfg = Get-HccOpsConfig -Path $ConfigPath
    $i = if ($Instance) { $Instance } else { [string]$cfg['SqlInstance'] }
    $d = Assert-HccDatabaseName $(if ($Database) { $Database } else { [string]$cfg['Database'] })
    $trust = Resolve-HccTrustServerCertificate -Instance $i -Explicit $null -Configured $cfg['TrustServerCertificate']
    return @{ Instance = $i; Database = $d; Trust = $trust }
}

function Invoke-EgressSql([string]$Sql, [switch]$Scalar) {
    $t = Get-Target
    return Invoke-HccSql -Instance $t.Instance -Query ("USE " + (ConvertTo-HccSqlName $t.Database) + "; " + $Sql) -TrustServerCertificate:$t.Trust -Scalar:$Scalar
}

function Install-Rule {
    if ($WhatIfPreference) {
        Write-Host "What if: would create or refresh outbound block rule '$RuleName' for $ServiceExe and register disabled scheduled task $TaskPath$TaskName (SYSTEM, on demand)"
        return
    }
    $rule = Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue
    if (-not $rule) {
        New-NetFirewallRule -DisplayName $RuleName -Direction Outbound -Action Block -Program $ServiceExe -RemoteAddress $BlockedRemote `
            -Profile Any -Enabled True -Description 'HCC: blocks internet for the HCC server except LAN and loopback (EGRESS-CONTRACT 4). Toggled by \HCC\ToggleEgress.' | Out-Null
        Write-HccLog "Created firewall rule '$RuleName' (enabled = internet blocked) for $ServiceExe"
    } else {
        Set-NetFirewallRule -DisplayName $RuleName -Program $ServiceExe -RemoteAddress $BlockedRemote -Action Block -Direction Outbound | Out-Null
        Write-HccLog "Firewall rule '$RuleName' already present; program and ranges refreshed"
    }

    if (-not (Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue)) {
        $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $action = New-ScheduledTaskAction -Execute $ps -Argument ('-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -State Apply')
        $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        $task = Register-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -Action $action -Principal $principal `
            -Description 'HCC: applies the internet switch to the firewall rule (on demand, started by the HCC server).'
        Disable-ScheduledTask -InputObject $task | Out-Null
        Write-HccLog "Registered scheduled task $TaskPath$TaskName (SYSTEM, on demand, disabled)"
    }
}

function Set-Rule([string]$To) {
    $rule = Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue
    if (-not $rule) { throw "Firewall rule '$RuleName' is missing. Run Set-HccEgress.ps1 -State Install first." }
    # On = internet allowed = block rule disabled; Off = blocked = enabled.
    $enabled = if ($To -eq 'On') { 'False' } else { 'True' }
    Set-NetFirewallRule -DisplayName $RuleName -Enabled $enabled | Out-Null
    Write-HccLog "Internet for HCC: $To (rule '$RuleName' enabled = $enabled)"
}

switch ($State) {
    'Install' { Install-Rule }
    'On' { Set-Rule 'On' }
    'Off' { Set-Rule 'Off' }
    'Apply' {
        $detail = $null
        try {
            $wanted = (Invoke-EgressSql "SELECT wanted_state FROM ops.egress_firewall_state WHERE id = 1;" -Scalar).Trim()
            if ($wanted -notin @('On', 'Off')) { throw "Unexpected wanted_state '$wanted'" }
            Set-Rule $wanted
            $detail = 'applied by \HCC\ToggleEgress'
            if ($PSCmdlet.ShouldProcess('ops.egress_firewall_state', "record applied_state = $wanted")) { Invoke-EgressSql ("UPDATE ops.egress_firewall_state SET applied_state = " + (ConvertTo-HccSqlLiteral $wanted) + ", applied_utc = SYSUTCDATETIME(), detail = " + (ConvertTo-HccSqlLiteral $detail) + " WHERE id = 1;") | Out-Null }
        } catch {
            $msg = $_.Exception.GetType().Name
            try { Invoke-EgressSql ("UPDATE ops.egress_firewall_state SET detail = " + (ConvertTo-HccSqlLiteral ("not applied: " + $msg)) + " WHERE id = 1;") | Out-Null } catch { }
            Write-HccLog "Egress firewall apply failed ($msg); the software gate still holds." -Level ERROR
            exit 1
        }
    }
    'Status' {
        $rule = Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue
        if ($rule) { Write-Host ("Rule '{0}': Enabled={1} Action={2} Direction={3}" -f $RuleName, $rule.Enabled, $rule.Action, $rule.Direction) } else { Write-Host "Rule '$RuleName': MISSING" }
        $task = Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue
        if ($task) { Write-Host ("Task {0}{1}: State={2}" -f $TaskPath, $TaskName, $task.State) } else { Write-Host "Task $TaskPath$TaskName : MISSING" }
        try { Write-Host (Invoke-EgressSql "SELECT wanted_state, applied_state, applied_utc, detail FROM ops.egress_firewall_state WHERE id = 1;") } catch { Write-Host "Database row: unreadable ($($_.Exception.GetType().Name))" }
    }
}
