<#
.SYNOPSIS
  LAN HTTPS for HCC: creates a private, NAME-CONSTRAINED hotel CA and a short-lived server certificate for the PC's LAN name/IP (or
  imports your own PFX), exports the CA certificate that must be installed on every tablet/phone/PC that uses HCC, and renews the
  server certificate.
.DESCRIPTION
  Run from an elevated PowerShell on the hotel PC.
  Create (default):
    - "HCC Hotel CA" (CA certificate, 10 years, path length 0) with a CRITICAL Name Constraints extension (F-M04): it can only vouch
      for the hotel server's own names and addresses (the -ServerName/-DnsName names, -IpAddress addresses, localhost, 127.0.0.1 and
      any -PermittedNetwork). A certificate it signed for any other site (a bank, Gmail) is rejected by Windows and Chrome, so
      installing it on staff devices does not let it impersonate other websites there (install it only on hotel devices).
    - server certificate (-ServerDays, default 397 days; DNS + IP SANs, Server Authentication; non-exportable key) signed by that CA.
    - CA certificate (public part, safe to share) -> <OutputDirectory>\HCC-Hotel-CA.cer
    - CA private key -> -CaPfxPath (default <OutputDirectory>\HCC-Hotel-CA.pfx), protected by a password you type, and then REMOVED
      from this PC (always; the script fails and removes both certificates if the export fails). Keep that file and the password in
      the safe, ideally straight on a USB stick. The CA key is not on the server, so a compromise of the PC cannot mint new certificates.
  Renew (-RenewServer -CaPfx <file>), once a year before the server certificate expires (INSTALL.md section 4b):
    - imports the CA from the PFX into LocalMachine\My as NON-EXPORTABLE, issues a new server certificate with the same names, removes
      the CA (and its key) from the store again, grants the service read on the new key and writes the new thumbprint (-UpdateConfig).
      Devices keep working: they trust the CA, not the server certificate.
  The service account gets read access to the server certificate's private key. The thumbprint is printed and, with -UpdateConfig
  <path to appsettings.Production.json>, written to Hcc:Hosting:CertificateThumbprint together with AllowedHosts (the same names and
  addresses, F-L02).
  Use -ImportPfx <file> (password prompt) instead to use a certificate from a real CA; the CA steps are skipped.
  -WhatIf shows the plan (names, IPs, constraints, files) and changes nothing.
  Android trust steps are in INSTALL.md (section "Phones and tablets") and ANDROID_SERVER_ADDRESS.md.
# ETP-REUSE: replace/align with ETP's certificate/TLS script (if any) when V:\ETP is available.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ServerName = $env:COMPUTERNAME,
    [string[]]$DnsName = @(),
    [string[]]$IpAddress = @(),
    [string[]]$PermittedNetwork = @(),     # optional extra CIDR ranges the CA may sign for (for example 192.168.1.0/24 if the PC's address may change)
    [string]$OutputDirectory,
    [string]$ServiceAccount = 'NT SERVICE\HccServer',
    [string]$ImportPfx,
    [string]$CaPfxPath,
    [switch]$RenewServer,
    [string]$CaPfx,
    [switch]$ExportCaPfx,                  # kept for older instructions: the CA key is now always exported and removed
    [string]$UpdateConfig,
    [ValidateRange(30, 825)][int]$ServerDays = 397
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

# --- Name Constraints (RFC 5280 4.2.1.10), DER-encoded by hand (pure, unit-tested; no extra software) ------------------------------
function ConvertTo-HccDer {
    param([int]$Tag, [byte[]]$Content)
    $len = $Content.Length
    if ($len -lt 128) { $head = [byte[]]@($Tag, $len) }
    elseif ($len -lt 256) { $head = [byte[]]@($Tag, 0x81, $len) }
    else { $head = [byte[]]@($Tag, 0x82, [byte]($len -shr 8), [byte]($len -band 0xFF)) }
    return , [byte[]]($head + $Content)
}

