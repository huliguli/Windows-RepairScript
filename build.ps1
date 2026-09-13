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
# Manifest (src\app.manifest, asInvoker seit 8.1): wird IMMER eingebettet, Dev wie Release.
# Die EXE startet ohne UAC-Dialog; Administratorrechte holt sich erst der Helfer, wenn eine
# Massnahme sie braucht (docs\M2-ENTWURF.md, Abschnitt 0 Punkt 1 und 8).
# Schalter -Release aendert nur noch die Abschlusszeile ("(Release)" statt "(Dev)"); er wird
# sonst nirgends gelesen. Die Signatur haengt allein an -Sign, /optimize+ ist immer an, das
# Manifest immer dabei: ein Bau ohne -Sign ist technisch dieselbe EXE, nur unsigniert.
# CertPassword ohne Vorgabe: sign.ps1 fragt sonst nach bzw. nimmt WW_CERT_PASSWORD.
#
# Startpruefung: vor dem Uebersetzen entsteht ui-hashes.txt (SHA-256 jeder Datei unter ui\,
# ohne shot_*.png) und wird als Ressource "ui-hashes.txt" in die EXE eingebettet. Die App
# prueft beim Start die kopierten Oberflaechendateien dagegen (host\Selbstpruefung.cs).
param([switch]$Release, [switch]$Sign, [string]$CertPassword)

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
# Nur SDK-Ordner, deren Name eine reine Versionsnummer ist (10.0.401). Ein Vorab-SDK heisst
# 10.0.200-preview.1.25120.5; der [version]-Cast wirft daran, und unter 'Stop' brach der ganze
# Bau ab, obwohl ein stabiles SDK daneben lag (13.09.2026). Vorab-Fassungen bleiben aussen vor.
$sdkDir = Get-ChildItem $sdkRoot -Directory -ErrorAction SilentlyContinue |
          Where-Object { $_.Name -match '^[0-9.]+$' -and (Test-Path (Join-Path $_.FullName 'Roslyn\bincore\csc.dll')) } |
          Sort-Object { [version]$_.Name } | Select-Object -Last 1
if (-not $sdkDir) { throw "Kein Roslyn-Compiler in einem freigegebenen SDK gefunden (gesucht unter $sdkRoot\<version>\Roslyn\bincore\csc.dll; Ordner mit Bindestrich im Namen sind Vorab-Fassungen und zaehlen nicht)." }
$csc = Get-Item (Join-Path $sdkDir.FullName 'Roslyn\bincore\csc.dll')

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

