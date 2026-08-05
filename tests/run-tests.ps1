# Pruefungen, die bei jedem Bau und in der CI laufen.
#
# Hintergrund: Regeln, die nur in einem Dokument stehen, werden ueberlesen. Genau so sind
# die Farbwolken, das Korn und die falsch geschlossenen Anfuehrungszeichen ins Projekt
# gekommen. Deshalb stehen sie hier als Test - ein Verstoss bricht den Bau.
#
# Aufruf:  .\tests\run-tests.ps1
# Ergebnis: ExitCode 0 = alles gruen, 1 = mindestens ein Test rot.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$failed = 0
$passed = 0

function Test-Result([string]$name, [bool]$ok, [string[]]$details) {
    if ($ok) {
        Write-Host ("  [ok]   " + $name) -ForegroundColor Green
        $script:passed++
    } else {
        Write-Host ("  [FEHL] " + $name) -ForegroundColor Red
        foreach ($d in $details) { Write-Host ("         " + $d) -ForegroundColor DarkYellow }
        $script:failed++
    }
}

# Alle nutzersichtbaren Quellen. bin\ ist nur eine Kopie und wird ausgelassen.
$uiFiles  = Get-ChildItem (Join-Path $root 'ui') -File -Include *.js,*.css,*.html -Recurse
$csFiles  = Get-ChildItem (Join-Path $root 'src'),(Join-Path $root 'host') -File -Filter *.cs -Recurse
# README und CHANGELOG sind kundennahe Texte: der Changelog wird woertlich zur
# Release-Beschreibung auf GitHub. Sie gehoeren damit in die Sprachpruefung.
$docFiles = @(Get-Item (Join-Path $root 'README.md')) + @(Get-Item (Join-Path $root 'CHANGELOG.md'))
$allFiles = @($uiFiles) + @($csFiles) + @($docFiles)

Write-Host "`nOberflaeche laeuft ueberhaupt" -ForegroundColor Cyan

# Ein Syntaxfehler in app.js legt die GESAMTE Oberflaeche lahm: kein Symbol, kein
# Katalog, keine Reaktion - und der C#-Bau merkt davon nichts, weil er JavaScript
# gar nicht ansieht. Real passiert beim Zurueckholen der Warteschlange.
$hits = @()
$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) {
    $hits += "node nicht gefunden - die Syntaxpruefung der Oberflaeche konnte NICHT laufen"
} else {
    foreach ($f in (Get-ChildItem (Join-Path $root 'ui') -Filter *.js)) {
        $out = & node --check $f.FullName 2>&1
        if ($LASTEXITCODE -ne 0) {
            $hits += "$($f.Name): " + (($out | Select-Object -First 3) -join ' | ')
        }
    }
}
Test-Result "JavaScript der Oberflaeche ist fehlerfrei lesbar" ($hits.Count -eq 0) $hits