# "a.b.c.d/nn" or "a.b.c.d" (=/32), IPv6 likewise (/128): address bytes followed by mask bytes, as iPAddress constraints need.
function ConvertTo-HccIpConstraintBytes {
    param([Parameter(Mandatory)][string]$Cidr)
    $parts = $Cidr.Trim() -split '/', 2
    $ip = $null
    if (-not [Net.IPAddress]::TryParse($parts[0], [ref]$ip)) { throw "'$Cidr' is not an IP address or network." }
    $addr = $ip.GetAddressBytes()
    $bits = if ($parts.Count -gt 1) { [int]$parts[1] } else { $addr.Length * 8 }
    if ($bits -lt 0 -or $bits -gt $addr.Length * 8) { throw "'$Cidr' has an invalid prefix length." }
    $mask = New-Object byte[] $addr.Length
    for ($i = 0; $i -lt $addr.Length; $i++) {
        $b = [Math]::Max(0, [Math]::Min(8, $bits - 8 * $i))
        $mask[$i] = [byte]((0xFF -shl (8 - $b)) -band 0xFF)
        $addr[$i] = [byte]($addr[$i] -band $mask[$i])
    }
    return , [byte[]]($addr + $mask)
}

function Get-HccNameConstraintsDer {
    param([string[]]$DnsNames = @(), [string[]]$IpNetworks = @())
    $permitted = New-Object System.Collections.Generic.List[byte]
    foreach ($d in $DnsNames) { $permitted.AddRange([byte[]](ConvertTo-HccDer 0x30 (ConvertTo-HccDer 0x82 ([Text.Encoding]::ASCII.GetBytes($d))))) }
    $hasV4 = $false; $hasV6 = $false
    foreach ($n in $IpNetworks) {
        $b = ConvertTo-HccIpConstraintBytes $n
        if ($b.Length -eq 8) { $hasV4 = $true } else { $hasV6 = $true }
        $permitted.AddRange([byte[]](ConvertTo-HccDer 0x30 (ConvertTo-HccDer 0x87 $b)))
    }
    # Exclude the whole other address family, so an address of a family with no permitted range is never accepted.
    $excluded = New-Object System.Collections.Generic.List[byte]
    if (-not $hasV4) { $excluded.AddRange([byte[]](ConvertTo-HccDer 0x30 (ConvertTo-HccDer 0x87 (New-Object byte[] 8)))) }
    if (-not $hasV6) { $excluded.AddRange([byte[]](ConvertTo-HccDer 0x30 (ConvertTo-HccDer 0x87 (New-Object byte[] 32)))) }
    $body = [byte[]](ConvertTo-HccDer 0xA0 $permitted.ToArray())
    if ($excluded.Count -gt 0) { $body = [byte[]]($body + (ConvertTo-HccDer 0xA1 $excluded.ToArray())) }
    return , [byte[]](ConvertTo-HccDer 0x30 $body)
}

function New-HccNameConstraintsExtension {
    param([string[]]$DnsNames, [string[]]$IpNetworks)
    $der = Get-HccNameConstraintsDer -DnsNames $DnsNames -IpNetworks $IpNetworks
    return New-Object Security.Cryptography.X509Certificates.X509Extension('2.5.29.30', $der, $true)
}

if ($MyInvocation.InvocationName -eq '.') { return }   # dot-sourced by the tests: functions only

$plan = Get-HccCertificatePlan -ServerName $ServerName -DnsName $DnsName -IpAddress $IpAddress
$networks = @($plan.IpAddresses | ForEach-Object { $_ }) + @($PermittedNetwork | Where-Object { $_ })
foreach ($n in $networks) { ConvertTo-HccIpConstraintBytes $n | Out-Null }   # validate before doing anything
if ($plan.IpAddresses.Count -le 1 -and -not $ImportPfx) { Write-Warning 'No -IpAddress given: tablets that open https://<ip>:5443 will not match. Pass the PC LAN address (give it a fixed address or a router reservation).' }
if ($RenewServer -and -not $CaPfx) { throw '-RenewServer needs -CaPfx <the HCC-Hotel-CA.pfx from the safe>.' }
$cer = Join-Path $OutputDirectory 'HCC-Hotel-CA.cer'
if (-not $CaPfxPath) { $CaPfxPath = Join-Path $OutputDirectory 'HCC-Hotel-CA.pfx' }
$mode = if ($ImportPfx) { 'import' } elseif ($RenewServer) { 'renew' } else { 'create' }

