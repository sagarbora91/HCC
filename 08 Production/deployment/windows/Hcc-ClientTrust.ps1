<#
.SYNOPSIS
  Shared by the HCC client setup (installer\HccClient.iss) and New-HccClientKit.ps1: checks the hotel CA certificate and the server
  address, trusts that ONE certificate on a client PC, removes it again, and checks that the server answers over HTTPS.
.DESCRIPTION
  Dot-source it for the functions (the tests and the kit script do), or run it with -Action (the client setup does):
    -Action Inspect -CerPath <file> (-ConfigPath <hcc-client.json> | -ServerUrl <url>) -OutFile <file>   checks only, changes nothing
    -Action Trust   -CerPath <file> -ExpectedSha256 <fingerprint the person approved> -OutFile <file>    adds the certificate to
                    Local Machine > Trusted Root Certification Authorities (needs an administrator; -WhatIf changes nothing)
    -Action Untrust -Sha256 <fingerprint> -OutFile <file>                                               removes that certificate again
    -Action Check   -ServerUrl <url> -OutFile <file>                                                    does the server answer? (never fails)
  -OutFile receives key=value lines (UTF-16, so hotel names with any letters survive); exit code 0 = ok, 1 = refused / failed.
  Nothing secret is handled here: the CA certificate is the public part only.
  Windows PowerShell 5.1 and PowerShell 7.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('', 'Inspect', 'Trust', 'Untrust', 'Check')][string]$Action = '',
    [string]$CerPath,
    [string]$ConfigPath,
    [string]$ServerUrl,
    [string]$ExpectedSha256,
    [string]$Sha256,
    [string]$OutFile
)
$ErrorActionPreference = 'Stop'

function ConvertTo-HccFingerprint {
    # "AB:cd ..." / "ab cd.." -> 64 upper-case hex characters, or '' when it is not a SHA-256 fingerprint.
    param([string]$Text)
    if (-not $Text) { return '' }
    $h = ($Text -replace '[\s:\-]', '').ToUpperInvariant()
    if ($h -match '^[0-9A-F]{64}$') { return $h }
    return ''
}
function Format-HccFingerprint { param([string]$Hex) return (($Hex -split '(.{2})' | Where-Object { $_ }) -join ':') }

function Get-HccCertificateSha256 {
    param([Security.Cryptography.X509Certificates.X509Certificate2]$Cert)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return (($sha.ComputeHash($Cert.RawData) | ForEach-Object { $_.ToString('X2') }) -join '') } finally { $sha.Dispose() }
}

# Only https://<host>[:port]/ made of letters, digits, '.' and '-' (so it can be put into a shortcut / script / command line safely).
function Test-HccServerUrl {
    param([string]$Url)
    $r = [pscustomobject]@{ Ok = $false; Error = ''; Url = ''; Host = ''; Port = 443 }
    if (-not $Url -or -not $Url.Trim()) { $r.Error = 'The server address is empty.'; return $r }
    $u = $Url.Trim()
    if ($u -notmatch '^https://') { $r.Error = "The server address must start with https:// (it is '$u'). HCC never runs without the secure connection."; return $r }
    if ($u -notmatch '^https://([A-Za-z0-9]([A-Za-z0-9.-]{0,251}[A-Za-z0-9])?)(:([0-9]{1,5}))?/?$') {
        $r.Error = "The server address '$u' is not of the form https://name:port/ (letters, digits, '.' and '-' only, no user name, path or extra parts)."; return $r
    }
    $hostName = $Matches[1]
    $port = 443
    if ($Matches[4]) { $port = [int]$Matches[4] }
    if ($port -lt 1 -or $port -gt 65535) { $r.Error = "The port $port is not valid."; return $r }
    $r.Host = $hostName; $r.Port = $port
    if ($port -eq 443) { $r.Url = "https://$hostName/" } else { $r.Url = "https://${hostName}:$port/" }
    $r.Ok = $true
    return $r
}

