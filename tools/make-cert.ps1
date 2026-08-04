# Erzeugt ein selbst-signiertes Code-Signing-Zertifikat und exportiert es als PFX nach cert\.
# -Trust legt es zusaetzlich in die Vertrauensspeicher des aktuellen Benutzers (ohne Adminrechte),
# damit signierte Dateien auf DIESEM Rechner als gueltig gelten.
#
# Das Passwort hat KEINEN Vorgabewert: sonst steht der Schluessel zum privaten
# Zertifikat im Quelltext, direkt neben dem Hinweis, wo die PFX liegt.
# Reihenfolge: Parameter, sonst WW_CERT_PASSWORD, sonst Abfrage.
param(
    [string]$Subject  = "CN=Jonas (Windows-Wartung)",
    [string]$Password,
    [switch]$Trust
)
$ErrorActionPreference = 'Stop'

if (-not $Password) { $Password = $env:WW_CERT_PASSWORD }
if (-not $Password -and [Environment]::UserInteractive) {
    $sicher = Read-Host -AsSecureString "Passwort fuer die neue PFX vergeben"
    $Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sicher))
}
if (-not $Password) {
    throw "Kein Passwort angegeben. -Password uebergeben oder WW_CERT_PASSWORD setzen."
}
$root    = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$certDir = Join-Path $root 'cert'
New-Item -ItemType Directory -Force -Path $certDir | Out-Null
$pfx = Join-Path $certDir 'WindowsWartung.pfx'
$cer = Join-Path $certDir 'WindowsWartung.cer'

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
    -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter (Get-Date).AddYears(5)

$sec = ConvertTo-SecureString $Password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $sec | Out-Null
Export-Certificate   -Cert $cert -FilePath $cer | Out-Null

if ($Trust) {
    Write-Host "Hinweis: Windows zeigt gleich eine Sicherheitsabfrage zum Vertrauensspeicher (Root)." -ForegroundColor Yellow
    Write-Host "Bitte mit 'Ja' bestaetigen. (Interaktiv ausfuehren, nicht im Hintergrund.)" -ForegroundColor Yellow
    foreach ($name in 'Root','TrustedPublisher') {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($name, 'CurrentUser')
        $store.Open('ReadWrite'); $store.Add($cert); $store.Close()
    }
    "Zertifikat in CurrentUser\Root + TrustedPublisher eingetragen (nur dieser Benutzer)."
}

"PFX:        $pfx"
"Public CER: $cer"
"Thumbprint: $($cert.Thumbprint)"
"Passwort:   $Password   (fuer CI als Secret hinterlegen, NICHT einchecken)"
