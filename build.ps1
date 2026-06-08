# Baut die WebView2-Variante (HTML/CSS-UI in nativem Host) mit dem eingebauten csc.exe.
# Schalter -Release bindet das Admin-Manifest ein (UAC). Ohne -Release: Dev-Build ohne Manifest.
param([switch]$Release, [switch]$Sign, [string]$CertPassword = "wartung")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe nicht gefunden: $csc" }

$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null

if (-not (Test-Path (Join-Path $root 'assets\app.ico'))) {
    & (Join-Path $root 'tools\generate-icon.ps1')
}

Push-Location $root
try {
    $refs = @(
        'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll',
        'System.Web.Extensions.dll',
        'System.IO.Compression.dll','System.IO.Compression.FileSystem.dll',
        'libs\Microsoft.Web.WebView2.Core.dll','libs\Microsoft.Web.WebView2.WinForms.dll'
    ) -join ','

    $sources = @(
        'host\Program.cs','host\ShellForm.cs',
        'src\ActionCatalog.cs','src\MaintenanceAction.cs','src\CommandRunner.cs',
        'src\NativeMethods.cs','src\AssemblyInfo.cs'
    )

    $argList = @(
        '/nologo','/target:winexe','/platform:x64',
        '/out:bin\WindowsWartung.exe',
        "/reference:$refs",
        '/codepage:65001','/langversion:5','/optimize+'
    )
    if (Test-Path 'assets\app.ico') { $argList += '/win32icon:assets\app.ico' }
    if ($Release) { $argList += '/win32manifest:src\app.manifest' }
    $argList += $sources

    & $csc @argList
    $code = $LASTEXITCODE
}
finally { Pop-Location }

if ($code -ne 0) { "`nBUILD FEHLGESCHLAGEN (ExitCode $code)"; exit $code }

# Laufzeit-Dateien neben die Exe legen (managed WebView2-DLLs + nativer Loader)
Copy-Item (Join-Path $root 'libs\*.dll') $bin -Force
$uiDst = Join-Path $bin 'ui'
New-Item -ItemType Directory -Force -Path $uiDst | Out-Null
Copy-Item (Join-Path $root 'ui\*') $uiDst -Recurse -Force -Exclude 'shot_*.png'

if ($Sign) {
    $pfx = Join-Path $root 'cert\WindowsWartung.pfx'
    if (Test-Path $pfx) {
        & (Join-Path $root 'sign.ps1') -File (Join-Path $bin 'WindowsWartung.exe') -Pfx $pfx -Password $CertPassword
    } else {
        "Hinweis: -Sign gesetzt, aber cert\WindowsWartung.pfx fehlt -> Signatur uebersprungen (tools\make-cert.ps1)."
    }
}

"`nBUILD OK  ->  bin\WindowsWartung.exe" + $(if ($Release) { '  (Release/Admin)' } else { '  (Dev)' })
