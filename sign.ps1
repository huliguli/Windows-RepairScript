# Signiert eine Datei mit einer PFX (Authenticode, SHA256, mit Zeitstempel) - ohne SDK, nur PowerShell.
param(
    [string]$File,
    [string]$Pfx,
    [string]$Password  = "wartung",
    [string]$Timestamp = "http://timestamp.digicert.com"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $File) { $File = Join-Path $root 'bin\WindowsWartung.exe' }
if (-not $Pfx)  { $Pfx  = Join-Path $root 'cert\WindowsWartung.pfx' }

if (-not (Test-Path $File)) { throw "Datei fehlt: $File" }
if (-not (Test-Path $Pfx))  { throw "PFX fehlt: $Pfx  (zuerst:  tools\make-cert.ps1)" }

$flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable
$cert  = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Pfx, $Password, $flags)

$res = $null
try {
    $res = Set-AuthenticodeSignature -FilePath $File -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $Timestamp -ErrorAction Stop
} catch {
    Write-Host "Zeitstempel fehlgeschlagen ($($_.Exception.Message)) - signiere ohne Zeitstempel."
    $res = Set-AuthenticodeSignature -FilePath $File -Certificate $cert -HashAlgorithm SHA256
}

"Datei:     $File"
"Status:    $($res.Status)"
"Signierer: $($res.SignerCertificate.Subject)"
if ($res.Status -ne 'Valid') {
    "Hinweis: 'Valid' erscheint nur, wenn das Zertifikat vertraut wird (siehe tools\make-cert.ps1 -Trust)."
}