# Reads ONE certificate (DER or PEM) and decides whether it may be trusted as the hotel CA.
function Get-HccCaFileInfo {
    param([string]$Path)
    $r = [pscustomobject]@{ Ok = $false; Error = ''; Subject = ''; Sha256 = ''; NotAfter = ''; NameConstrained = $false; Cert = $null }
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { $r.Error = "The certificate file '$Path' was not found."; return $r }
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -eq 0 -or $bytes.Length -gt 65536) { $r.Error = 'This is not a certificate file (empty or too large).'; return $r }
    $text = [Text.Encoding]::ASCII.GetString($bytes)
    if ($text -match '-----BEGIN') {
        $blocks = [regex]::Matches($text, '-----BEGIN CERTIFICATE-----([A-Za-z0-9+/=\s]+)-----END CERTIFICATE-----')
        if ($blocks.Count -ne 1) { $r.Error = "The file must hold exactly one certificate (it holds $($blocks.Count))."; return $r }
        $bytes = [Convert]::FromBase64String(($blocks[0].Groups[1].Value -replace '\s', ''))
    }
    try { $cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList (, [byte[]]$bytes) }
    catch { $r.Error = 'This file is not a certificate (.cer) file.'; return $r }
    $r.Subject = $cert.Subject; $r.Cert = $cert
    $r.Sha256 = Get-HccCertificateSha256 $cert
    $r.NotAfter = $cert.NotAfter.ToString('yyyy-MM-dd')
    $bc = @($cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' })
    if ($bc.Count -eq 0 -or -not $bc[0].CertificateAuthority) { $r.Error = 'This is not a CA certificate (it is a server or user certificate). Use HCC-Hotel-CA.cer from the HCC client kit.'; return $r }
    if ($cert.Subject -ne $cert.Issuer) { $r.Error = 'This certificate is not a self-signed root. Use HCC-Hotel-CA.cer from the HCC client kit.'; return $r }
    if ($cert.NotAfter -lt (Get-Date)) { $r.Error = "This certificate expired on $($r.NotAfter)."; return $r }
    if ($cert.NotBefore -gt (Get-Date).AddDays(1)) { $r.Error = 'This certificate is not valid yet: check the date and time of this PC.'; return $r }
    $r.NameConstrained = [bool](@($cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.30' }).Count -gt 0)
    if (-not $r.NameConstrained) { $r.Error = 'This CA has no name restriction, so it could vouch for any website. HCC only installs a CA limited to the hotel server. Make a new certificate on the server (New-HccCertificate.ps1) and a new client kit.'; return $r }
    $r.Ok = $true
    return $r
}

function Read-HccClientConfig {
    param([string]$Path)
    $r = [pscustomobject]@{ Ok = $false; Error = ''; HotelName = ''; ServerUrl = ''; CaSha256 = '' }
    try { $j = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8) | ConvertFrom-Json } catch { $r.Error = "hcc-client.json cannot be read: $($_.Exception.Message)"; return $r }
    foreach ($n in 'serverUrl', 'hotelName', 'caSha256') { if (-not $j.PSObject.Properties[$n]) { $r.Error = "hcc-client.json has no '$n'."; return $r } }
    $r.HotelName = [string]$j.hotelName; $r.ServerUrl = [string]$j.serverUrl; $r.CaSha256 = [string]$j.caSha256
    $r.Ok = $true
    return $r
}

# Everything the setup checks before it asks the person to confirm: certificate, address, fingerprint.
function Test-HccClientInputs {
    param([string]$CerPath, [string]$ConfigPath, [string]$ServerUrl)
    $r = [pscustomobject]@{ Ok = $false; Error = ''; HotelName = ''; ServerUrl = ''; Subject = ''; Sha256 = ''; Sha256Display = ''; NotAfter = ''; ExpectedKnown = $false; FingerprintMatches = $false }
    $expected = ''
    $url = $ServerUrl
    if ($ConfigPath) {
        $c = Read-HccClientConfig $ConfigPath
        if (-not $c.Ok) { $r.Error = $c.Error; return $r }
        $r.HotelName = $c.HotelName; $url = $c.ServerUrl
        if ($c.CaSha256) {
            $expected = ConvertTo-HccFingerprint $c.CaSha256
            if (-not $expected) { $r.Error = 'hcc-client.json has a caSha256 that is not a SHA-256 fingerprint.'; return $r }
        }
    }
    $u = Test-HccServerUrl $url
    if (-not $u.Ok) { $r.Error = $u.Error; return $r }
    $r.ServerUrl = $u.Url
    $ca = Get-HccCaFileInfo $CerPath
    $r.Subject = $ca.Subject; $r.Sha256 = $ca.Sha256; $r.NotAfter = $ca.NotAfter
    if ($ca.Sha256) { $r.Sha256Display = Format-HccFingerprint $ca.Sha256 }
    if (-not $ca.Ok) { $r.Error = $ca.Error; return $r }
    if ($expected) {
        $r.ExpectedKnown = $true
        if ($expected -ne $ca.Sha256) { $r.Error = "The certificate file does not match the kit: its fingerprint is $($r.Sha256Display) but hcc-client.json expects $(Format-HccFingerprint $expected). The files are from different kits or one was changed. Copy the kit folder from the server again."; return $r }
        $r.FingerprintMatches = $true
    }
    $r.Ok = $true
    return $r
}

# Adds the one certificate to LocalMachine\Root. Re-checks everything itself and compares with the fingerprint the person approved.
function Add-HccTrustedRoot {
    [CmdletBinding(SupportsShouldProcess)]
    param([string]$CerPath, [string]$ApprovedSha256)
    $ca = Get-HccCaFileInfo $CerPath
    if (-not $ca.Ok) { throw $ca.Error }
    $approved = ConvertTo-HccFingerprint $ApprovedSha256
    if (-not $approved -or $approved -ne $ca.Sha256) { throw 'The certificate is not the one that was confirmed (fingerprint differs). Nothing was installed.' }
    if (-not $PSCmdlet.ShouldProcess('LocalMachine\Root', "Trust the hotel CA $($ca.Sha256)")) { return 'whatif' }
    $store = New-Object Security.Cryptography.X509Certificates.X509Store('Root', [Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    try {
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $have = @($store.Certificates | Where-Object { $_.Thumbprint -eq $ca.Cert.Thumbprint })
        if ($have.Count -gt 0) { return 'already' }
        $store.Add($ca.Cert)
        return 'added'
    }
    finally { $store.Close() }
}

function Remove-HccTrustedRoot {
    [CmdletBinding(SupportsShouldProcess)]
    param([string]$Sha256)
    $want = ConvertTo-HccFingerprint $Sha256
    if (-not $want) { throw 'Not a SHA-256 fingerprint.' }
    $store = New-Object Security.Cryptography.X509Certificates.X509Store('Root', [Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    try {
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $hit = @($store.Certificates | Where-Object { (Get-HccCertificateSha256 $_) -eq $want })
        if ($hit.Count -eq 0) { return 'absent' }
        if (-not $PSCmdlet.ShouldProcess('LocalMachine\Root', "Remove the hotel CA $want")) { return 'whatif' }
        foreach ($c in $hit) { $store.Remove($c) }
        return 'removed'
    }
    finally { $store.Close() }
}

# Pure: what kind of failure was it? ok = the server answered (even with an HTTP error page).
function Get-HccReachabilityKind {
    param($Exception)
    $e = $Exception
    $texts = @()
    while ($e) {
        if (($e -is [Net.WebException]) -and $e.Response) { return 'ok' }
        if (($e -is [Net.WebException]) -and ($e.Status -eq [Net.WebExceptionStatus]::TrustFailure -or $e.Status -eq [Net.WebExceptionStatus]::SecureChannelFailure)) { return 'certificate' }
        if ($e -is [Security.Authentication.AuthenticationException]) { return 'certificate' }
        $texts += [string]$e.Message
        $e = $e.InnerException
    }
    $all = $texts -join ' | '
    if ($all -match 'certificate|trust|SSL|TLS|secure channel') { return 'certificate' }
    if ($all -match 'connect|resolve|remote name|timed out|timeout|actively refused|unreachable|No such host|name or service') { return 'unreachable' }
    return 'other'
}

function Test-HccServerReachable {
    param([string]$Url, [int]$TimeoutSec = 8)
    $kind = 'other'; $detail = ''
    try {
        try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }
        $req = [Net.HttpWebRequest]::Create($Url)
        $req.Method = 'GET'; $req.Timeout = $TimeoutSec * 1000; $req.AllowAutoRedirect = $false
        $resp = $req.GetResponse(); $resp.Close(); $kind = 'ok'
    }
    catch { $kind = Get-HccReachabilityKind $_.Exception; $detail = $_.Exception.Message }
    $msg = switch ($kind) {
        'ok' { 'The HCC server answered over the secure connection.' }
        'certificate' { 'The server answered, but this PC does not accept its certificate. Check that the date and time of this PC are right and that the kit is the one from this hotel server.' }
        'unreachable' { 'This PC cannot reach the HCC server right now. That is normal if the server PC is switched off, this PC is not on the hotel network or Wi-Fi, or a firewall blocks the connection. The setup is complete; the HCC icon will work once the server can be reached.' }
        default { 'The server did not give a clear answer. The setup is complete; try the HCC icon when the server is running.' }
    }
    return [pscustomobject]@{ Kind = $kind; Message = $msg; Detail = $detail }
}

function Write-HccResult {
    param([string]$Path, [System.Collections.IDictionary]$Values)
    if (-not $Path) { return }
    $lines = foreach ($k in $Values.Keys) { "$k=" + ([string]$Values[$k] -replace '[\r\n]+', ' ') }
    [IO.File]::WriteAllText($Path, (($lines -join "`r`n") + "`r`n"), [Text.Encoding]::Unicode)
}

if (-not $Action) { return }   # dot-sourced: functions only (strict mode is for the command-line use below)
Set-StrictMode -Version Latest

$out = [ordered]@{}
$exit = 0
try {
    switch ($Action) {
        'Inspect' {
            $i = Test-HccClientInputs -CerPath $CerPath -ConfigPath $ConfigPath -ServerUrl $ServerUrl
            $out.ok = [int]$i.Ok; $out.error = $i.Error; $out.hotel = $i.HotelName; $out.url = $i.ServerUrl; $out.subject = $i.Subject
            $out.sha256 = $i.Sha256; $out.sha256display = $i.Sha256Display; $out.notAfter = $i.NotAfter
            $out.expectedKnown = [int]$i.ExpectedKnown; $out.matches = [int]$i.FingerprintMatches
            if (-not $i.Ok) { $exit = 1 }
        }
        'Trust' {
            $res = Add-HccTrustedRoot -CerPath $CerPath -ApprovedSha256 $ExpectedSha256 -WhatIf:$WhatIfPreference
            $out.ok = 1; $out.result = $res; $out.error = ''
        }
        'Untrust' {
            $res = Remove-HccTrustedRoot -Sha256 $Sha256 -WhatIf:$WhatIfPreference
            $out.ok = 1; $out.result = $res; $out.error = ''
        }
        'Check' {
            $u = Test-HccServerUrl $ServerUrl
            if (-not $u.Ok) { $out.kind = 'other'; $out.message = $u.Error; $out.detail = '' }
            else { $c = Test-HccServerReachable $u.Url; $out.kind = $c.Kind; $out.message = $c.Message; $out.detail = $c.Detail }
            $out.ok = 1   # a failed check never fails the setup
        }
    }
}
catch { $out.ok = 0; $out.error = $_.Exception.Message; $exit = 1 }
Write-HccResult -Path $OutFile -Values $out
if (-not $OutFile) { $out.GetEnumerator() | ForEach-Object { Write-Host "$($_.Key)=$($_.Value)" } }
exit $exit
