# Signiert eine Datei mit einer PFX (Authenticode, SHA256, mit Zeitstempel) - ohne SDK, nur PowerShell.
# Das Passwort hat KEINEN Vorgabewert mehr. Ein fest verdrahtetes Standardpasswort im
# Quelltext ist genau das, was ein Angreifer als Erstes versucht - und es stand hier
# zusammen mit dem Hinweis, wo die PFX liegt.
# Reihenfolge: Parameter, sonst Umgebungsvariable WW_CERT_PASSWORD, sonst Abfrage.
param(
    [string]$File,
    [string]$Pfx,
    [string]$Password,
    [string]$Timestamp = "http://timestamp.digicert.com"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $File) { $File = Join-Path $root 'bin\WindowsWartung.exe' }
if (-not $Pfx)  { $Pfx  = Join-Path $root 'cert\WindowsWartung.pfx' }

if (-not (Test-Path $File)) { throw "Datei fehlt: $File" }
if (-not (Test-Path $Pfx))  { throw "PFX fehlt: $Pfx  (zuerst:  tools\make-cert.ps1)" }

if (-not $Password) { $Password = $env:WW_CERT_PASSWORD }
if (-not $Password) {
    if ([Environment]::UserInteractive -and -not $env:CI) {
        $sicher = Read-Host -AsSecureString "Passwort der PFX"
        $Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sicher))
    }
}
if (-not $Password) {
    throw @"
Kein Passwort fuer die PFX angegeben.

Moeglichkeiten:
  * -Password uebergeben
  * Umgebungsvariable WW_CERT_PASSWORD setzen
  * interaktiv aufrufen, dann wird gefragt
"@
}

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
