<#
.SYNOPSIS
  LAN HTTPS for HCC: creates a private hotel CA and a server certificate for the PC's LAN name/IP (or imports your own PFX),
  and exports the CA certificate that must be installed on every tablet/phone/PC that uses HCC.
.DESCRIPTION
  Run from an elevated PowerShell on the hotel PC. Creates in LocalMachine\My:
    - "HCC Hotel CA" (CA certificate, 10 years)  -> exported as <OutputDirectory>\HCC-Hotel-CA.cer (public part only; safe to share)
    - server certificate (2 years, DNS + IP SANs, Server Authentication) signed by that CA
  The CA private key is exported to <OutputDirectory>\HCC-Hotel-CA.pfx (password you type) only with -ExportCaPfx, and then removed
  from this PC's store, so the CA can re-issue the server certificate in two years. Keep that file and password in the safe.
  The service account gets read access to the server certificate's private key. The thumbprint is printed and, with -UpdateConfig
  <path to appsettings.Production.json>, written to Hcc:Hosting:CertificateThumbprint.
  Use -ImportPfx <file> (password prompt) instead to use a certificate from a real CA; the CA steps are skipped.
  -WhatIf shows the plan (names, IPs, files) and changes nothing.
  Android trust steps are in INSTALL.md (section "Phones and tablets") and ANDROID_SERVER_ADDRESS.md.
# ETP-REUSE: replace/align with ETP's certificate/TLS script (if any) when V:\ETP is available.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ServerName = $env:COMPUTERNAME,
    [string[]]$DnsName = @(),
    [string[]]$IpAddress = @(),
    [string]$OutputDirectory,
    [string]$ServiceAccount = 'NT SERVICE\HccServer',
    [string]$ImportPfx,
    [switch]$ExportCaPfx,
    [string]$UpdateConfig,
    [int]$ServerYears = 2
)
$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'certs' }

# Pure planning function (unit-tested): names and IPs that go into the certificate.
function Get-HccCertificatePlan {
    param([string]$ServerName, [string[]]$DnsName, [string[]]$IpAddress)
    $dns = @(@($ServerName) + @($DnsName) | Where-Object { $_ } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Select-Object -Unique)
    $dns = @($dns + 'localhost' | Select-Object -Unique)
    $ips = @()
    foreach ($i in @($IpAddress | Where-Object { $_ })) {
        $parsed = $null
        if (-not [Net.IPAddress]::TryParse($i.Trim(), [ref]$parsed)) { throw "'$i' is not a valid IP address." }
        $ips += $parsed.ToString()
    }
    $ips = @($ips + '127.0.0.1' | Select-Object -Unique)
    foreach ($n in $dns) { if ($n -notmatch '^[a-z0-9]([a-z0-9.-]{0,251}[a-z0-9])?$') { throw "'$n' is not a valid DNS/host name." } }
    return [pscustomobject]@{ DnsNames = $dns; IpAddresses = $ips; SubjectName = $dns[0] }
}

if ($MyInvocation.InvocationName -eq '.') { return }   # dot-sourced by the tests: functions only

$plan = Get-HccCertificatePlan -ServerName $ServerName -DnsName $DnsName -IpAddress $IpAddress
if ($plan.IpAddresses.Count -le 1 -and -not $ImportPfx) { Write-Warning 'No -IpAddress given: tablets that open https://<ip>:5443 will not match. Pass the PC LAN address (give it a fixed address or a router reservation).' }
$cer = Join-Path $OutputDirectory 'HCC-Hotel-CA.cer'
$caPfx = Join-Path $OutputDirectory 'HCC-Hotel-CA.pfx'