$summary = switch ($mode) {
    'import' { "Import $ImportPfx" }
    'renew' { "Renew the server certificate for DNS [$($plan.DnsNames -join ', ')] IP [$($plan.IpAddresses -join ', ')] with the CA from $CaPfx" }
    default { "Create name-constrained HCC Hotel CA + server certificate for DNS [$($plan.DnsNames -join ', ')] IP [$($plan.IpAddresses -join ', ')]" }
}
if (-not $PSCmdlet.ShouldProcess("LocalMachine\My on $env:COMPUTERNAME", $summary)) {
    Write-Host "DRY RUN plan: DNS names = $($plan.DnsNames -join ', ')"
    Write-Host "DRY RUN plan: IP addresses = $($plan.IpAddresses -join ', ')"
    if ($mode -eq 'import') { Write-Host "DRY RUN plan: import $ImportPfx into LocalMachine\My; grant read on its key to $ServiceAccount" }
    else {
        if ($mode -eq 'create') {
            Write-Host "DRY RUN plan: create CA 'HCC Hotel CA' (10 y, path length 0) with critical Name Constraints: permitted DNS [$($plan.DnsNames -join ', ')], permitted IP [$($networks -join ', ')]"
            Write-Host "DRY RUN plan: export CA certificate (public) to $cer; export CA key (password) to $CaPfxPath, then remove the CA key from this PC"
        }
        else { Write-Host "DRY RUN plan: import the CA from $CaPfx as non-exportable, then remove it (and its key) from this PC again after signing" }
        Write-Host "DRY RUN plan: server certificate CN=$($plan.SubjectName), valid $ServerDays days, non-exportable key, in LocalMachine\My (renew before it expires: -RenewServer -CaPfx)"
    }
    Write-Host "DRY RUN plan: grant $ServiceAccount read on the server key; print the thumbprint$(if ($UpdateConfig) { "; write it and AllowedHosts=$(($plan.DnsNames + $plan.IpAddresses) -join ';') to $UpdateConfig" })"
    return
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run this script from an elevated (Administrator) PowerShell.' }
if (-not (Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null }

function Remove-HccStoreCertificate($c) { if ($c) { Remove-Item -LiteralPath ("Cert:\LocalMachine\My\" + $c.Thumbprint) -DeleteKey -Force -ErrorAction SilentlyContinue } }
function New-HccServerCertificate($signer) {
    $san = @($plan.DnsNames | ForEach-Object { "DNS=$_" }) + @($plan.IpAddresses | ForEach-Object { "IPAddress=$_" })
    return New-SelfSignedCertificate -Type Custom -Subject ("CN=" + $plan.SubjectName) -Signer $signer -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature, KeyEncipherment -KeyExportPolicy NonExportable -CertStoreLocation 'Cert:\LocalMachine\My' -NotAfter (Get-Date).AddDays($ServerDays) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.1', ('2.5.29.17={text}' + ($san -join '&')))
}

if ($mode -eq 'import') {
    $pw = Read-Host 'PFX password' -AsSecureString
    $server = Import-PfxCertificate -FilePath $ImportPfx -CertStoreLocation 'Cert:\LocalMachine\My' -Password $pw
}
elseif ($mode -eq 'renew') {
    $pw = Read-Host 'Password of the CA backup file' -AsSecureString
    $ca = Import-PfxCertificate -FilePath $CaPfx -CertStoreLocation 'Cert:\LocalMachine\My' -Password $pw   # no -Exportable: the key cannot be copied out
    try {
        if (-not ($ca.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.30' })) { Write-Warning 'This CA has no Name Constraints (made before F-M04). Plan a new CA: run this script without -RenewServer and re-install HCC-Hotel-CA.cer on the devices.' }
        $server = New-HccServerCertificate $ca
    }
    finally { Remove-HccStoreCertificate $ca }
    Write-Host 'Server certificate renewed; the CA key was removed from this PC again.'
}
else {
    $nc = New-HccNameConstraintsExtension -DnsNames $plan.DnsNames -IpNetworks $networks
    $ca = New-SelfSignedCertificate -Type Custom -Subject 'CN=HCC Hotel CA' -KeyUsage CertSign, CRLSign, DigitalSignature -KeyAlgorithm RSA -KeyLength 3072 `
        -HashAlgorithm SHA256 -KeyExportPolicy Exportable -CertStoreLocation 'Cert:\LocalMachine\My' -NotAfter (Get-Date).AddYears(10) `
        -TextExtension @('2.5.29.19={critical}{text}ca=true&pathlength=0') -Extension @($nc)
    $server = $null
    try {
        $server = New-HccServerCertificate $ca
        Export-Certificate -Cert $ca -FilePath $cer -Type CERT | Out-Null
        Write-Host "CA certificate (public) exported: $cer"
        $pw = Read-Host 'Password to protect the CA backup file (keep it in the safe)' -AsSecureString
        Export-PfxCertificate -Cert $ca -FilePath $CaPfxPath -Password $pw | Out-Null
        if (-not (Test-Path -LiteralPath $CaPfxPath)) { throw "The CA key was not written to $CaPfxPath." }
        Write-Host "CA private key exported: $CaPfxPath  (move it to a USB stick in the safe with its password; delete it from this PC)"
    }
    catch {
        Remove-HccStoreCertificate $server
        Remove-HccStoreCertificate $ca
        throw "Certificate setup stopped and was undone (nothing is left in the store): $($_.Exception.Message)"
    }
    Remove-HccStoreCertificate $ca   # the public CA stays in the exported .cer; the key only in the PFX
}

# Let the service account read the server certificate's private key (CNG or legacy CSP key folder).
try {
    $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($server)
    $keyName = if ($rsa -is [Security.Cryptography.RSACng]) { $rsa.Key.UniqueName } else { $rsa.CspKeyContainerInfo.UniqueKeyContainerName }
    $keyFile = @('Microsoft\Crypto\Keys', 'Microsoft\Crypto\RSA\MachineKeys') | ForEach-Object { Join-Path $env:ProgramData $_ } |
        ForEach-Object { Get-ChildItem -LiteralPath $_ -Filter $keyName -ErrorAction SilentlyContinue } | Select-Object -First 1
    if (-not $keyFile) { throw "key file $keyName not found" }
    $acl = Get-Acl $keyFile.FullName
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($ServiceAccount, 'Read', 'Allow')
    $acl.AddAccessRule($rule); Set-Acl -Path $keyFile.FullName -AclObject $acl
    Write-Host "Granted $ServiceAccount read access to the certificate key."
}
catch { Write-Warning "Could not set key permission for $ServiceAccount ($($_.Exception.Message)). Create the service first (Install-HccService.ps1), then grant it in certlm.msc > Manage Private Keys." }

Write-Host ''
Write-Host "Server certificate thumbprint: $($server.Thumbprint) (valid until $($server.NotAfter.ToString('yyyy-MM-dd')); renew before then: INSTALL.md section 4b)"
if ($UpdateConfig) {
    $json = Get-Content -LiteralPath $UpdateConfig -Raw | ConvertFrom-Json
    if (-not $json.Hcc.PSObject.Properties['Hosting']) { $json.Hcc | Add-Member -NotePropertyName Hosting -NotePropertyValue ([pscustomobject]@{}) }
    $json.Hcc.Hosting | Add-Member -NotePropertyName CertificateThumbprint -NotePropertyValue $server.Thumbprint -Force
    $json | Add-Member -NotePropertyName AllowedHosts -NotePropertyValue (($plan.DnsNames + $plan.IpAddresses) -join ';') -Force
    [IO.File]::WriteAllText($UpdateConfig, ($json | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
    Write-Host "Thumbprint and AllowedHosts written to $UpdateConfig (restart HccServer to use the new certificate)."
}
if ($mode -eq 'create') { Write-Host "Next: copy $cer to each tablet/phone and install it as a CA certificate (INSTALL.md)." }
