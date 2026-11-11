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

# --- Name Constraints (RFC 5280 4.2.1.10): a small DER reader (AsnReader does not exist in .NET Framework) --------------------------
function Read-HccTlv {
    param([byte[]]$Bytes, [int]$Pos, [int]$End)
    if ($Pos + 2 -gt $End) { throw 'truncated DER' }
    $tag = [int]$Bytes[$Pos]; $len = [int]$Bytes[$Pos + 1]; $p = $Pos + 2
    if ($len -ge 128) {
        $n = $len -band 127
        if ($n -lt 1 -or $n -gt 3 -or ($p + $n) -gt $End) { throw 'bad DER length' }
        $len = 0
        for ($i = 0; $i -lt $n; $i++) { $len = ($len -shl 8) -bor [int]$Bytes[$p + $i] }
        $p += $n
    }
    if (($p + $len) -gt $End) { throw 'truncated DER' }
    return [pscustomobject]@{ Tag = $tag; Start = $p; End = ($p + $len) }
}
function Read-HccTlvList {
    param([byte[]]$Bytes, [int]$Start, [int]$End)
    $items = New-Object System.Collections.Generic.List[object]
    $pos = $Start
    while ($pos -lt $End) { $t = Read-HccTlv $Bytes $pos $End; $items.Add($t); $pos = $t.End }
    return , $items.ToArray()
}
function ConvertTo-HccIpRange {
    # 4+4 or 16+16 bytes (address, mask) -> Family, Address (masked), Prefix; $null when the mask is not contiguous
    param([byte[]]$B)
    if ($B.Length -ne 8 -and $B.Length -ne 32) { return $null }
    $n = [int]($B.Length / 2)
    $addr = New-Object byte[] $n; $mask = New-Object byte[] $n
    [Array]::Copy($B, 0, $addr, 0, $n); [Array]::Copy($B, $n, $mask, 0, $n)
    $prefix = 0; $seenZero = $false
    foreach ($m in $mask) {
        for ($bit = 7; $bit -ge 0; $bit--) {
            if ((([int]$m -shr $bit) -band 1) -eq 1) { if ($seenZero) { return $null }; $prefix++ } else { $seenZero = $true }
        }
    }
    for ($i = 0; $i -lt $n; $i++) { $addr[$i] = [byte]([int]$addr[$i] -band [int]$mask[$i]) }
    $fam = 6
    if ($n -eq 4) { $fam = 4 }
    return [pscustomobject]@{ Family = $fam; Address = $addr; Prefix = $prefix }
}
function Test-HccIpInRange {
    param([byte[]]$Address, $Range)
    if ($Address.Length -ne $Range.Address.Length) { return $false }
    $bits = $Range.Prefix
    for ($i = 0; $i -lt $Address.Length; $i++) {
        $take = [Math]::Max(0, [Math]::Min(8, $bits - 8 * $i))
        $m = (255 -shl (8 - $take)) -band 255
        if (([int]$Address[$i] -band $m) -ne [int]$Range.Address[$i]) { return $false }
    }
    return $true
}
function Test-HccPrivateRange {
    # The permitted address range must lie inside 10/8, 172.16/12, 192.168/16, 127/8 (IPv4) or ::1/128, fc00::/7 (IPv6).
    param($Range)
    if ($Range.Family -eq 4) {
        foreach ($p in @(@('10.0.0.0', 8), @('172.16.0.0', 12), @('192.168.0.0', 16), @('127.0.0.0', 8))) {
            $net = [Net.IPAddress]::Parse($p[0]).GetAddressBytes()
            $r = [pscustomobject]@{ Family = 4; Address = $net; Prefix = [int]$p[1] }
            if ($Range.Prefix -ge $r.Prefix -and (Test-HccIpInRange $Range.Address $r)) { return $true }
        }
        return $false
    }
    $loop = [Net.IPAddress]::Parse('::1').GetAddressBytes()
    if ($Range.Prefix -eq 128 -and (Test-HccIpInRange $Range.Address ([pscustomobject]@{ Family = 6; Address = $loop; Prefix = 128 }))) { return $true }
    return ($Range.Prefix -ge 7 -and (([int]$Range.Address[0] -band 254) -eq 252))
}
$script:HccPublicSuffixes = @('com', 'net', 'org', 'edu', 'gov', 'mil', 'int', 'info', 'biz', 'name', 'pro', 'io', 'co', 'in', 'us', 'uk', 'de', 'fr', 'cn', 'ru', 'app', 'dev', 'xyz', 'online', 'site', 'shop', 'tech', 'cloud', 'local', 'lan', 'internal', 'home', 'corp', 'localdomain', 'test', 'example', 'invalid')
$script:HccPrivateSuffixes = @('local', 'lan', 'internal', 'home.arpa', 'localdomain', 'intranet', 'corp', 'home')
function Test-HccPermittedDnsName {
    # Allow-list: a single-label name (HOTELPC, localhost) or a name under a private-use suffix. $null when acceptable, else the reason.
    param([string]$Name)
    $d = $Name.Trim().ToLowerInvariant()
    if (-not $d) { return 'an empty name is permitted' }
    if ($d.StartsWith('.')) { return "'$d' starts with a dot (it would cover a whole domain)" }
    if ($d -match '[*\s]') { return "'$d' contains a wildcard" }
    if ($d -notmatch '^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$') { return "'$d' is not a plain host name" }
    if ($d -notmatch '\.') {
        if ($script:HccPublicSuffixes -contains $d) { return "'$d' is a whole top-level domain" }
        if ($d.Length -le 2) { return "'$d' is a country-code top-level domain" }
        return $null
    }
    foreach ($suf in $script:HccPrivateSuffixes) { if ($d.EndsWith('.' + $suf)) { return $null } }
    return "'$d' looks like a real internet name; HCC accepts only single-label names or names ending in .$($script:HccPrivateSuffixes -join ', .')"
}
function Get-HccNameConstraintInfo {
    # Parses the Name Constraints extension of the CA and decides whether it really limits the CA to the hotel server.
    param($Cert)
    $r = [pscustomobject]@{ Ok = $false; Error = ''; PermittedDns = @(); PermittedIp = @(); ExcludedDns = @(); ExcludedIp = @(); Permitted = '' }
    $ext = @($Cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.30' })
    if ($ext.Count -eq 0) { $r.Error = 'This CA has no name restriction, so it could vouch for any website. HCC only installs a CA limited to the hotel server. Make a new certificate on the server (New-HccCertificate.ps1) and a new client kit.'; return $r }
    if ($ext.Count -ne 1) { $r.Error = 'This CA has more than one name restriction, which is not valid.'; return $r }
    if (-not $ext[0].Critical) { $r.Error = 'The name restriction of this CA is not marked critical, so some programs would ignore it. Make a new certificate on the server and a new client kit.'; return $r }
    $pd = New-Object System.Collections.Generic.List[string]; $pi = New-Object System.Collections.Generic.List[object]
    $ed = New-Object System.Collections.Generic.List[string]; $ei = New-Object System.Collections.Generic.List[object]
    try {
        $b = [byte[]]$ext[0].RawData
        $top = Read-HccTlv $b 0 $b.Length
        if ($top.Tag -ne 0x30 -or $top.End -ne $b.Length) { throw 'not a NameConstraints sequence' }
        foreach ($part in (Read-HccTlvList $b $top.Start $top.End)) {
            if ($part.Tag -ne 0xA0 -and $part.Tag -ne 0xA1) { continue }
            foreach ($sub in (Read-HccTlvList $b $part.Start $part.End)) {
                if ($sub.Tag -ne 0x30) { throw 'bad subtree' }
                $kids = Read-HccTlvList $b $sub.Start $sub.End
                if ($kids.Count -eq 0) { throw 'empty subtree' }
                $base = $kids[0]
                $val = New-Object byte[] ($base.End - $base.Start)
                [Array]::Copy($b, $base.Start, $val, 0, $val.Length)
                if ($base.Tag -eq 0x82) {
                    $n = [Text.Encoding]::ASCII.GetString($val)
                    if ($part.Tag -eq 0xA0) { $pd.Add($n) } else { $ed.Add($n) }
                }
                elseif ($base.Tag -eq 0x87) {
                    $rg = ConvertTo-HccIpRange $val
                    if ($null -eq $rg) { throw 'bad address range' }
                    if ($part.Tag -eq 0xA0) { $pi.Add($rg) } else { $ei.Add($rg) }
                }
            }
        }
    }
    catch { $r.Error = "The name restriction of this CA cannot be read ($($_.Exception.Message))."; return $r }
    $r.PermittedDns = $pd.ToArray(); $r.PermittedIp = $pi.ToArray(); $r.ExcludedDns = $ed.ToArray(); $r.ExcludedIp = $ei.ToArray()
    if ($pd.Count -eq 0) { $r.Error = 'The name restriction of this CA permits no host names, so it does not limit it to the hotel server.'; return $r }
    foreach ($d in $pd) { $why = Test-HccPermittedDnsName $d; if ($why) { $r.Error = "The name restriction of this CA is too wide: $why."; return $r } }
    foreach ($rg in $pi) { if (-not (Test-HccPrivateRange $rg)) { $r.Error = 'The name restriction of this CA permits addresses outside the private hotel network (10.x, 172.16-31.x, 192.168.x, 127.x).'; return $r } }
    foreach ($fam in 4, 6) {
        $has = @($pi | Where-Object { $_.Family -eq $fam }).Count -gt 0
        $excl = @($ei | Where-Object { $_.Family -eq $fam -and $_.Prefix -eq 0 }).Count -gt 0
        if (-not $has -and -not $excl) { $r.Error = "The name restriction of this CA does not limit IPv$fam addresses."; return $r }
    }
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($d in $pd) { $names.Add($d) }
    foreach ($rg in $pi) {
        $a = (New-Object Net.IPAddress -ArgumentList (, [byte[]]$rg.Address)).ToString()
        if ($rg.Prefix -eq $rg.Address.Length * 8) { $names.Add($a) } else { $names.Add("$a/$($rg.Prefix)") }
    }
    $r.Permitted = $names -join ', '
    $r.Ok = $true
    return $r
}
function Test-HccHostPermitted {
    # Is the server host inside what the CA may vouch for? Returns $null when yes, else the reason.
    param($Info, [string]$HostName)
    $h = $HostName.Trim().ToLowerInvariant()
    $ip = $null
    if ([Net.IPAddress]::TryParse($h, [ref]$ip)) {
        $bytes = $ip.GetAddressBytes()
        foreach ($x in $Info.ExcludedIp) { if (Test-HccIpInRange $bytes $x) { return "The server address $h is excluded by the hotel certificate." } }
        foreach ($p in $Info.PermittedIp) { if (Test-HccIpInRange $bytes $p) { return $null } }
        return "The server address $h is not one of the addresses this certificate may vouch for ($($Info.Permitted)). This kit does not belong to this server."
    }
    foreach ($x in $Info.ExcludedDns) { $xd = $x.ToLowerInvariant().TrimStart('.'); if ($h -eq $xd -or $h.EndsWith('.' + $xd)) { return "The server name $h is excluded by the hotel certificate." } }
    foreach ($p in $Info.PermittedDns) { $pn = $p.ToLowerInvariant(); if ($h -eq $pn -or $h.EndsWith('.' + $pn)) { return $null } }
    return "The server name $h is not one of the names this certificate may vouch for ($($Info.Permitted)). This kit does not belong to this server."
}

# Reads ONE certificate (DER or PEM) and decides whether it may be trusted as the hotel CA.
function Get-HccCaFileInfo {
    param([string]$Path)
    $r = [pscustomobject]@{ Ok = $false; Error = ''; Subject = ''; Sha256 = ''; NotAfter = ''; NameConstrained = $false; Permitted = ''; NameConstraints = $null; Cert = $null }
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { $r.Error = "The certificate file '$Path' was not found."; return $r }
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -eq 0 -or $bytes.Length -gt 65536) { $r.Error = 'This is not a certificate file (empty or too large).'; return $r }
    $text = [Text.Encoding]::ASCII.GetString($bytes)
    if ($text -match 'PRIVATE KEY') { $r.Error = 'This file contains a private key. Never copy a private key to other PCs. Use only the public HCC-Hotel-CA.cer from the HCC client kit.'; return $r }
    if ($text -match '-----BEGIN') {
        $blocks = [regex]::Matches($text, '-----BEGIN CERTIFICATE-----([A-Za-z0-9+/=\s]+)-----END CERTIFICATE-----')
        if ($blocks.Count -ne 1) { $r.Error = "The file must hold exactly one certificate (it holds $($blocks.Count))."; return $r }
        try { $bytes = [Convert]::FromBase64String(($blocks[0].Groups[1].Value -replace '\s', '')) } catch { $r.Error = 'This file is not a certificate (.cer) file.'; return $r }
    }
    try { $type = [string][Security.Cryptography.X509Certificates.X509Certificate2]::GetCertContentType($bytes) } catch { $type = 'Unknown' }
    if ($type -ne 'Cert') { $r.Error = 'This file is not a single plain certificate (.cer). Key files (.pfx), certificate bundles and other formats are refused.'; return $r }
    try { $cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList (, [byte[]]$bytes) }
    catch { $r.Error = 'This file is not a certificate (.cer) file.'; return $r }
    $r.Subject = $cert.Subject; $r.Cert = $cert
    $r.Sha256 = Get-HccCertificateSha256 $cert
    $r.NotAfter = $cert.NotAfter.ToString('yyyy-MM-dd')
    if ($cert.HasPrivateKey) { $r.Error = 'This certificate file carries a private key and is refused.'; return $r }
    $bc = @($cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' })
    if ($bc.Count -eq 0 -or -not $bc[0].CertificateAuthority) { $r.Error = 'This is not a CA certificate (it is a server or user certificate). Use HCC-Hotel-CA.cer from the HCC client kit.'; return $r }
    if (-not $bc[0].Critical) { $r.Error = 'This CA certificate does not mark itself critical as a CA. Use HCC-Hotel-CA.cer from the HCC client kit.'; return $r }
    $ku = @($cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.15' })
    if ($ku.Count -gt 0 -and -not ($ku[0].KeyUsages -band [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign)) { $r.Error = 'This CA certificate is not allowed to sign certificates.'; return $r }
    if ($cert.Subject -ne $cert.Issuer) { $r.Error = 'This certificate is not a self-signed root. Use HCC-Hotel-CA.cer from the HCC client kit.'; return $r }
    if ($cert.SignatureAlgorithm.FriendlyName -match 'sha1|md5') { $r.Error = 'This certificate uses an old, weak signature (SHA-1/MD5).'; return $r }
    try { $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($cert) } catch { $rsa = $null }
    if ($rsa -and $rsa.KeySize -lt 2048) { $r.Error = 'This certificate has a weak key (under 2048 bits).'; return $r }
    if ($cert.NotAfter -lt (Get-Date)) { $r.Error = "This certificate expired on $($r.NotAfter)."; return $r }
    if ($cert.NotBefore -gt (Get-Date).AddDays(1)) { $r.Error = 'This certificate is not valid yet: check the date and time of this PC.'; return $r }
    $nc = Get-HccNameConstraintInfo $cert
    $r.NameConstraints = $nc
    $r.NameConstrained = $nc.Ok
    $r.Permitted = $nc.Permitted
    if (-not $nc.Ok) { $r.Error = $nc.Error; return $r }
    $r.Ok = $true
    return $r
}

# Hotel name shown on the confirmation page: 1-100 characters, no control / format / line-separator characters.
function Test-HccHotelName {
    param([string]$Name)
    if ($null -eq $Name) { return $false }
    $n = $Name.Trim()
    return ($n.Length -ge 1 -and $n.Length -le 100 -and $n -notmatch '[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]')
}

function Read-HccClientConfig {
    param([string]$Path)
    $r = [pscustomobject]@{ Ok = $false; Error = ''; HotelName = ''; ServerUrl = ''; CaSha256 = '' }
    try { $j = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8) | ConvertFrom-Json } catch { $r.Error = "hcc-client.json cannot be read: $($_.Exception.Message)"; return $r }
    foreach ($n in 'serverUrl', 'hotelName', 'caSha256') { if (-not $j.PSObject.Properties[$n]) { $r.Error = "hcc-client.json has no '$n'."; return $r } }
    # empty / null caSha256 = "no fingerprint known" (never a match)
    if ($null -ne $j.hotelName) { $r.HotelName = [string]$j.hotelName }
    if ($null -ne $j.serverUrl) { $r.ServerUrl = [string]$j.serverUrl }
    if ($null -ne $j.caSha256) { $r.CaSha256 = [string]$j.caSha256 }
    $r.Ok = $true
    return $r
}

# Everything the setup checks before it asks the person to confirm: certificate, address, fingerprint, host inside the CA's names.
function Test-HccClientInputs {
    param([string]$CerPath, [string]$ConfigPath, [string]$ServerUrl)
    $r = [pscustomobject]@{ Ok = $false; Error = ''; HotelName = ''; ServerUrl = ''; Subject = ''; Sha256 = ''; Sha256Display = ''; NotAfter = ''; Permitted = ''; ExpectedKnown = $false; FingerprintMatches = $false }
    $expected = ''
    $url = $ServerUrl
    if ($ConfigPath) {
        $c = Read-HccClientConfig $ConfigPath
        if (-not $c.Ok) { $r.Error = $c.Error; return $r }
        if (Test-HccHotelName $c.HotelName) { $r.HotelName = $c.HotelName.Trim() } else { $r.HotelName = '(name not shown: not valid)' }
        $url = $c.ServerUrl
        if ($c.CaSha256 -and $c.CaSha256.Trim()) {
            $expected = ConvertTo-HccFingerprint $c.CaSha256
            if (-not $expected) { $r.Error = 'hcc-client.json has a caSha256 that is not a SHA-256 fingerprint.'; return $r }
        }
    }
    $u = Test-HccServerUrl $url
    if (-not $u.Ok) { $r.Error = $u.Error; return $r }
    $r.ServerUrl = $u.Url
    $ca = Get-HccCaFileInfo $CerPath
    $r.Subject = $ca.Subject; $r.Sha256 = $ca.Sha256; $r.NotAfter = $ca.NotAfter; $r.Permitted = $ca.Permitted
    if ($ca.Sha256) { $r.Sha256Display = Format-HccFingerprint $ca.Sha256 }
    if (-not $ca.Ok) { $r.Error = $ca.Error; return $r }
    if ($expected) {
        $r.ExpectedKnown = $true
        if ($expected -ne $ca.Sha256) { $r.Error = "The certificate file does not match the kit: its fingerprint is $($r.Sha256Display) but hcc-client.json expects $(Format-HccFingerprint $expected). The files are from different kits or one was changed. Copy the kit folder from the server again."; return $r }
        $r.FingerprintMatches = $true
    }
    $why = Test-HccHostPermitted $ca.NameConstraints $u.Host
    if ($why) { $r.Error = $why; return $r }
    $r.Ok = $true
    return $r
}

# Adds the one certificate to LocalMachine\Root. Re-checks everything itself and compares with the fingerprint the person approved.
function Add-HccTrustedRoot {
    [CmdletBinding(SupportsShouldProcess)]
    param([string]$CerPath, [string]$ApprovedSha256, [scriptblock]$StoreFactory = { New-Object Security.Cryptography.X509Certificates.X509Store('Root', [Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine) })
    $ca = Get-HccCaFileInfo $CerPath
    if (-not $ca.Ok) { throw $ca.Error }
    $approved = ConvertTo-HccFingerprint $ApprovedSha256
    if (-not $approved -or $approved -ne $ca.Sha256) { throw 'The certificate is not the one that was confirmed (fingerprint differs). Nothing was installed.' }
    if (-not $PSCmdlet.ShouldProcess('LocalMachine\Root', "Trust the hotel CA $($ca.Sha256)")) { return 'whatif' }
    $store = & $StoreFactory   # the seam: tests pass a fake store, so no test touches LocalMachine\Root
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
    # Removes only a certificate that is an HCC-style hotel CA (self-signed CA with private-network Name Constraints). A Microsoft or public
    # root is never removed, whatever the caller's list says.
    [CmdletBinding(SupportsShouldProcess)]
    param([string]$Sha256, [scriptblock]$StoreFactory = { New-Object Security.Cryptography.X509Certificates.X509Store('Root', [Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine) })
    $want = ConvertTo-HccFingerprint $Sha256
    if (-not $want) { throw 'Not a SHA-256 fingerprint.' }
    $find = {
        param($Flags)
        $st = & $StoreFactory
        try {
            $st.Open($Flags)
            return , @($st.Certificates | Where-Object { (Get-HccCertificateSha256 $_) -eq $want })
        }
        finally { $st.Close() }
    }
    $hit = & $find ([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    if ($hit.Count -eq 0) { return 'absent' }
    foreach ($c in $hit) {
        $isCa = @($c.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' -and $_.CertificateAuthority }).Count -gt 0
        if (-not $isCa -or $c.Subject -ne $c.Issuer -or -not (Get-HccNameConstraintInfo $c).Ok) { return 'refused: not an HCC hotel CA' }
    }
    if (-not $PSCmdlet.ShouldProcess('LocalMachine\Root', "Remove the hotel CA $want")) { return 'whatif' }
    $store = & $StoreFactory   # opened read-write only now
    try {
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        foreach ($c in @($store.Certificates | Where-Object { (Get-HccCertificateSha256 $_) -eq $want })) { $store.Remove($c) }
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
            $out.permitted = $i.Permitted; $out.expectedKnown = [int]$i.ExpectedKnown; $out.matches = [int]$i.FingerprintMatches
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