if (-not $PSCmdlet.ShouldProcess("LocalMachine\My on $env:COMPUTERNAME", $(if ($ImportPfx) { "Import $ImportPfx" } else { "Create HCC Hotel CA + server certificate for DNS [$($plan.DnsNames -join ', ')] IP [$($plan.IpAddresses -join ', ')]" }))) {
    Write-Host "DRY RUN plan: DNS names = $($plan.DnsNames -join ', ')"
    Write-Host "DRY RUN plan: IP addresses = $($plan.IpAddresses -join ', ')"
    if ($ImportPfx) { Write-Host "DRY RUN plan: import $ImportPfx into LocalMachine\My; grant read on its key to $ServiceAccount" }
    else {
        Write-Host "DRY RUN plan: create CA 'HCC Hotel CA' (10 y) and server certificate (CN=$($plan.SubjectName), $ServerYears y) in LocalMachine\My"
        Write-Host "DRY RUN plan: export CA certificate (public) to $cer$(if ($ExportCaPfx) { "; export CA key to $caPfx and remove it from this PC" })"
    }
    Write-Host "DRY RUN plan: grant $ServiceAccount read on the server key; print the thumbprint$(if ($UpdateConfig) { "; write it to $UpdateConfig" })"
    return
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run this script from an elevated (Administrator) PowerShell.' }
if (-not (Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null }

if ($ImportPfx) {
    $pw = Read-Host 'PFX password' -AsSecureString
    $server = Import-PfxCertificate -FilePath $ImportPfx -CertStoreLocation 'Cert:\LocalMachine\My' -Password $pw
}
else {
    $ca = New-SelfSignedCertificate -Type Custom -Subject 'CN=HCC Hotel CA' -KeyUsage CertSign, CRLSign, DigitalSignature -KeyAlgorithm RSA -KeyLength 3072 `
        -HashAlgorithm SHA256 -KeyExportPolicy Exportable -CertStoreLocation 'Cert:\LocalMachine\My' -NotAfter (Get-Date).AddYears(10) `
        -TextExtension @('2.5.29.19={critical}{text}ca=true&pathlength=0')
    $san = @($plan.DnsNames | ForEach-Object { "DNS=$_" }) + @($plan.IpAddresses | ForEach-Object { "IPAddress=$_" })
    $server = New-SelfSignedCertificate -Type Custom -Subject ("CN=" + $plan.SubjectName) -Signer $ca -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature, KeyEncipherment -KeyExportPolicy NonExportable -CertStoreLocation 'Cert:\LocalMachine\My' -NotAfter (Get-Date).AddYears($ServerYears) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.1', ('2.5.29.17={text}' + ($san -join '&')))
    Export-Certificate -Cert $ca -FilePath $cer -Type CERT | Out-Null
    Write-Host "CA certificate (public) exported: $cer"
    if ($ExportCaPfx) {
        $pw = Read-Host 'Password to protect the CA backup file' -AsSecureString
        Export-PfxCertificate -Cert $ca -FilePath $caPfx -Password $pw | Out-Null
        Write-Host "CA private key exported: $caPfx  (keep it and the password in the safe)"
        Remove-Item -LiteralPath ("Cert:\LocalMachine\My\" + $ca.Thumbprint) -DeleteKey -Force   # public CA stays in the exported .cer
    }
}

# Let the service account read the private key.
try {
    $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($server)
    $keyName = $rsa.Key.UniqueName
    $keyFile = Get-ChildItem (Join-Path $env:ProgramData 'Microsoft\Crypto\RSA\MachineKeys') -Filter $keyName -ErrorAction Stop | Select-Object -First 1
    $acl = Get-Acl $keyFile.FullName
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($ServiceAccount, 'Read', 'Allow')
    $acl.AddAccessRule($rule); Set-Acl -Path $keyFile.FullName -AclObject $acl
    Write-Host "Granted $ServiceAccount read access to the certificate key."
}
catch { Write-Warning "Could not set key permission for $ServiceAccount ($($_.Exception.Message)). Create the service first (Install-HccService.ps1), then re-run with -ImportPfx or grant it in certlm.msc > Manage Private Keys." }

Write-Host ''
Write-Host "Server certificate thumbprint: $($server.Thumbprint)"
if ($UpdateConfig) {
    $json = Get-Content -LiteralPath $UpdateConfig -Raw | ConvertFrom-Json
    if (-not $json.Hcc.PSObject.Properties['Hosting']) { $json.Hcc | Add-Member -NotePropertyName Hosting -NotePropertyValue ([pscustomobject]@{}) }
    $json.Hcc.Hosting | Add-Member -NotePropertyName CertificateThumbprint -NotePropertyValue $server.Thumbprint -Force
    $json | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $UpdateConfig -Encoding UTF8
    Write-Host "Thumbprint written to $UpdateConfig"
}
Write-Host "Next: copy $cer to each tablet/phone and install it as a CA certificate (INSTALL.md)."