# Jede Kennung, die app.js anspricht, muss es in index.html auch geben. Ein Tippfehler
# hier fuehrt zu einem stillen null-Zugriff mitten im Ablauf.
$html = [IO.File]::ReadAllText((Join-Path $root 'ui\index.html'), [Text.Encoding]::UTF8)
$js   = [IO.File]::ReadAllText((Join-Path $root 'ui\app.js'), [Text.Encoding]::UTF8)
$vorhanden = [regex]::Matches($html, 'id="([a-zA-Z0-9_-]+)"') | ForEach-Object { $_.Groups[1].Value }
$hits = @()
foreach ($m in ([regex]::Matches($js, "\`$\('#([a-zA-Z0-9_-]+)'\)") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)) {
    # Kennungen, die app.js selbst erzeugt, stehen nicht im HTML: entweder als
    # id="..." in einem erzeugten HTML-Schnipsel oder per .id = '...' zur Laufzeit.
    if ($js -match ("id=""" + $m + """")) { continue }
    if ($js -match ("\.id\s*=\s*'" + $m + "'")) { continue }
    if ($js -match ("'" + $m + "'\s*\+")) { continue }
    if ($vorhanden -notcontains $m) { $hits += "app.js sucht #$m - gibt es in index.html nicht" }
}
Test-Result "Alle angesprochenen Kennungen existieren im HTML" ($hits.Count -eq 0) $hits

# svg('name') mit unbekanntem Namen liefert stillschweigend ein LEERES Symbol - man
# sieht einen leeren Kasten und sucht den Fehler an der falschen Stelle. Real passiert
# beim Vormerken-Knopf: 'plus' fehlte im Satz.
$csText  = [IO.File]::ReadAllText((Join-Path $root 'src\ActionCatalog.cs'), [Text.Encoding]::UTF8)
$iconsJs = [IO.File]::ReadAllText((Join-Path $root 'ui\icons.js'), [Text.Encoding]::UTF8)
$bekannt = [regex]::Matches($iconsJs, "(?m)^\s*([a-zA-Z][a-zA-Z0-9]*)\s*:\s*'") | ForEach-Object { $_.Groups[1].Value }
$hits = @()
foreach ($n in ([regex]::Matches($js, "svg\('([a-zA-Z][a-zA-Z0-9]*)'") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)) {
    if ($bekannt -notcontains $n) { $hits += "svg('$n') - dieses Symbol gibt es in icons.js nicht" }
}
# Auch die aus C# kommenden Symbolnamen des Katalogs muessen existieren.
foreach ($n in ([regex]::Matches($csText, 'Icon = "([a-zA-Z][a-zA-Z0-9]*)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)) {
    if ($bekannt -notcontains $n) { $hits += "Katalog nutzt Symbol '$n' - gibt es in icons.js nicht" }
}
Test-Result "Alle verwendeten Symbole existieren ($($bekannt.Count) im Satz)" ($hits.Count -eq 0) $hits

Write-Host "`nAnti-AI-Slop, Optik (Teil B)" -ForegroundColor Cyan

# Tell 6: radiale Farbwolken und Korn-Overlay.
$hits = @()
foreach ($f in $uiFiles) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName)) {
        $i++
        if ($line -match '^\s*(/\*|\*|//)') { continue }   # Kommentar, kein Stil
        if ($line -match 'radial-gradient|feTurbulence') { $hits += "$($f.Name):$i  $($line.Trim())" }
    }
}
Test-Result "Tell 6: keine Farbwolken, kein Korn" ($hits.Count -eq 0) $hits

# Tell 7: Verlaufstext in Ueberschriften.
$hits = @()
foreach ($f in $uiFiles) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName)) {
        $i++
        if ($line -match '^\s*(/\*|\*|//)') { continue }
        if ($line -match 'background-clip\s*:\s*text|-webkit-text-fill-color') { $hits += "$($f.Name):$i" }
    }
}
Test-Result "Tell 7: kein Verlaufstext" ($hits.Count -eq 0) $hits

# Tell 8: farbiger Schein statt Schatten. Ein Schatten ist die Abwesenheit von Licht -
# in einem echten Schatten liegen die drei Farbkanaele dicht beieinander. Geprueft werden
# nur AUSSEN liegende Schatten; "inset" ist eine Kante, keine Leuchtdeko.
$hits = @()
foreach ($f in $uiFiles) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName)) {
        $i++
        if ($line -notmatch 'box-shadow|--shadow') { continue }
        foreach ($m in [regex]::Matches($line, 'rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)')) {
            $r = [int]$m.Groups[1].Value; $g = [int]$m.Groups[2].Value; $b = [int]$m.Groups[3].Value
            $spread = (@($r,$g,$b) | Measure-Object -Maximum).Maximum - (@($r,$g,$b) | Measure-Object -Minimum).Minimum
            # Vor dem Treffer steht "inset"? Dann ist es eine Innenkante.
            $before = $line.Substring(0, $m.Index)
            if ($before -match 'inset[^,;]*$') { continue }
            if ($spread -gt 40) { $hits += "$($f.Name):$i  rgb($r,$g,$b) Kanalabstand $spread" }
        }
        if ($line -match 'box-shadow[^;]*0 0 \d+px var\(--(accent|green|red|yellow)') {
            $hits += "$($f.Name):$i  Schein in Akzent-/Statusfarbe"
        }
    }
}
Test-Result "Tell 8: neutrale Schatten, kein farbiger Schein" ($hits.Count -eq 0) $hits

# Tell 9: Inhalt, der erst beim Scrollen erscheint.
$hits = @()
foreach ($f in $uiFiles) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName)) {
        $i++
        if ($line -match 'IntersectionObserver|data-reveal') { $hits += "$($f.Name):$i" }
    }
}
Test-Result "Tell 9: kein Einblenden beim Scrollen" ($hits.Count -eq 0) $hits

Write-Host "`nAnti-AI-Slop, Text (Teil A)" -ForegroundColor Cyan

# Tell 1: englischer Geviertstrich im deutschen Satz.
$hits = @()
foreach ($f in $allFiles) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line.Contains([char]0x2014)) { $hits += "$($f.Name):$i  $($line.Trim())" }
    }
}
Test-Result "Tell 1: kein Geviertstrich (U+2014)" ($hits.Count -eq 0) $hits

# Tell 3: deutsches Anfuehrungspaar korrekt geschlossen.
# Falsch:  "Text"  (unten geoeffnet, gerade geschlossen)   Richtig:  "Text"
$hits = @()
$rx = [regex]("" + [char]0x201E + "[^" + [char]0x201C + [char]0x201E + "]{0,160}?" + [char]0x0022)
foreach ($f in $allFiles) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        foreach ($m in $rx.Matches($line)) { $hits += "$($f.Name):$i  $($m.Value)" }
    }
}
Test-Result "Tell 3: deutsche Anfuehrungszeichen korrekt geschlossen" ($hits.Count -eq 0) $hits

Write-Host "`nDeutsche Sprache" -ForegroundColor Cyan

# Umlaute statt ae/oe/ue in nutzersichtbaren C#-Zeichenketten.
# Der Bau nutzt /codepage:65001 - die alte ASCII-Regel ist hinfaellig. Geprueft werden nur
# Zeichenketten, die als Anzeige-Text gedacht sind (keine Befehle, keine Pfade, kein PowerShell).
$ersatz = 'Aufraeum|aufraeum|gruen|Gruen|uebersprung|Verknuepf|zuruecksetz|Zuruecksetz|waehlen|Waehlen|fuer das|moeglich|noetig|schliessen'
$hits = @()
foreach ($f in $csFiles) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*//') { continue }                         # Kommentare duerfen ASCII sein
        if ($line -match 'powershell|cmd\.exe|Args =|Join-Path|HKLM|\$env:') { continue }  # Befehle
        foreach ($m in [regex]::Matches($line, '"([^"]{4,})"')) {
            if ($m.Groups[1].Value -match $ersatz) { $hits += "$($f.Name):$i  $($m.Groups[1].Value)" }
        }
    }
}
Test-Result "Anzeige-Texte mit echten Umlauten" ($hits.Count -eq 0) $hits

# README und CHANGELOG sind kundennahe Texte - der Changelog wird woertlich zur
# Release-Beschreibung auf GitHub. Auch dort gehoeren echte Umlaute hin.
$hits = @()
foreach ($f in $docFiles) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        foreach ($w in ([regex]::Matches($line, '\b[A-Za-zÄÖÜäöüß]{4,}\b') | ForEach-Object { $_.Value })) {
            if ($w -match '(?i)(laeuft|oeffn|pruef|geraet|kuenft|ausloes|haette|oberflaech|veroeff|aufraeum|gruen|zuruecksetz|moeglich|noetig|waehl|fuer[a-z])') {
                $hits += "$($f.Name):$i  $w"
            }
        }
    }
}
Test-Result "README und CHANGELOG mit echten Umlauten" ($hits.Count -eq 0) $hits

Write-Host "`nKatalog an EINER Stelle" -ForegroundColor Cyan

# Der Katalog wird vom Host geschickt. Baut jemand wieder eine zweite Fassung in die
# Oberflaeche, laufen die Texte auseinander - genau das war vorher der Fall
# (18 von 28 Beschreibungen wichen ab, der CHKDSK-Hinweis fehlte im UI komplett).
$jsText = [IO.File]::ReadAllText((Join-Path $root 'ui\app.js'), [Text.Encoding]::UTF8)
$hits = @()
if ($jsText -match "(?m)^\s*const\s+ACTIONS\s*=") { $hits += "ui/app.js definiert wieder eine eigene ACTIONS-Liste" }
if ($jsText -match "(?m)^\s*const\s+INFO\s*=")    { $hits += "ui/app.js definiert wieder eigene INFO-Texte" }
if ($jsText -notmatch "case 'catalog'")             { $hits += "ui/app.js verarbeitet die Katalog-Nachricht des Hosts nicht mehr" }
Test-Result "Aktionstexte stehen nur im C#-Katalog" ($hits.Count -eq 0) $hits

# Gegenprobe: der Host schickt ihn auch wirklich.
$shellText = [IO.File]::ReadAllText((Join-Path $root 'host\ShellForm.cs'), [Text.Encoding]::UTF8)
$hits = @()
if ($shellText -notmatch 'type = "catalog"') { $hits += "host/ShellForm.cs sendet keinen Katalog mehr" }
Test-Result "Host sendet den Katalog an die Oberflaeche" ($hits.Count -eq 0) $hits

Write-Host "`nSicherheit" -ForegroundColor Cyan

# Das Haekchen "Sicherungspunkt vor jeder Reparatur" muss auch bei den RISKANTEN
# Aktionen greifen. Frueher zaehlte nur IsRepair - alle fuenf Danger-Aktionen liefen
# ohne Netz. Die Garantie steckt jetzt in WantsRestorePoint; hier wird geprueft,
# dass sie nicht stillschweigend wieder eingeengt wird.
$maText = [IO.File]::ReadAllText((Join-Path $root 'src\MaintenanceAction.cs'), [Text.Encoding]::UTF8)
$hits = @()
$prop = [regex]::Match($maText, 'public bool WantsRestorePoint\s*\{\s*get\s*\{\s*return([^;]+);')
if (-not $prop.Success) { $hits += "WantsRestorePoint wurde entfernt oder umgebaut" }
else {
    $expr = $prop.Groups[1].Value
    foreach ($flag in @('IsRepair','Danger','NeedsRestore')) {
        if ($expr -notmatch $flag) { $hits += "WantsRestorePoint beruecksichtigt '$flag' nicht mehr" }
    }
}
$shellText2 = [IO.File]::ReadAllText((Join-Path $root 'host\ShellForm.cs'), [Text.Encoding]::UTF8)
if ($shellText2 -match 'restore\s*&&\s*a\.IsRepair') { $hits += "host/ShellForm.cs prueft wieder nur IsRepair statt WantsRestorePoint" }
Test-Result "Riskante Aktionen bekommen einen Sicherungspunkt" ($hits.Count -eq 0) $hits

# Kein Passwort im Quelltext. Ein fest verdrahteter Vorgabewert fuer die PFX stand
# hier zusammen mit dem Hinweis, wo die Datei liegt.
$hits = @()
foreach ($f in (Get-ChildItem $root -Filter *.ps1 -Recurse |
                Where-Object { $_.FullName -notmatch '\\(bin|dist|cert)\\' })) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '(?i)\$(Cert)?Password[^=]*=\s*"[^"$]+"') {
            $hits += "$($f.Name):$i  $($line.Trim())"
        }
    }
}
Test-Result "Kein fest verdrahtetes Zertifikat-Passwort" ($hits.Count -eq 0) $hits

# Die Pruefsumme muss ueber den Dateinamen zugeordnet werden. Seit das Release zwei
# .sha256-Dateien enthaelt (ZIP und Installer), entscheidet sonst die Reihenfolge der
# API darueber, wogegen geprueft wird - heute zufaellig richtig, morgen vielleicht nicht.
# Achtung: Hier stand $shellText3 - die Variable wird aber erst weiter unten gefuellt.
# Damit lief diese Haelfte der Pruefung gegen $null und konnte nie anschlagen.
$hits = @()
if ($shellText2 -match 'EndsWith\("\.sha256"') {
    $hits += "Der Updater ordnet die Pruefsumme wieder ueber die Endung statt ueber den Dateinamen zu"
}
if ($shellText2 -notmatch '\+ "\.sha256"') {
    $hits += "Der Updater bildet den Pruefsummen-Namen nicht mehr aus dem Dateinamen"
}
Test-Result "Pruefsumme wird eindeutig zugeordnet" ($hits.Count -eq 0) $hits

# Die Herkunft wird an den installierten Herausgeber gebunden, bevor getauscht wird.
$hits = @()
if ($shellText2 -notmatch 'UpdateTrust\.PruefeHerausgeber') {
    $hits += "Das Update prueft den Herausgeber nicht mehr"
}
Test-Result "Update ist an den Herausgeber gebunden" ($hits.Count -eq 0) $hits

# Die Bindung ist nicht nur vorhanden, sie funktioniert auch: TrustProbe.cs uebersetzt
# src/UpdateTrust.cs und prueft es gegen echte signierte Dateien (die WebView2-DLLs
# aus libs/ sind von Microsoft eingebettet signiert und liegen im Repository).
$hits = @()
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    $hits += "dotnet nicht gefunden - die Signatur-Probe konnte NICHT laufen"
} else {
    $sdkRoot = Join-Path (Split-Path -Parent $dotnet.Source) 'sdk'
    $csc = Get-ChildItem (Join-Path $sdkRoot '*\Roslyn\bincore\csc.dll') -ErrorAction SilentlyContinue |
           Sort-Object { [version]($_.FullName -replace '.*\\sdk\\([0-9.]+)\\.*','$1') } | Select-Object -Last 1
    $refDir = @(
        (Join-Path $sdkRoot '..\packs\Microsoft.NETFramework.ReferenceAssemblies.net48\*\build\.NETFramework\v4.8'),
        "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
    ) | ForEach-Object { Get-Item $_ -ErrorAction SilentlyContinue } |
        Where-Object { $_ -and (Test-Path (Join-Path $_.FullName 'mscorlib.dll')) } | Select-Object -First 1

    if (-not $csc -or -not $refDir) {
        $hits += "Compiler oder Referenzassemblies nicht gefunden - Signatur-Probe uebersprungen"
    } else {
        $exe = Join-Path $env:TEMP 'WW_TrustProbe.exe'
        $refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Windows.Forms.dll') |
                ForEach-Object { "/r:$($refDir.FullName)\$_" }
        $bauArgs = @($csc.FullName,'/nologo','/nostdlib+','/target:exe','/platform:x64',
                     "/out:$exe",'/codepage:65001','/langversion:latest') + $refs +
                   @((Join-Path $root 'src\UpdateTrust.cs'), (Join-Path $root 'src\AppLog.cs'),
                     (Join-Path $root 'tests\TrustProbe.cs'))
        $bauOut = & dotnet @bauArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            $hits += "Probe liess sich nicht uebersetzen: " + (($bauOut | Select-Object -First 2) -join ' | ')
        } else {
            & $exe $root
            if ($LASTEXITCODE -ne 0) { $hits += "mindestens eine Teilpruefung der Signatur-Bindung ist rot" }
            Remove-Item $exe -ErrorAction SilentlyContinue
        }
    }
}
Test-Result "Signatur-Bindung funktioniert gegen echte Dateien" ($hits.Count -eq 0) $hits

# Die Registrierungs-Pruefung darf NUR melden, was sich nachweisen laesst. Ein Fehlalarm
# kostet hier mehr als bei jeder anderen Funktion: wer einen Deinstallations-Eintrag
# entfernt, nimmt dem Nutzer die Moeglichkeit, das Programm je wieder loszuwerden.
#
# Am 4. August 2026 auf einem echten Rechner gemessen: 3 von 9 Funden der Kategorie
# "Programme, die es nicht mehr gibt" waren falsch, weil nur der InstallLocation-Ordner
# geprueft wurde und nicht der Weg zum Deinstallieren. Die Probe faehrt den echten Lauf
# und prueft jeden Fund gegen die Festplatte gegen.
$hits = @()
if (-not $csc -or -not $refDir) {
    $hits += "Compiler oder Referenzassemblies nicht gefunden - Registrierungs-Probe uebersprungen"
} else {
    $exe = Join-Path $env:TEMP 'WW_RegistryProbe.exe'
    $refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll',
              'System.Windows.Forms.dll','System.Web.Extensions.dll') |
            ForEach-Object { "/r:$($refDir.FullName)\$_" }
    $bauArgs = @($csc.FullName,'/nologo','/nostdlib+','/target:exe','/platform:x64',
                 "/out:$exe",'/codepage:65001','/langversion:latest') + $refs +
               @((Join-Path $root 'src\RegistryScan.cs'), (Join-Path $root 'src\AppLog.cs'),
                 (Join-Path $root 'src\Shell.cs'), (Join-Path $root 'src\NativeMethods.cs'),
                 (Join-Path $root 'tests\RegistryProbe.cs'))
    $bauOut = & dotnet @bauArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $hits += "Probe liess sich nicht uebersetzen: " + (($bauOut | Select-Object -First 2) -join ' | ')
    } else {
        & $exe
        if ($LASTEXITCODE -ne 0) { $hits += "mindestens eine Teilpruefung der Registrierungs-Suche ist rot" }
        Remove-Item $exe -ErrorAction SilentlyContinue
    }
}
Test-Result "Registrierungs-Suche meldet nur Nachweisbares" ($hits.Count -eq 0) $hits

# Der Weg zum Deinstallieren MUSS mitgeprueft werden. Ohne diese Bedingung galt das
# AMD-Chipsatzpaket als "Programm, das es nicht mehr gibt", obwohl seine Deinstallation
# ueber MsiExec einwandfrei laeuft. Hier wird abgesichert, dass die Bedingung bleibt.
$regText = [IO.File]::ReadAllText((Join-Path $root 'src\RegistryScan.cs'), [Text.Encoding]::UTF8)
$hits = @()
if ($regText -notmatch 'DeinstallationTot') {
    $hits += "Die Pruefung des Deinstallations-Wegs wurde entfernt"
}
if ($regText -notmatch 'DriveType\.Fixed') {
    $hits += "Es werden wieder Wechseldatentraeger und Netzlaufwerke beurteilt"
}
if ($regText -notmatch 'DarfEntferntWerden') {
    $hits += "Das zweite Schloss vor dem Entfernen wurde entfernt"
}
Test-Result "Schutzregeln der Registrierungs-Suche sind vorhanden" ($hits.Count -eq 0) $hits

# Der Loeschweg beim Speicher darf NIE einen Pfad aus der Oberflaeche entgegennehmen.
# Die Oberflaeche schickt Kennungen, welcher Ordner dahintersteckt entscheidet der
# Quelltext. Und der Downloads-Ordner ist keine Kategorie: genau daran ist Razer Cortex
# gescheitert (Fotos und Videos geloescht, weil sie in Downloads lagen).
$storText = [IO.File]::ReadAllText((Join-Path $root 'src\StorageScan.cs'), [Text.Encoding]::UTF8)
$scanText = [IO.File]::ReadAllText((Join-Path $root 'host\ScanFlow.cs'), [Text.Encoding]::UTF8)
$hits = @()
if ($storText -notmatch 'Aufraeumen\(IEnumerable<string> schluessel') {
    $hits += "StorageScan.Aufraeumen nimmt nicht mehr nur Kennungen entgegen"
}
$aufraeumTeil = [regex]::Match($storText, '(?s)public static object Aufraeumen.*?^        \}', 'Multiline')
if ($aufraeumTeil.Success -and $aufraeumTeil.Value -match 'Downloads') {
    $hits += "Der Downloads-Ordner ist wieder eine aufraeumbare Kategorie"
}
if ($scanText -notmatch 'erlaubt\.Contains\(k\)') {
    $hits += "host/ScanFlow.cs prueft die Kennungen nicht mehr gegen das angezeigte Ergebnis"
}
Test-Result "Aufraeumen kennt nur eigene Kennungen, nie fremde Pfade" ($hits.Count -eq 0) $hits

# Die eine Frage, die beim Loeschen zaehlt: Kann LeereOrdner ueber eine Abzweigung in einen
# FREMDEN Ordner laufen? Daran sind reihenweise "PC-Reiniger" gescheitert. Die Probe baut
# den Fall im Temp-Ordner nach und sieht nach, was ueberlebt hat.
$hits = @()
if (-not $csc -or -not $refDir) {
    $hits += "Compiler oder Referenzassemblies nicht gefunden - Speicher-Probe uebersprungen"
} else {
    $exe = Join-Path $env:TEMP 'WW_StorageProbe.exe'
    $refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll',
              'System.Windows.Forms.dll','System.Web.Extensions.dll') |
            ForEach-Object { "/r:$($refDir.FullName)\$_" }
    $bauArgs = @($csc.FullName,'/nologo','/nostdlib+','/target:exe','/platform:x64',
                 "/out:$exe",'/codepage:65001','/langversion:latest') + $refs +
               @((Join-Path $root 'src\StorageScan.cs'), (Join-Path $root 'src\AppLog.cs'),
                 (Join-Path $root 'src\Shell.cs'), (Join-Path $root 'src\NativeMethods.cs'),
                 (Join-Path $root 'tests\StorageProbe.cs'))
    $bauOut = & dotnet @bauArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $hits += "Probe liess sich nicht uebersetzen: " + (($bauOut | Select-Object -First 2) -join ' | ')
    } else {
        & $exe
        if ($LASTEXITCODE -ne 0) { $hits += "mindestens eine Teilpruefung des Loeschwegs ist rot" }
        Remove-Item $exe -ErrorAction SilentlyContinue
    }
}
Test-Result "Aufraeumen laeuft nicht ueber Abzweigungen hinaus" ($hits.Count -eq 0) $hits

# Die App laeuft im Auslieferungszustand ERHOEHT. Wer von hier aus einen Explorer, einen
# Browser oder einen Editor startet, vererbt ihm die Administratorrechte - und aus einem
# erhoehten Explorer laesst sich anschliessend jede beliebige Datei erhoeht starten.
# Deshalb laufen solche Starts ueber Shell.OeffneImNutzerkontext, das die laufende
# (nicht erhoehte) Oberflaeche des Nutzers die Arbeit machen laesst.
#
# Zwei Stellen duerfen das bewusst und sind deshalb ausgenommen:
#   * Shell.cs        - der Rueckfallweg von OeffneImNutzerkontext selbst.
#   * CommandRunner.cs - Wartungswerkzeuge mit eigenem Fenster (cleanmgr, mdsched, chkdsk).
#                        Die BRAUCHEN die Rechte, sie sollen ja am System arbeiten.
$erlaubt = @('Shell.cs', 'CommandRunner.cs')
$hits = @()
foreach ($f in (Get-ChildItem (Join-Path $root 'src'),(Join-Path $root 'host') -File -Filter *.cs -Recurse)) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*(///|//)') { continue }
        # UseShellExecute = true heisst "starte es so, wie der Nutzer es anklicken wuerde" -
        # und vererbt dabei unsere Rechte. Werkzeugaufrufe der App laufen mit false.
        if ($line -match 'UseShellExecute\s*=\s*true' -and $erlaubt -notcontains $f.Name) {
            $hits += "$($f.Name):$i  $($line.Trim())"
        }
        # Explorer und Adressen NIE direkt, auch nicht aus den Ausnahmen heraus.
        if ($line -match 'Process\.Start\s*\(\s*(new ProcessStartInfo\s*\(\s*)?"(explorer|https?:)') {
            $hits += "$($f.Name):$i  $($line.Trim())"
        }
    }
}
$shellSrc = [IO.File]::ReadAllText((Join-Path $root 'src\Shell.cs'), [Text.Encoding]::UTF8)
if ($shellSrc -notmatch 'OeffneImNutzerkontext') {
    $hits += "Shell.OeffneImNutzerkontext wurde entfernt"
}
Test-Result "Nichts wird mit den Adminrechten der App geoeffnet" ($hits.Count -eq 0) $hits

Write-Host "`nDaten des Nutzers" -ForegroundColor Cyan

# Der Verlauf wurde mit File.WriteAllText geschrieben: das kuerzt die Datei zuerst auf null.
# Ein Absturz oder Stromausfall in diesem Moment hinterliess eine halbe Datei, und weil die
# als JSON unlesbar ist, war der GESAMTE Verlauf still verloren. Die Probe stellt genau das
# nach - in einem Wegwerf-Ordner, der echte Verlauf wird nicht angefasst.
$hits = @()
if (-not $csc -or -not $refDir) {
    $hits += "Compiler oder Referenzassemblies nicht gefunden - Verlaufs-Probe uebersprungen"
} else {
    $exe = Join-Path $env:TEMP 'WW_HistoryProbe.exe'
    $refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Windows.Forms.dll',
              'System.Web.Extensions.dll') | ForEach-Object { "/r:$($refDir.FullName)\$_" }
    $bauArgs = @($csc.FullName,'/nologo','/nostdlib+','/target:exe','/platform:x64',
                 "/out:$exe",'/codepage:65001','/langversion:latest') + $refs +
               @((Join-Path $root 'src\History.cs'), (Join-Path $root 'src\AppLog.cs'),
                 (Join-Path $root 'tests\HistoryProbe.cs'))
    $bauOut = & dotnet @bauArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $hits += "Probe liess sich nicht uebersetzen: " + (($bauOut | Select-Object -First 2) -join ' | ')
    } else {
        & $exe
        if ($LASTEXITCODE -ne 0) { $hits += "mindestens eine Teilpruefung des Verlaufs ist rot" }
        Remove-Item $exe -ErrorAction SilentlyContinue
    }
}
Test-Result "Verlauf ueberlebt einen Absturz beim Schreiben" ($hits.Count -eq 0) $hits

Write-Host "`nFunktionsumfang" -ForegroundColor Cyan

# Jeder Befehl, den der Host versteht, muss von der Oberflaeche auch ausloesbar sein.
#
# Anlass: Beim Oberflaechen-Umbau auf v7.0 sind acht Funktionen still verschwunden
# (Warteschlange, Herunterfahren danach, Netzwerk-Diagnose, Treiber-Backup,
# Autostart-Ordner, Selbststart-Schalter, Verlauf leeren, Browser-Rueckfallweg beim
# Update). Der Code blieb im Host stehen, nur der Weg dorthin fehlte - und genau das
# faellt beim Durchklicken der NEUEN Oberflaeche niemandem auf.
#
# Geprueft wird gegen das Vorkommen des Befehlsnamens als Zeichenkette irgendwo in
# app.js, damit auch berechnete Aufrufe zaehlen (z. B. mode === 'fix' ? 'startFix' : ...).
$shellText3 = [IO.File]::ReadAllText((Join-Path $root 'host\ShellForm.cs'), [Text.Encoding]::UTF8)
$jsText2    = [IO.File]::ReadAllText((Join-Path $root 'ui\app.js'), [Text.Encoding]::UTF8)

$befehle = [regex]::Matches($shellText3, 'type == "([a-zA-Z]+)"') |
           ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

# Diese Befehle darf die Oberflaeche bewusst nicht senden (mit Begruendung).
$ausgenommen = @{
    'ready' = 'wird gesendet, aber als erster Aufruf ohne type-Vergleich gesucht'
}

$hits = @()
foreach ($b in $befehle) {
    if ($ausgenommen.ContainsKey($b)) { continue }
    if ($jsText2 -notmatch ("'" + $b + "'")) {
        $hits += "'$b' versteht der Host, aber ui/app.js loest es nirgends aus"
    }
}
Test-Result "Alle Host-Befehle sind aus der Oberflaeche erreichbar ($($befehle.Count) Befehle)" ($hits.Count -eq 0) $hits

# Abbrechen muss BEIDE Laufarten stoppen: den Pruef-/Reparaturablauf UND eine
# Einzelaktion aus dem Werkzeugkasten (die laeuft im CommandRunner, nicht im Flow).
$hits = @()
if ($shellText3 -notmatch 'type == "cancel"') { $hits += "Der Befehl 'cancel' fehlt im Host" }
$cancelZweig = [regex]::Match($shellText3, 'type == "cancel"\)([^\r\n]*)')
if ($cancelZweig.Success) {
    $z = $cancelZweig.Groups[1].Value
    if ($z -notmatch '_runner') { $hits += "'cancel' stoppt den CommandRunner nicht" }
    if ($z -notmatch 'CancelFlow') { $hits += "'cancel' stoppt den Pruefablauf nicht" }
}
Test-Result "Abbrechen stoppt Ablauf UND Einzelaktion" ($hits.Count -eq 0) $hits

Write-Host ""
Write-Host ("Ergebnis: {0} bestanden, {1} fehlgeschlagen" -f $passed, $failed) -ForegroundColor $(if ($failed) { 'Red' } else { 'Green' })
if ($failed) { exit 1 }
exit 0