# ---------- Pruefliste der Oberflaechendateien ----------
# Eine Zeile je Datei unter ui\ (rekursiv, ohne shot_*.png):  <sha256 klein hex>  <pfad>
# Pfad relativ zu ui\ mit "/" als Trenner, ordinal sortiert, UTF-8 ohne BOM, LF-Enden.
# Die Datei wird als Ressource "ui-hashes.txt" eingebettet; host\Selbstpruefung.cs liest
# sie mit demselben Format. Wer das Format aendert, aendert beide Seiten.
# Windows PowerShell 5.1 (CI) schreibt mit Set-Content -Encoding UTF8 eine BOM - deshalb
# WriteAllText mit eigenem Encoding.
$uiRoot = Join-Path $root 'ui'
$uiHashDatei = Join-Path $env:TEMP 'ui-hashes.txt'
$uiPfade = New-Object System.Collections.Generic.List[string]
$uiHashes = @{}
foreach ($f in @(Get-ChildItem $uiRoot -File -Recurse | Where-Object { $_.Name -notlike 'shot_*.png' })) {
    $rel = $f.FullName.Substring($uiRoot.Length).TrimStart('\') -replace '\\', '/'
    $uiPfade.Add($rel)
    $uiHashes[$rel] = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
if ($uiPfade.Count -eq 0) { throw "Keine Oberflaechendatei unter $uiRoot gefunden - die Pruefliste waere leer." }
$uiPfade.Sort([StringComparer]::Ordinal)
$uiZeilen = foreach ($p in $uiPfade) { $uiHashes[$p] + '  ' + $p }
[IO.File]::WriteAllText($uiHashDatei, (($uiZeilen -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))
"Pruefliste: $($uiPfade.Count) Oberflaechendateien -> $uiHashDatei"

# ---------- Uebersetzen ----------

Push-Location $root
try {
    # /nostdlib+ schaltet die implizite mscorlib des SDK ab; die 4.8-Variante wird
    # unten explizit referenziert. Ohne das mischt Roslyn die .NET-Core-Basisklassen dazu.
    # System.Management, System.ServiceProcess, System.Runtime.Serialization und System.Xml sind
    # seit v8 fuer Kern und Sammler dabei: WMI, Dienste und JSON laufen im eigenen Prozess,
    # ohne powershell.exe. Alle vier liegen in den net48-Referenzassemblies.
    $frameworkRefs = @(
        'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll',
        'System.Windows.Forms.dll', 'System.Web.Extensions.dll',
        'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll',
        'System.Management.dll', 'System.ServiceProcess.dll',
        'System.Runtime.Serialization.dll', 'System.Xml.dll'
    ) | ForEach-Object { "/reference:$ref\$_" }

    $localRefs = @(
        '/reference:libs\Microsoft.Web.WebView2.Core.dll',
        '/reference:libs\Microsoft.Web.WebView2.WinForms.dll'
    )

    # kern\ und sammler\ kommen als Ordner - eine neue Regel- oder Quellendatei ist automatisch
    # dabei. AufzeichnenCli.cs hat einen eigenen Main und gehoert nur zur Kommandozeile
    # (tools\bau-kern.ps1). src\Diagnostics.cs ist seit v8 durch kern\Regeln ersetzt.
    # host\, src\ und helfer\ kommen ebenfalls als Ordner (seit 8.1): eine neue Datei ist automatisch dabei.
    $sources = @()
    $sources += Get-ChildItem 'host\*.cs' | ForEach-Object { 'host\' + $_.Name }
    $sources += Get-ChildItem 'src\*.cs' | ForEach-Object { 'src\' + $_.Name }
    $sources += Get-ChildItem 'helfer\*.cs' -ErrorAction SilentlyContinue | ForEach-Object { 'helfer\' + $_.Name }
    $sources += Get-ChildItem 'kern\*.cs' | ForEach-Object { 'kern\' + $_.Name }
    $sources += Get-ChildItem 'kern\Regeln\*.cs' | ForEach-Object { 'kern\Regeln\' + $_.Name }
    $sources += Get-ChildItem 'sammler\*.cs' | Where-Object { $_.Name -ne 'AufzeichnenCli.cs' } | ForEach-Object { 'sammler\' + $_.Name }
    $sources += Get-ChildItem 'sammler\Quellen\*.cs' | ForEach-Object { 'sammler\Quellen\' + $_.Name }

    $argList = @(
        $csc.FullName,
        '/nologo','/nostdlib+','/target:winexe','/platform:x64',
        '/out:bin\WindowsWartung.exe',
        '/codepage:65001','/langversion:latest','/optimize+',
        '/warnaserror-','/nowarn:1701,1702'
    )
    if (Test-Path 'assets\app.ico') { $argList += '/win32icon:assets\app.ico' }
    # Manifest immer (asInvoker), nicht nur im Release: ein Dev-Bau ohne Manifest bekaeme
    # von Windows die Installer-Erkennung und wuerde anders starten als das Release.
    # Die Testprobe verlangt diese Zeile ohne Release-Bedingung davor.
    $argList += '/win32manifest:src\app.manifest'
    $argList += "/resource:$uiHashDatei,ui-hashes.txt"
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
# Erst leeren, dann kopieren: eine unter ui\ geloeschte Datei bliebe sonst in bin\ui liegen,
# und die Startpruefung meldete sie bei jedem Dev-Start als "zusaetzlich".
Remove-Item (Join-Path $uiDst '*') -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root 'ui\*') $uiDst -Recurse -Force -Exclude 'shot_*.png'

if ($Sign) {
    $pfx = Join-Path $root 'cert\WindowsWartung.pfx'
    if (Test-Path $pfx) {
        $signArgs = @{ File = (Join-Path $bin 'WindowsWartung.exe'); Pfx = $pfx }
        if ($CertPassword) { $signArgs['Password'] = $CertPassword }
        & (Join-Path $root 'sign.ps1') @signArgs
    } else {
        "Hinweis: -Sign gesetzt, aber cert\WindowsWartung.pfx fehlt -> Signatur uebersprungen (tools\make-cert.ps1)."
    }
}

$sdkVer = $sdkDir.Name
"`nBUILD OK  ->  bin\WindowsWartung.exe" + $(if ($Release) { '  (Release)' } else { '  (Dev)' })
"           Compiler: Roslyn aus SDK $sdkVer, C# latest, Ziel .NET Framework 4.8"
