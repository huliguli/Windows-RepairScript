# Baut die WebView2-Variante (HTML/CSS-UI in nativem Host).
#
# Compiler: Roslyn aus dem .NET SDK, Zielrahmen bleibt .NET Framework 4.8.
# Damit stehen moderne C#-Sprachmittel zur Verfuegung (Nullable, Musterabgleich,
# Datensaetze, switch-Ausdruecke), waehrend die EXE weiterhin nur ~128 KB wiegt und
# ohne Laufzeit-Installation auf jedem Windows 11 laeuft - .NET Framework 4.8.1 ist
# dort ab Werk vorhanden und hat kein Support-Enddatum.
#
# Der eingebaute csc.exe des Frameworks kann das NICHT: er lehnt jedes
# /langversion oberhalb von 5 mit CS1617 ab. Deshalb ist das SDK Pflicht.
#
# Schalter -Release bindet das Admin-Manifest ein (UAC). Ohne -Release: Dev-Build ohne Manifest.
param([switch]$Release, [switch]$Sign, [string]$CertPassword = "wartung")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# ---------- Werkzeuge suchen ----------

# Roslyn-Compiler des neuesten installierten SDK.
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw @"
Das .NET SDK wurde nicht gefunden (dotnet ist nicht im PATH).

Der Bau nutzt den Roslyn-Compiler aus dem SDK, weil der in Windows eingebaute
Compiler nur C# 5 beherrscht. Das SDK ist NUR zum Bauen noetig - die fertige EXE
laeuft weiterhin ohne .NET-Installation.

Abhilfe: https://dotnet.microsoft.com/download  (SDK, nicht nur Runtime)
"@
}
$sdkRoot = Join-Path (Split-Path -Parent $dotnet.Source) 'sdk'
$csc = Get-ChildItem (Join-Path $sdkRoot '*\Roslyn\bincore\csc.dll') -ErrorAction SilentlyContinue |
       Sort-Object { [version]($_.FullName -replace '.*\\sdk\\([0-9.]+)\\.*','$1') } |
       Select-Object -Last 1
if (-not $csc) { throw "Kein Roslyn-Compiler im SDK gefunden (gesucht unter $sdkRoot\*\Roslyn\bincore\csc.dll)." }

# Referenzassemblies fuer .NET Framework 4.8. Bevorzugt das SDK-Paket (auf Bau-Servern
# vorhanden), sonst die lokal installierten Targeting Packs.
$refCandidates = @(
    (Join-Path $sdkRoot '..\packs\Microsoft.NETFramework.ReferenceAssemblies.net48\*\build\.NETFramework\v4.8'),
    "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
)
$ref = $null
foreach ($c in $refCandidates) {
    $hit = Get-Item $c -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($hit -and (Test-Path (Join-Path $hit.FullName 'mscorlib.dll'))) { $ref = $hit.FullName; break }
}
if (-not $ref) {
    throw @"
Die Referenzassemblies fuer .NET Framework 4.8 wurden nicht gefunden.

Abhilfe (eine Variante genuegt):
  * Developer Pack installieren: https://dotnet.microsoft.com/download/dotnet-framework/net48
  * oder in der CI:  nuget install Microsoft.NETFramework.ReferenceAssemblies.net48
"@
}

$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null

if (-not (Test-Path (Join-Path $root 'assets\app.ico'))) {
    & (Join-Path $root 'tools\generate-icon.ps1')
}

# ---------- Uebersetzen ----------

Push-Location $root
try {
    # /nostdlib+ schaltet die implizite mscorlib des SDK ab; die 4.8-Variante wird
    # unten explizit referenziert. Ohne das mischt Roslyn die .NET-Core-Basisklassen dazu.
    $frameworkRefs = @(
        'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll',
        'System.Windows.Forms.dll', 'System.Web.Extensions.dll',
        'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll'
    ) | ForEach-Object { "/reference:$ref\$_" }

    $localRefs = @(
        '/reference:libs\Microsoft.Web.WebView2.Core.dll',
        '/reference:libs\Microsoft.Web.WebView2.WinForms.dll'
    )

    $sources = @(
        'host\Program.cs','host\ShellForm.cs','host\CheckFlow.cs','host\Autostart.cs',
        'src\ActionCatalog.cs','src\MaintenanceAction.cs','src\CommandRunner.cs',
        'src\NativeMethods.cs','src\History.cs','src\RestorePoints.cs','src\PowerPlans.cs',
        'src\AppxCleaner.cs','src\Explain.cs','src\Scheduler.cs','src\AutoRunner.cs',
        'src\AppLog.cs','src\Shell.cs','src\Diagnostics.cs','src\AssemblyInfo.cs'
    )

    $argList = @(
        $csc.FullName,
        '/nologo','/nostdlib+','/target:winexe','/platform:x64',
        '/out:bin\WindowsWartung.exe',
        '/codepage:65001','/langversion:latest','/optimize+',
        '/warnaserror-','/nowarn:1701,1702'
    )
    if (Test-Path 'assets\app.ico') { $argList += '/win32icon:assets\app.ico' }
    if ($Release) { $argList += '/win32manifest:src\app.manifest' }
    $argList += $frameworkRefs
    $argList += $localRefs
    $argList += $sources

    & dotnet @argList
    $code = $LASTEXITCODE
}
finally { Pop-Location }

if ($code -ne 0) { "`nBUILD FEHLGESCHLAGEN (ExitCode $code)"; exit $code }

# ---------- Laufzeit-Dateien neben die Exe legen ----------
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

$sdkVer = ($csc.FullName -replace '.*\\sdk\\([0-9.]+)\\.*','$1')
"`nBUILD OK  ->  bin\WindowsWartung.exe" + $(if ($Release) { '  (Release/Admin)' } else { '  (Dev)' })
"           Compiler: Roslyn aus SDK $sdkVer, C# latest, Ziel .NET Framework 4.8"
