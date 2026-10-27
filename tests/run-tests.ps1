# Pruefungen, die in der CI (Tag, Push auf main, Handstart) und vor jedem Release laufen; build.ps1 ruft sie nicht auf.
#
# Hintergrund: Regeln, die nur in einem Dokument stehen, werden ueberlesen. Genau so sind
# die Farbwolken, das Korn und die falsch geschlossenen Anfuehrungszeichen ins Projekt
# gekommen. Deshalb stehen sie hier als Test - ein Verstoss bricht den Bau.
#
# Aufruf:  .\tests\run-tests.ps1
# Ergebnis: ExitCode 0 = alles gruen, 1 = mindestens ein Test rot.
#
# Stand 19.09.2026: 70 Pruefungen (39 bis 8.0, 13 mit 8.1 in der Gruppe "v8.1: Helfer und
# Rechte"; nach dem adversarischen Gegenlesen kamen die bin-Frischepruefung und der
# Pipe-Zugriffsschutz dazu; 9 mit 8.2 in der Gruppe "v8.2: Massnahmen": Trockenlauf der
# M3-Kennungen, Ablehnungen, Sicherungsdatei-Herkunft, Rueckweg-Inhalt, kein PowerShell in
# helfer\Massnahmen, ersetzte Werkzeuge, Nachrichten der Massnahmen, sechs Freischaltbare,
# netz.dnscache.leeren live; 9 mit 8.3 in der Gruppe "v8.3: Stufe 3": Trockenlauf der
# M4-Kennungen, Ablehnungen, Bestaetigungspflicht, Export-Herkunft und Rueckweg-Inhalt M4,
# DISM nur als API, Techniker-Nachrichten, Update-Weg trocken, Zeitplan-Sperre und Zeitbudget,
# DLL-Suchpfad). Die Zahl steht in $erwartet und wird am Ende gegen die gelaufenen
# Pruefungen gehalten: eine still uebersprungene Pruefung faellt so auf.
#
# Laeuft unter Windows PowerShell 5.1 (CI: shell: powershell) UND unter pwsh 7: keine ??- und
# ?:-Operatoren, keine Cmdlets, die es nur in 7 gibt. Die Datei ist UTF-8 ohne BOM und wird von
# 5.1 als ANSI gelesen - deshalb stehen Umlaute hier nur als \u-Ersatz in Mustern, nie in
# Ausgabe-Texten.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$failed = 0
$passed = 0
$erwartet = 70

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
$csFiles  = Get-ChildItem (Join-Path $root 'src'),(Join-Path $root 'host'),(Join-Path $root 'kern'),(Join-Path $root 'sammler') -File -Filter *.cs -Recurse
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
# Ausgenommen ist das Verkettungs-Idiom  "„" + name + "“"  - dort folgt auf „ sofort das Ende
# des C#-Literals und ein Pluszeichen; ein echter Fehler haette Text zwischen „ und ".
$rx = [regex]("" + [char]0x201E + '(?!' + [char]0x0022 + '\s*\+)' + "[^" + [char]0x201C + [char]0x201E + "]{0,160}?" + [char]0x0022)
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
$ersatz = 'Aufraeum|aufraeum|gruen|Gruen|uebersprung|Verknuepf|zuruecksetz|Zuruecksetz|waehlen|Waehlen|fuer |moeglich|noetig|schliessen|Geraet|Gehaeus|ueberschritt|Datentraeger |Zaehler|gueltig|traegt|laeuft|Schluessel|verfuegbar|unvollstaendig|Laenge'
$hits = @()
foreach ($f in $csFiles) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*//') { continue }                         # Kommentare duerfen ASCII sein
        if ($line -match 'powershell|cmd\.exe|Args =|Join-Path|HKLM|\$env:|Ps\("|CmdBE\(') { continue }  # Befehle (die PowerShell-Ausgaben des Werkzeugkastens bleiben v7-Stil)
        foreach ($m in [regex]::Matches($line, '"([^"]{4,})"')) {
            $w = $m.Groups[1].Value
            # Kennungen und JSON-Feldnamen (klein beginnend, ohne Leerzeichen: "speicher.aufraeumen",
            # "kennwortNoetig") sind Schluessel, keine Anzeige-Texte - die bleiben ASCII.
            if ($w -cmatch '^[a-z][A-Za-z0-9._:-]*$') { continue }
            # Code ZWISCHEN zwei Literalen (" + x.Wert + ") ist kein Text.
            if ($w -match '^\s*[+,)]|[+(]\s*$') { continue }
            if ($w -match $ersatz) { $hits += "$($f.Name):$i  $w" }
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
        $line = $line -replace '`[^`]*`', ''                      # Code-Spans (`--pruefen`) sind Bezeichner, keine Prosa
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
        # src\AppLog.cs haengt seit 8.1 an Kern.Ablage (kern\Entscheidungen.cs); die zieht
        # kern\Json.cs und kern\Protokoll.cs nach sich, dazu System.Runtime.Serialization und
        # System.Xml. Gilt fuer jede Probe, die AppLog.cs oder History.cs mituebersetzt.
        $refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Windows.Forms.dll',
                  'System.Runtime.Serialization.dll','System.Xml.dll') |
                ForEach-Object { "/r:$($refDir.FullName)\$_" }
        $bauArgs = @($csc.FullName,'/nologo','/nostdlib+','/target:exe','/platform:x64',
                     "/out:$exe",'/codepage:65001','/langversion:latest') + $refs +
                   @((Join-Path $root 'src\UpdateTrust.cs'), (Join-Path $root 'src\AppLog.cs'),
                     (Join-Path $root 'kern\Entscheidungen.cs'), (Join-Path $root 'kern\Json.cs'),
                     (Join-Path $root 'kern\Protokoll.cs'),
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
              'System.Windows.Forms.dll','System.Web.Extensions.dll',
              'System.Runtime.Serialization.dll','System.Xml.dll') |
            ForEach-Object { "/r:$($refDir.FullName)\$_" }
    $bauArgs = @($csc.FullName,'/nologo','/nostdlib+','/target:exe','/platform:x64',
                 "/out:$exe",'/codepage:65001','/langversion:latest') + $refs +
               @((Join-Path $root 'src\RegistryScan.cs'), (Join-Path $root 'src\AppLog.cs'),
                 (Join-Path $root 'src\Shell.cs'), (Join-Path $root 'src\NativeMethods.cs'),
                 (Join-Path $root 'kern\Entscheidungen.cs'), (Join-Path $root 'kern\Json.cs'),
                 (Join-Path $root 'kern\Protokoll.cs'),
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
              'System.Windows.Forms.dll','System.Web.Extensions.dll',
              'System.Runtime.Serialization.dll','System.Xml.dll') |
            ForEach-Object { "/r:$($refDir.FullName)\$_" }
    $bauArgs = @($csc.FullName,'/nologo','/nostdlib+','/target:exe','/platform:x64',
                 "/out:$exe",'/codepage:65001','/langversion:latest') + $refs +
               @((Join-Path $root 'src\StorageScan.cs'), (Join-Path $root 'src\AppLog.cs'),
                 (Join-Path $root 'src\Shell.cs'), (Join-Path $root 'src\NativeMethods.cs'),
                 (Join-Path $root 'kern\Entscheidungen.cs'), (Join-Path $root 'kern\Json.cs'),
                 (Join-Path $root 'kern\Protokoll.cs'),
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

# Wer aus dem Programm heraus einen Explorer, einen Browser oder einen Editor startet, vererbt
# ihm seine Rechte - und aus einem erhoehten Explorer laesst sich anschliessend jede beliebige
# Datei erhoeht starten. Seit 8.1 startet die Oberflaeche zwar ohne Rechte (asInvoker), aber
# der Helfer und die geplante Wartung laufen erhoeht, und ein Host unter EnableLUA=0 ebenfalls.
# Deshalb laufen solche Starts ueber Shell.OeffneImNutzerkontext, das die laufende (nicht
# erhoehte) Oberflaeche des Nutzers die Arbeit machen laesst.
#
# UseShellExecute = true heisst "starte es so, wie der Nutzer es anklicken wuerde" - und vererbt
# dabei die eigenen Rechte. Werkzeugaufrufe laufen mit false. Fuenf Stellen duerfen das bewusst:
#   * Shell.cs          - der Rueckfallweg von OeffneImNutzerkontext selbst (Ausnahme wie bisher).
#   * HelferClient.cs   - der Start des Helfers per Verb = runas: ohne UseShellExecute kein UAC-Dialog.
#   * ShellForm.cs      - StarteErhoeht, seit 8.3 nur noch der Installer per runas (UAC-Dialog).
#   * Werkzeuge.cs      - Detached-Werkzeuge mit eigenem Fenster (cleanmgr, mdsched, chkdsk): die
#                         BRAUCHEN die Rechte, sie sollen ja am System arbeiten.
#   * Program.cs (host) - seit 8.3 der Neustart nach dem Update (NeueFassungStarten): die neue
#                         Fassung laeuft mit dem Token des NICHT erhoehten Hosts, ohne Verb runas;
#                         der erhoehte Host geht stattdessen ueber Shell.OeffneImNutzerkontext.
# Die drei 8.1-Stellen muessen den Grund in DERSELBEN Zeile als Kommentar tragen (UAC oder
# Detached), Program.cs den Neustart nach dem Update und "nicht erhoeht"; ein UseShellExecute =
# true ohne diese Begruendung ist auch dort ein Verstoss.
$erlaubtOhneGrund = @('Shell.cs')
$erlaubtMitGrund  = @('HelferClient.cs', 'ShellForm.cs', 'Werkzeuge.cs', 'Program.cs')
$hits = @()
foreach ($f in (Get-ChildItem (Join-Path $root 'src'),(Join-Path $root 'host'),(Join-Path $root 'helfer') -File -Filter *.cs -Recurse)) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*(///|//)') { continue }
        if ($line -match 'UseShellExecute\s*=\s*true') {
            if ($erlaubtOhneGrund -contains $f.Name) { }
            elseif ($f.Name -eq 'Program.cs') {
                # Nur host\Program.cs, und der Kommentar hinter dem Code muss den Neustart nach dem
                # Update und "nicht erhoeht" nennen; ein Program.cs in src\ oder helfer\ bleibt ein Verstoss.
                if ($f.DirectoryName -ne (Join-Path $root 'host')) { $hits += "$($f.Name):$i  ausserhalb von host\: $($line.Trim())" }
                elseif ($line -notmatch '//.*Neustart nach dem Update.*nicht erhoeht') { $hits += "$($f.Name):$i  ohne Begruendung (Neustart nach dem Update, nicht erhoeht im Zeilenkommentar): $($line.Trim())" }
            }
            elseif ($erlaubtMitGrund -contains $f.Name) {
                # Der Kommentar hinter dem Code muss UAC oder Detached nennen.
                if ($line -notmatch '//.*(UAC|Detached)') { $hits += "$($f.Name):$i  ohne Begruendung (UAC oder Detached im Zeilenkommentar): $($line.Trim())" }
            }
            else { $hits += "$($f.Name):$i  $($line.Trim())" }
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
              'System.Web.Extensions.dll','System.Runtime.Serialization.dll','System.Xml.dll') |
            ForEach-Object { "/r:$($refDir.FullName)\$_" }
    $bauArgs = @($csc.FullName,'/nologo','/nostdlib+','/target:exe','/platform:x64',
                 "/out:$exe",'/codepage:65001','/langversion:latest') + $refs +
               @((Join-Path $root 'src\History.cs'), (Join-Path $root 'src\AppLog.cs'),
                 (Join-Path $root 'kern\Entscheidungen.cs'), (Join-Path $root 'kern\Json.cs'),
                 (Join-Path $root 'kern\Protokoll.cs'),
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

# Die Gegenrichtung des Tests darueber - und die wichtigere. Jede Nachricht, die der Host
# an die Oberflaeche schickt, muss dort auch einen Zweig haben. Fehlt er, passiert schlicht
# nichts. Hat die Oberflaeche vorher schon auf einen Wartebildschirm umgeschaltet, haengt
# sie dort fuer immer.
#
# Real passiert am 22.08.2026: 'done' wurde nur im Werkzeugkasten-Modus ausgewertet. Die
# geplante Wartung lief in der offenen App, meldete sich nach 615 s ordnungsgemaess fertig -
# und der Ablauf-Bildschirm blieb bei 70 Prozent stehen, fuenfeinhalb Stunden lang.
$hostAll = ($csFiles | ForEach-Object { [IO.File]::ReadAllText($_.FullName, [Text.Encoding]::UTF8) }) -join "`n"
$nachrichten = [regex]::Matches($hostAll, 'type\s=\s"([a-zA-Z]+)"') |
               ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$hits = @()
foreach ($n in $nachrichten) {
    if ($jsText2 -notmatch ("case\s+'" + $n + "'")) {
        $hits += "Der Host sendet '$n' - ui\app.js hat dafuer keinen case-Zweig"
    }
}
Test-Result "Jede Host-Nachricht wird in der Oberflaeche behandelt ($($nachrichten.Count) Nachrichten)" ($hits.Count -eq 0) $hits

# Der Hauptweg darf einen Start nie stumm verwerfen. startFlow() in app.js zeigt den
# Ablauf-Bildschirm, BEVOR es startCheck/startFix sendet - ein blosses "return" im Host
# laesst den Nutzer vor einem Balken sitzen, der sich nie wieder bewegt.
$flowText = [IO.File]::ReadAllText((Join-Path $root 'host\CheckFlow.cs'), [Text.Encoding]::UTF8)
$hits = @()
if ($flowText -match 'if \(FlowRunning \|\| ScanRunning[^\r\n]*\) return;') {
    $hits += "Ein Start wird noch stumm mit 'return' verworfen"
}
foreach ($m in @('StartCheck', 'StartFix')) {
    if ($flowText -notmatch ($m + '\(\)\s*\r?\n\s*\{\s*\r?\n\s*if \(StartAbgelehnt')) {
        $hits += "$m ruft StartAbgelehnt nicht als erstes auf"
    }
}
if ($flowText -notmatch 'type = "flowBusy"') { $hits += "Der Host sendet nie 'flowBusy'" }
Test-Result "Der Hauptweg lehnt einen Start nie stumm ab" ($hits.Count -eq 0) $hits

# Abbrechen muss in JEDEM Zustand wirken. Zwei Wege, auf denen der Knopf tot war:
# 1. Es lief nichts mehr - alle drei Abbrecher stiegen still aus, keine Antwort.
# 2. Nach einem fertigen Werkzeug wurde nur die Beschriftung von "Zurueck" auf
#    "Abbrechen" zurueckgesetzt, nicht der Klick-Griff. Der Knopf blaetterte danach nur
#    noch in den Werkzeugkasten, waehrend DISM unsichtbar weiterlief.
$hits = @()
if ($shellText3 -notmatch 'if \(!lief\) FlowIdle\(\)') { $hits += "'cancel' antwortet nicht, wenn nichts mehr lief" }
if ($jsText2 -notmatch "case 'flowIdle'")             { $hits += "ui\app.js behandelt 'flowIdle' nicht" }
if (([regex]::Matches($jsText2, 'onclick = cancelClick')).Count -lt 2) {
    $hits += "Der Klick-Griff des Abbrechen-Knopfes wird nirgends zurueckgesetzt"
}
Test-Result "Abbrechen bleibt in jedem Zustand wirksam" ($hits.Count -eq 0) $hits

# Der eigentliche Fehler vom 22.08.2026 war KEIN fehlender case-Zweig - 'done' gab es.
# Er war nur an S.mode === 'action' gebunden. Startet der Host einen Lauf von sich aus
# (geplante Wartung aus dem Zeitplan), steht S.mode aber noch auf 'check' oder 'fix', und
# die Fertigmeldung lief ins Leere. Massgeblich ist der sichtbare Bildschirm.
$hits = @()
# Der Zweig ist mit den Nebenansichten (Toast, Knoepfe freigeben) auf gut 2000 Zeichen gewachsen.
$doneZweig = [regex]::Match($jsText2, "case 'done':(?s).{0,4000}?break;")
if (-not $doneZweig.Success) { $hits += "Der 'done'-Zweig ist nicht auffindbar" }
else {
    if ($doneZweig.Value -notmatch "S\.screen === 'run'")      { $hits += "'done' fragt nicht den sichtbaren Bildschirm ab" }
    if ($doneZweig.Value -match "if\(S\.mode === 'action'\)\{") { $hits += "'done' haengt wieder am Modus statt am Bildschirm" }
}
$stateZweig = [regex]::Match($jsText2, "case 'state':(?s).{0,600}?break;")
if ($stateZweig.Success -and $stateZweig.Value -notmatch "S\.screen === 'run'") {
    $hits += "'state' fragt nicht den sichtbaren Bildschirm ab"
}
Test-Result "Die Fertigmeldung raeumt den Ablauf-Bildschirm" ($hits.Count -eq 0) $hits

# Ein einzelner haengender Befehl darf den Lauf nicht fuer immer festhalten. WaitForExit()
# ohne Argument wartet zusaetzlich auf das Dateiende der Ausgabe - das kann ein
# ueberlebender Enkelprozess (DISM startet DismHost.exe) beliebig lange verhindern.
# Seit 8.1 startet nicht mehr der CommandRunner die Werkzeuge, sondern helfer\Werkzeuge.cs
# (lokal, ueber die Pipe und in --auto derselbe Code); der Wachhund lebt dort.
$werkzeugeText = [IO.File]::ReadAllText((Join-Path $root 'helfer\Werkzeuge.cs'), [Text.Encoding]::UTF8)
$hits = @()
if ($werkzeugeText -notmatch 'StandardZeitgrenzeMs')     { $hits += "helfer\Werkzeuge.cs kennt keine Standard-Zeitgrenze je Schritt" }
if ($werkzeugeText -notmatch 'System\.Threading\.Timer') { $hits += "Es gibt keinen Wachhund, der den Schritt beendet" }
if ($werkzeugeText -notmatch 'KillTree\((pid|proc\.Id)\)') { $hits += "Der Wachhund beendet den Prozessbaum nicht" }
# Der Wachhund muss innerhalb des Timers den Baum beenden, nicht nur irgendwo in der Datei.
$wachhund = [regex]::Match($werkzeugeText, '(?s)new System\.Threading\.Timer\(.{0,1500}?\}, null,')
if (-not $wachhund.Success) { $hits += "Der Wachhund-Timer ist nicht auffindbar" }
elseif ($wachhund.Value -notmatch 'KillTree\(') { $hits += "Der Wachhund-Timer ruft KillTree nicht auf" }
Test-Result "Jeder Werkzeugschritt im Helfer hat eine Zeitgrenze" ($hits.Count -eq 0) $hits

# Jeder PowerShell-Schritt des Werkzeugkastens laeuft als  powershell -Command "<text>".
# Ein doppeltes Anfuehrungszeichen IM Text beendet den Befehl, der Rest ist ein Parsefehler,
# powershell.exe endet mit 1 und der Schritt hat nichts getan. Aktion 6 (Wiederherstellen
# der ausgeblendeten Updates) hatte genau das bis 8.0.0. Deshalb: den Katalog uebersetzen,
# jeden Schritt ausgeben und mit dem PowerShell-Parser pruefen - vor dem Release, nicht
# beim Nutzer.
$hits = @()
$exe = Join-Path $env:TEMP ('WW_KatalogProbe_' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.exe')
try {
    $bauOut = & (Join-Path $root 'tools\csc.ps1') -Out $exe -Target exe `
        -Sources 'src\ActionCatalog.cs','src\MaintenanceAction.cs','tests\KatalogProbe.cs' 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { $hits += "Katalog liess sich nicht uebersetzen: " + (($bauOut | Select-Object -Last 3) -join ' | ') }
    else {
        $anzahl = [int](& $exe)
        if ($anzahl -lt 25) { $hits += "Nur $anzahl Aktionen im Katalog - erwartet werden mindestens 25" }
        $psSchritte = 0
        for ($id = 0; $id -lt $anzahl; $id++) {
            $txt = (& $exe $id) -join "`n"
            $titel = ($txt -split "`n")[0]
            $teile = $txt -split '<<STEP powershell.exe>>' | Select-Object -Skip 1
            $k = 0
            foreach ($s in $teile) {
                $k++
                $cmd = ($s -split '<<END>>')[0].Trim()
                if ($cmd -notmatch '(?s)^-NoProfile -ExecutionPolicy Bypass -Command "(.*)"$') { $hits += "$titel, PowerShell-Schritt ${k}: unerwartete Argumentform"; continue }
                $inner = $Matches[1]
                $psSchritte++
                if ($inner.Contains('"')) { $hits += "$titel, PowerShell-Schritt ${k}: doppeltes Anfuehrungszeichen im -Command-Text (beendet den Befehl)" }
                $err = $null
                [System.Management.Automation.Language.Parser]::ParseInput($inner, [ref]$null, [ref]$err) | Out-Null
                if ($err.Count -gt 0) { $hits += "$titel, PowerShell-Schritt ${k}: Parsefehler: " + $err[0].Message }
            }
        }
        if ($psSchritte -lt 10) { $hits += "Nur $psSchritte PowerShell-Schritte gefunden - die Probe hat den Katalog nicht gelesen" }
    }
}
finally { Remove-Item $exe -ErrorAction SilentlyContinue }
Test-Result "Jeder PowerShell-Schritt im Werkzeugkasten ist fehlerfrei lesbar ($psSchritte Schritte)" ($hits.Count -eq 0) $hits

Write-Host "`nv8: Kern, Sammler, Regeln" -ForegroundColor Cyan

# Schichtregel (Konzept 3.2): der Kern haengt von nichts ab. Kein WinForms, kein WMI, kein
# Prozessstart, kein WebView2. Nur so laufen die Regeln in den Proben ohne Rechte und ohne
# Fenster. Der harte Beweis ist lauf-proben.ps1 (uebersetzt NUR kern\); dieser Grep faengt
# den Verstoss frueher und benennt die Zeile.
$hits = @()
foreach ($f in (Get-ChildItem (Join-Path $root 'kern') -File -Filter *.cs -Recurse)) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*//') { continue }
        # -cmatch: "registry.autostart" ist ein Quellname in der fehlerliste, "Registry." die Win32-API.
        if ($line -cmatch 'System\.Windows\.Forms|System\.Management|System\.Diagnostics\.Process|ProcessStartInfo|new Process\b|Process\.Start|\.Start\(|StartInfo|using System\.Diagnostics;|Microsoft\.Web\.|System\.Web\.|Registry\.|EventLogReader|DllImport|GetTypeFromProgID|Win32_Process\b|ShellExecute|CreateProcess') {
            $hits += "$($f.Name):$i  $($line.Trim())"
        }
    }
}
Test-Result "Schichtregel: kern\ haengt von nichts ab" ($hits.Count -eq 0) $hits

# Der Sammler startet keinen einzigen Prozess: kein powershell.exe, kein cmd.exe. Das ist die
# Antwort auf Defender und ClickFix (Konzept 3.8) UND auf die Geschwindigkeit (v7: sieben
# PowerShell-Kaltstarts je Lauf).
$hits = @()
foreach ($f in (Get-ChildItem (Join-Path $root 'sammler') -File -Filter *.cs -Recurse)) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*//' -or $line -match '^\s*///') { continue }
        # Eine Namensliste bekannter Host-Programme (rundll32, powershell ...) ist kein Aufruf; sie
        # traegt den Vermerk "Namensliste, kein Prozessstart" auf derselben Zeile.
        if ($line -match 'Namensliste, kein Prozessstart') { continue }
        if ($line -match '(?i)powershell|cmd\.exe|Process\.Start|ProcessStartInfo|-EncodedCommand|Invoke-Expression|pwsh|WScript\.Shell|Shell\.Application|ShellExecute|CreateProcess|Win32_Process\b|new Process\b') {
            $hits += "$($f.Name):$i  $($line.Trim())"
        }
    }
}
Test-Result "Sammler startet keinen Prozess (kein PowerShell, kein cmd)" ($hits.Count -eq 0) $hits

# Kein lokalisierter Text wird gedeutet (Konzept 3.7). Die Fallen, an denen v7 haengen blieb:
# "Healthy" als Wort, "is dirty" aus fsutil, FormatDescription() (Ereignistext), findstr
# LISTENING (auf deutschem Windows: ABHOEREN).
$hits = @()
foreach ($f in (Get-ChildItem (Join-Path $root 'kern'),(Join-Path $root 'sammler') -File -Filter *.cs -Recurse)) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*//' -or $line -match '^\s*///') { continue }
        if ($line -match 'Contains\("Healthy"\)|"is dirty"|"is not dirty"|FormatDescription\(\)|findstr|"LISTENING"|Contains\("OK"\)') {
            $hits += "$($f.Name):$i  $($line.Trim())"
        }
    }
}
Test-Result "Kein lokalisierter Text wird ausgewertet" ($hits.Count -eq 0) $hits

# Die Kernproben: Regeln gegen aufgezeichnete Systembilder mit gepflanzten Fehlerfaellen
# (deaktiviertes Geraet darf kein Fehler sein, SMART-Warnung muss gemeldet werden, deutsches
# und englisches Bild liefern dasselbe). Rueckgabewert entscheidet, und es muss etwas gelaufen
# sein: eine leere Antwort ist kein bestandener Lauf.
$hits = @()
$probenOut = & (Join-Path $root 'tools\lauf-proben.ps1') 2>&1
$probenCode = $LASTEXITCODE
$probenText = ($probenOut | Out-String)
if ($probenCode -ne 0) { $hits += "Kernproben rot (ExitCode $probenCode)"; $hits += ($probenOut | Where-Object { "$_" -match 'FEHL|rot:|error' } | Select-Object -First 12 | ForEach-Object { "$_" }) }
$zahl = 0
if ($probenText -match 'Ergebnis: (\d+) bestanden') { $zahl = [int]$Matches[1] }
if ($zahl -lt 1500) { $hits += "Nur $zahl Zusicherungen gelaufen - erwartet werden mindestens 1500 (Grundmenge, Stand 12.09.2026: 2172)" }
if ($probenText -notmatch ', 10 Probenklassen') { $hits += "Nicht 10 Probenklassen gelaufen (eine Datei fehlt oder 'Laufen' ist nicht mehr public static; seit 8.3 mit SpeicherplatzProben)" }
$bilder = @(Get-ChildItem (Join-Path $root 'tests\aufzeichnungen') -Filter 'gepflanzt-*.json' -ErrorAction SilentlyContinue)
if ($bilder.Count -lt 70) { $hits += "Nur $($bilder.Count) gepflanzte Testbilder - erwartet werden mindestens 70 (Stand 12.09.2026: 80)" }
Test-Result "Kernproben gruen ($zahl Zusicherungen, $($bilder.Count) gepflanzte Bilder)" ($hits.Count -eq 0) $hits

# Die Sammlerprobe: der echte Sammler laeuft auf diesem Rechner (nicht erhoeht in der CI ist
# er erhoeht - beides ist erlaubt), schreibt ein redigiertes Systembild und prueft die
# Redaktion selbst (ExitCode 2 = Rechnername, Benutzername, MAC oder private IP noch drin).
# Grundmengen: Schema 1, Aufzeichnungszeit, mindestens ein Geraet und ein Datentraeger.
$hits = @()
$exe = Join-Path $env:TEMP ('WW_aufzeichnen_' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.exe')
$bild = Join-Path $env:TEMP ('WW_bild_' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.json')
try {
    $bauOut = & (Join-Path $root 'tools\bau-kern.ps1') -Out $exe 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { $hits += "Kommandozeile liess sich nicht bauen: " + (($bauOut | Select-Object -Last 3) -join ' | ') }
    else {
        # Das Prozessverbot am Verhalten messen, nicht nur am Wortlaut: waehrend der Sammler laeuft,
        # darf er keinen einzigen Kindprozess haben (kein powershell.exe, kein cmd.exe, nichts).
        $aufLog = Join-Path $env:TEMP ('WW_aufz_' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.txt')
        $proc = Start-Process -FilePath $exe -ArgumentList @('--aufzeichnen', ('"' + $bild + '"')) -PassThru -NoNewWindow -RedirectStandardOutput $aufLog
        $null = $proc.Handle   # ohne gebundenen Handle ist ExitCode nach dem Ende null (Start-Process-Eigenheit; in der CI so passiert)
        $kinder = @{}
        while (-not $proc.HasExited) {
            foreach ($k in (Get-CimInstance Win32_Process -Filter "ParentProcessId = $($proc.Id)" -ErrorAction SilentlyContinue)) { $kinder[$k.Name] = $true }
            Start-Sleep -Milliseconds 300
        }
        $proc.WaitForExit()
        $aufCode = $proc.ExitCode
        if ($null -eq $aufCode) { $hits += "ExitCode des Sammlers nicht lesbar (Prozess-Handle nicht gebunden)" }
        $aufOut = @(Get-Content $aufLog -ErrorAction SilentlyContinue)
        Remove-Item $aufLog -ErrorAction SilentlyContinue
        if ($kinder.Count -gt 0) { $hits += "Der Sammler hat Kindprozesse gestartet: " + (($kinder.Keys | Sort-Object) -join ', ') }
        if ($aufCode -eq 2) { $hits += "Redaktion unvollstaendig: " + (($aufOut | Where-Object { "$_" -match 'REDAKTION' }) -join ' ') }
        elseif ($null -ne $aufCode -and $aufCode -ne 0) { $hits += "Aufzeichnen schlug fehl (ExitCode $aufCode): " + (($aufOut | Select-Object -Last 3) -join ' | ') }
        elseif (-not (Test-Path $bild)) { $hits += "Aufzeichnung wurde nicht geschrieben" }
        else {
            $j = Get-Content $bild -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($j.schema -ne 1) { $hits += "Schema ist $($j.schema), erwartet 1" }
            if (-not $j.aufgezeichnet) { $hits += "Aufzeichnungszeit fehlt" }
            if (@($j.geraete).Count -lt 1) { $hits += "Keine Geraete im Systembild" }
            if (@($j.datentraeger).Count -lt 1) { $hits += "Kein Datentraeger im Systembild" }
            if (@($j.volumes).Count -lt 1) { $hits += "Kein Volume im Systembild" }
            # M4 (docs\M4-ENTWURF.md 2.3, 6, 10.2): die Quelle shell.papierkorb liefert die Zahlen ODER
            # einen Fehlereintrag, nie stille Leere; DataBasePath ist auf diesem Rechner (und dem Runner)
            # der Standardort, also hostsPfadAbweichend null (ein Wert hiesse: der Sammler liest die
            # hosts-Datei an einem anderen Ort als Windows).
            $pkBytes = $null
            if ($null -ne $j.speicher) { $pkBytes = $j.speicher.papierkorbBytes }
            $pkFehler = @($j.fehlerliste | Where-Object { $_.quelle -eq 'shell.papierkorb' })
            if ($null -eq $pkBytes -and $pkFehler.Count -lt 1) { $hits += "speicher.papierkorbBytes fehlt, und die fehlerliste nennt keine Quelle shell.papierkorb (stille Leere)" }
            elseif ($null -ne $pkBytes -and [long]$pkBytes -lt 0) { $hits += "speicher.papierkorbBytes ist negativ ($pkBytes)" }
            if ($null -ne $j.netz -and $null -ne $j.netz.hostsPfadAbweichend) { $hits += "netz.hostsPfadAbweichend ist auf diesem Rechner gesetzt ($($j.netz.hostsPfadAbweichend)); erwartet null (DataBasePath Standard)" }
            # Jede Quelle liefert Daten ODER einen Fehlereintrag (Konzept 3.3). Ohne Rechte MUSS
            # der Zaehler-Zugriff als "zugriff" eingetragen sein - nie stille Leere.
            if (-not $j.erhoeht) {
                $z = @($j.fehlerliste | Where-Object { $_.art -eq 'zugriff' })
                if ($z.Count -lt 1) { $hits += "Nicht erhoeht, aber kein einziger 'zugriff'-Eintrag in der fehlerliste" }
            }
            $pruefOut = & $exe --pruefen $bild 2>&1
            if ($LASTEXITCODE -ne 0) { $hits += "Regeln ueber die Aufzeichnung schlugen fehl: " + (($pruefOut | Select-Object -Last 3) -join ' | ') }
        }
    }
}
finally {
    Remove-Item $exe, $bild -ErrorAction SilentlyContinue
}
Test-Result "Sammler laeuft, redigiert und meldet fehlende Rechte (Papierkorb gezaehlt oder als Fehler vermerkt, hosts am Standardort)" ($hits.Count -eq 0) $hits

# Jede Frage des Systems muss in der Oberflaeche beantwortbar sein: die Karte schickt
# 'antwort' mit 'absicht' oder 'reparieren', der Host merkt es sich (Entscheidungen) und
# rechnet die Regeln neu. Fehlt ein Glied, sieht der Nutzer eine Frage ohne Knopf.
$hits = @()
$jsText3 = [IO.File]::ReadAllText((Join-Path $root 'ui\app.js'), [Text.Encoding]::UTF8)
$cfText  = [IO.File]::ReadAllText((Join-Path $root 'host\CheckFlow.cs'), [Text.Encoding]::UTF8)
if ($jsText3 -notmatch "type:'antwort'") { $hits += "app.js schickt keine 'antwort'" }
if ($jsText3 -notmatch "wert:'absicht'" -or $jsText3 -notmatch "wert:'reparieren'") { $hits += "app.js kennt nicht beide Antworten" }
if ($cfText -notmatch 'Entscheidungen\.Setze\(') { $hits += "CheckFlow merkt sich die Antwort nicht" }
if ($cfText -notmatch 'Entscheidungen\.Speichern\(') { $hits += "CheckFlow speichert die Antwort nicht" }
if ($cfText -notmatch 'Alle\.Pruefen\(_bild, Entscheidungen\)') { $hits += "CheckFlow rechnet die Regeln nach der Antwort nicht neu" }
Test-Result "Fragen sind beantwortbar und werden gemerkt" ($hits.Count -eq 0) $hits

# Keine Reparatur ohne Befund (Grundsatz 4): StartFix darf nur laufen, wenn die
# Tiefenpruefung beschaedigte Windows-Dateien gefunden hat.
$hits = @()
$fixBlock = [regex]::Match($cfText, "void StartFix\(\)(?s).{0,1800}?_flowThread\.Start\(\);")
if (-not $fixBlock.Success) { $hits += "StartFix nicht auffindbar" }
elseif ($fixBlock.Value -notmatch '_filesState == Zustand\.Bad \|\| _filesState == Zustand\.Warn') { $hits += "StartFix prueft nicht, ob ein Befund vorliegt" }
Test-Result "Keine Reparatur ohne Befund" ($hits.Count -eq 0) $hits

Write-Host "`nv8.1: Helfer und Rechte" -ForegroundColor Cyan

# Rechte-Modell B (docs\M2-ENTWURF.md, Abschnitte 0, 8, 12, 13): die Oberflaeche startet ohne
# Administratorrechte; sie holt sich einen Helfer (dieselbe EXE mit --helfer) erst, wenn eine
# Massnahme sie braucht. Der Helfer kennt nur einen festen Katalog von Kennungen, nimmt nie
# Befehlszeilen an und prueft jeden Parameter selbst. Die Pruefungen hier fahren die gebaute
# bin\WindowsWartung.exe ueber ihre Abnahmewege (--plan --trocken, --pipe, --selbstpruefung),
# in ProgramData entstehen nur Protokolldateien, und die werden hinterher entfernt.

$exeApp = Join-Path $root 'bin\WindowsWartung.exe'
$protokollOrdner = Join-Path $env:ProgramData 'WindowsWartung\protokoll'

# Plan-Datei fuer --helfer --plan: dieselbe Form, die kern\Plan.cs liest (Json.LesenDatei<Plan>).
function Schreibe-Plan([string]$planId, [object[]]$schritte) {
    $plan = [ordered]@{ id = $planId; erstellt = '2026-09-13T00:00:00Z'; titel = 'Probe ' + $planId; schritte = @($schritte) }
    $datei = Join-Path $env:TEMP ('WW_plan_' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.json')
    [IO.File]::WriteAllText($datei, ($plan | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
    return $datei
}

# Startet die EXE (winexe: kein Konsolenfenster, keine Ausgabe) und beobachtet sie bis zum Ende.
# Der Handle wird direkt nach dem Start gebunden, sonst ist ExitCode hinterher null
# (Start-Process-Eigenheit, in der CI so passiert). Kindprozesse werden wie bei der
# Sammlerprobe ueber Win32_Process gezaehlt. Rueckgabe: ExitCode, Kinder, Sekunden.
function Starte-Helfer([string[]]$argumente, [bool]$beobachten) {
    $erg = New-Object PSObject -Property @{ ExitCode = $null; Kinder = @(); Sekunden = 0 }
    $uhr = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $exeApp -ArgumentList $argumente -PassThru
    $null = $p.Handle
    $kinder = @{}
    while (-not $p.HasExited) {
        if ($beobachten) {
            foreach ($k in (Get-CimInstance Win32_Process -Filter "ParentProcessId = $($p.Id)" -ErrorAction SilentlyContinue)) { $kinder[$k.Name] = $true }
        }
        if ($uhr.ElapsedMilliseconds -gt 180000) {
            try { $p.Kill() } catch { }
            $kinder['(Zeitgrenze 180 s erreicht, Prozess beendet)'] = $true
            break
        }
        Start-Sleep -Milliseconds 150
    }
    $p.WaitForExit()
    $erg.ExitCode = $p.ExitCode
    $erg.Kinder = @($kinder.Keys | Sort-Object)
    $erg.Sekunden = [int]$uhr.Elapsed.TotalSeconds
    return $erg
}

# Zeilen eines Laufprotokolls (JSONL) als Objekte; leer, wenn die Datei fehlt.
function Lies-Laufprotokoll([string]$pfad) {
    $zeilen = @()
    if (Test-Path $pfad) {
        foreach ($z in [IO.File]::ReadAllLines($pfad, [Text.Encoding]::UTF8)) {
            if ($z.Trim().Length -eq 0) { continue }
            try { $zeilen += ($z | ConvertFrom-Json) } catch { }
        }
    }
    return ,$zeilen
}

# Warum ein Helferlauf nicht mit 0 endete, als Zeilen fuer die Details der Pruefung. Der CI-Lauf
# 35354428200 (18.09.2026) meldete nur "Exit 2 statt 0 (Grund in lauf-probe-m3a-*.jsonl und in
# logs\app.log)": beide Dateien liegen auf der Azure-VM, das Protokoll wird im finally entfernt,
# und der Runner ist danach weg. Der Grund muss deshalb in der Ausgabe stehen. Aus dem Laufprotokoll
# jede abgelehnt-Zeile (kennung, laie, fachmann.grund) und die letzten drei Zeilen mit Befund
# (art fehler, nachweis mit bestanden=false, schritt mit problem=true); aus logs\app.log die letzten
# fuenf Zeilen dieses Plans ab "helfer: Plan ... <planId> ... aus", dorthin schreibt der Helfer im
# Plan-Modus jede [bad]- und [warn]-Zeile und die Ablehnung. Fehlt die Anfangszeile (Datei
# rotiert), die letzten fuenf helfer-Zeilen ueberhaupt. app.log wird mit FileShare.ReadWrite und
# Delete geoeffnet, weil Host oder geplante Wartung sie offen halten koennen (AppLog.Anhaengen).
# Rueckgabe nie leer: auch "nichts gefunden" ist eine Zeile.
function Lies-Laufgrund([string]$protokollPfad, [string]$planId, [string]$praefix) {
    $aus = @()
    $zeilen = Lies-Laufprotokoll $protokollPfad
    $abgelehnt = @($zeilen | Where-Object { $_.art -eq 'abgelehnt' })
    foreach ($z in $abgelehnt) {
        $grund = ''
        if ($z.fachmann -and "$($z.fachmann.grund)".Length -gt 0) { $grund = ' | grund: ' + $z.fachmann.grund }
        $aus += "$praefix abgelehnt [$($z.kennung)]: $($z.laie)$grund"
    }
    $befunde = @($zeilen | Where-Object {
        ($_.art -eq 'fehler') -or
        ($_.art -eq 'nachweis' -and $_.fachmann -and $_.fachmann.bestanden -eq $false) -or
        ($_.art -eq 'schritt' -and $_.fachmann -and $_.fachmann.problem -eq $true)
    } | Select-Object -Last 3)
    foreach ($z in $befunde) {
        $mehr = ''
        if ($z.fachmann -and "$($z.fachmann.fehler)".Length -gt 0) { $mehr = ' | fehler: ' + $z.fachmann.fehler }
        $aus += "$praefix $($z.art) [$($z.kennung)]: $($z.laie)$mehr"
    }
    if ($zeilen.Count -eq 0) { $aus += "$praefix Laufprotokoll fehlt oder ist leer: $protokollPfad" }
    elseif ($abgelehnt.Count -eq 0 -and $befunde.Count -eq 0) { $aus += "$praefix Laufprotokoll ($($zeilen.Count) Zeilen) nennt weder abgelehnt noch fehler noch problem" }
    $appLog = Join-Path $env:ProgramData 'WindowsWartung\logs\app.log'
    try {
        $fs = New-Object IO.FileStream($appLog, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        try { $text = (New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)).ReadToEnd() } finally { $fs.Dispose() }
        $alle = @($text -split "`r?`n" | Where-Object { $_.Length -gt 0 })
        $start = -1
        if ($planId.Length -gt 0) {
            for ($i = $alle.Count - 1; $i -ge 0; $i--) { if ($alle[$i] -like "*helfer: Plan*$planId*aus *") { $start = $i; break } }
        }
        if ($start -ge 0) { $letzte = @($alle[$start..($alle.Count - 1)] | Select-Object -Last 5); $woher = "ab Plan $planId" }
        else { $letzte = @($alle | Where-Object { $_ -like '*helfer:*' } | Select-Object -Last 5); $woher = "Anfangszeile des Plans nicht gefunden, letzte helfer-Zeilen" }
        if ($letzte.Count -eq 0) { $aus += "$praefix logs\app.log ohne helfer-Zeile ($($alle.Count) Zeilen)" }
        else {
            $aus += "$praefix logs\app.log ($woher):"
            foreach ($l in $letzte) { $aus += "$praefix    " + $l }
        }
    } catch { $aus += "$praefix logs\app.log nicht lesbar ($appLog): $($_.Exception.Message)" }
    return ,$aus
}

# Hoechste Folgenummer der Wiederherstellungspunkte (WMI root\default, SystemRestore); nur erhoeht
# lesbar, sonst null. Ein Trockenlauf darf sie nicht veraendern (B47).
function Lies-Sicherungsnummer {
    try {
        $max = 0
        foreach ($p in @(Get-CimInstance -Namespace 'root\default' -ClassName SystemRestore -ErrorAction Stop)) {
            if ([int]$p.SequenceNumber -gt $max) { $max = [int]$p.SequenceNumber }
        }
        return $max
    } catch { return $null }
}

# 0. bin\ ist eine Kopie, und build.ps1 ruft diese Suite nicht auf. Die Proben 4, 5, 5b, 6, 6b und 8
# fahren die gebaute EXE: ist sie aelter als die juengste Quelle, laufen sie gegen einen alten
# Stand und bleiben gruen, obwohl die Aenderung nie uebersetzt wurde (der Versionsvergleich in
# Probe 6 schlaegt nur bei einem Versionssprung an). Deshalb ein eigener Fehler, kein Hinweis.
# Verglichen wird LastWriteTimeUtc ueber src, host, helfer, kern, sammler, ui (ohne shot_*.png,
# die build.ps1 nicht kopiert) und build.ps1 selbst.
$hits = @()
$quellOrdner = @('src', 'host', 'helfer', 'kern', 'sammler', 'ui') | ForEach-Object { Join-Path $root $_ }
$juengste = [DateTime]::MinValue
$juengsteDatei = ''
foreach ($f in (@(Get-ChildItem $quellOrdner -File -Recurse | Where-Object { $_.Name -notlike 'shot_*.png' }) + @(Get-Item (Join-Path $root 'build.ps1')))) {
    if ($f.LastWriteTimeUtc -gt $juengste) { $juengste = $f.LastWriteTimeUtc; $juengsteDatei = $f.FullName.Substring($root.Length + 1) }
}
if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt - vorher build.ps1 laufen lassen" }
else {
    $exeZeit = (Get-Item $exeApp).LastWriteTimeUtc
    if ($exeZeit -lt $juengste) {
        $hits += ("bin\ ist aelter als die Quellen, build.ps1 laufen lassen: EXE vom {0:yyyy-MM-dd HH:mm:ss} UTC, {1} vom {2:yyyy-MM-dd HH:mm:ss} UTC" -f $exeZeit, $juengsteDatei, $juengste)
    }
}
Test-Result "bin\WindowsWartung.exe ist juenger als jede Quelle (sonst messen die EXE-Proben einen alten Stand)" ($hits.Count -eq 0) $hits

# 1. Manifest: asInvoker, und build.ps1 bettet es IMMER ein. Bis 8.0 stand das Manifest hinter
# if ($Release): ein Dev-Bau lief dann ohne Manifest, bekam von Windows die Installer-Erkennung
# und startete anders als das Release. Die Version im Manifest muss die AssemblyVersion sein.
$manifest = [IO.File]::ReadAllText((Join-Path $root 'src\app.manifest'), [Text.Encoding]::UTF8)
$buildZeilen = [IO.File]::ReadAllLines((Join-Path $root 'build.ps1'), [Text.Encoding]::UTF8)
$asmInfo = [IO.File]::ReadAllText((Join-Path $root 'src\AssemblyInfo.cs'), [Text.Encoding]::UTF8)
$hits = @()
if ($manifest -notmatch 'level="asInvoker"') { $hits += "src\app.manifest verlangt nicht asInvoker (requireAdministrator waere wieder der UAC-Dialog beim Start)" }
$manifestZeilen = 0
for ($i = 0; $i -lt $buildZeilen.Count; $i++) {
    $z = $buildZeilen[$i]
    if ($z -match '^\s*#') { continue }
    if ($z -notmatch '/win32manifest') { continue }
    $manifestZeilen++
    $vorher = ''
    if ($i -gt 0) { $vorher = $buildZeilen[$i - 1] }
    if ($z -match 'if\s*\(\s*\$Release\s*\)' -or $vorher -match 'if\s*\(\s*\$Release\s*\)') {
        $hits += "build.ps1:$($i + 1) bettet das Manifest nur im Release ein: $($z.Trim())"
    }
}
if ($manifestZeilen -eq 0) { $hits += "build.ps1 bettet src\app.manifest nicht ein (/win32manifest fehlt)" }
$identitaet = [regex]::Match($manifest, '<assemblyIdentity\b[^>]*>')
$mv = [regex]::Match($identitaet.Value, 'version="([0-9.]+)"')
$av = [regex]::Match($asmInfo, 'AssemblyVersion\("([0-9.]+)"\)')
$assemblyVersion = '8.1.0.0'
if (-not $identitaet.Success -or -not $mv.Success -or -not $av.Success) { $hits += "Version im Manifest oder in src\AssemblyInfo.cs nicht lesbar" }
else {
    $assemblyVersion = $av.Groups[1].Value
    if ($mv.Groups[1].Value -ne $assemblyVersion) { $hits += "Manifest-Version $($mv.Groups[1].Value) weicht von AssemblyVersion $assemblyVersion ab" }
}
Test-Result "Manifest: asInvoker, immer eingebettet, Version $assemblyVersion passt zur AssemblyVersion" ($hits.Count -eq 0) $hits

# 2. Schichtregel helfer\: der Helfer laeuft erhoeht, ohne Fenster und oft ohne Sitzung. Kein
# WinForms, kein WebView2, kein CommandRunner (der gehoert zur Oberflaeche) und kein Shell
# (Shell.OeffneImNutzerkontext ist der Weg des Hosts, nicht des Helfers).
# Zeichenketten zaehlen nicht: seit 8.3 nennt helfer\Massnahmen\Programm.cs die drei DLL-Namen des
# Pakets als Literale (StammDlls, Vertrag M4 10.1), das ist eine Dateiliste, kein Verweis auf die
# Assembly. Ein using, ein Typname oder ein Aufruf steht nie in einem Literal und bleibt ein Verstoss.
$hits = @()
$literalRx = [regex]'"(?:[^"\\]|\\.)*"'
foreach ($f in (Get-ChildItem (Join-Path $root 'helfer') -File -Filter *.cs -Recurse)) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*(///|//)') { continue }
        $code = $literalRx.Replace($line, '""')
        if ($code -cmatch 'System\.Windows\.Forms|Microsoft\.Web|\bCommandRunner\b|\bShell\.') {
            $hits += "$($f.Name):$i  $($line.Trim())"
        }
    }
}
Test-Result "Schichtregel: helfer\ kennt kein WinForms, kein WebView2, keinen CommandRunner, kein Shell" ($hits.Count -eq 0) $hits

# 3. Der Host startet keine Werkzeuge mehr selbst: DISM, SFC und PowerShell kennt nur noch der
# Helfer (Katalog). Process.Start( gibt es im Host an genau drei Stellen: der runas-Start des
# Helfers (host\HelferClient.cs), der Installer per runas in ShellForm.StarteErhoeht (seit 8.3
# nur noch dafuer; der ZIP-Weg laeuft als Plan ueber den Helfer) und seit 8.3 der Neustart nach
# dem Update in host\Program.cs NeueFassungStarten (nicht erhoeht, ohne Verb runas; der erhoehte
# Host geht ueber Shell.OeffneImNutzerkontext). Shell.cs (src, Oeffnen im Nutzerkontext) ist
# nicht Teil dieser Pruefung.
$hits = @()
$hostDateien = @(Get-ChildItem (Join-Path $root 'host') -File -Filter *.cs) +
               @(Get-Item (Join-Path $root 'src\CommandRunner.cs')) +
               @(Get-Item (Join-Path $root 'src\AutoRunner.cs'))
foreach ($f in $hostDateien) {
    $zeilen = [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)
    $von = -1; $bis = -1
    if ($f.Name -eq 'ShellForm.cs') {
        # Zeilenbereich der Methode StarteErhoeht: von der Signatur bis zur schliessenden
        # Klammer auf Member-Einrueckung (8 Leerzeichen).
        for ($i = 0; $i -lt $zeilen.Count; $i++) {
            if ($von -lt 0) { if ($zeilen[$i] -match 'string StarteErhoeht\(') { $von = $i }; continue }
            if ($zeilen[$i] -match '^        \}\s*$') { $bis = $i; break }
        }
        if ($von -lt 0) { $hits += "ShellForm.cs: StarteErhoeht nicht auffindbar" }
    }
    if ($f.Name -eq 'Program.cs') {
        # Zeilenbereich der Methode NeueFassungStarten, dieselbe Klammerregel wie bei StarteErhoeht.
        for ($i = 0; $i -lt $zeilen.Count; $i++) {
            if ($von -lt 0) { if ($zeilen[$i] -match 'void NeueFassungStarten\(') { $von = $i }; continue }
            if ($zeilen[$i] -match '^        \}\s*$') { $bis = $i; break }
        }
        if ($von -lt 0) { $hits += "Program.cs: NeueFassungStarten nicht auffindbar" }
    }
    for ($i = 0; $i -lt $zeilen.Count; $i++) {
        $line = $zeilen[$i]
        if ($line -match '^\s*(///|//)') { continue }
        if ($line -match '(?i)"(dism|sfc|powershell)\.exe"') { $hits += "$($f.Name):$($i + 1)  $($line.Trim())" }
        if ($line -match 'Process\.Start\s*\(') {
            $ok = $false
            if ($f.Name -eq 'HelferClient.cs') { $ok = $true }
            elseif ($f.Name -eq 'ShellForm.cs' -and $von -ge 0 -and $i -gt $von -and ($bis -lt 0 -or $i -lt $bis)) { $ok = $true }
            # Program.cs: nur innerhalb von NeueFassungStarten, ohne Verb runas in derselben Zeile, und
            # der Zeilenkommentar nennt den Neustart nach dem Update als nicht erhoeht.
            elseif ($f.Name -eq 'Program.cs' -and $von -ge 0 -and $i -gt $von -and ($bis -lt 0 -or $i -lt $bis) -and $line -notmatch 'runas' -and $line -match '//.*Neustart nach dem Update.*nicht erhoeht') { $ok = $true }
            if (-not $ok) { $hits += "$($f.Name):$($i + 1)  $($line.Trim())" }
        }
    }
}
$hcText = [IO.File]::ReadAllText((Join-Path $root 'host\HelferClient.cs'), [Text.Encoding]::UTF8)
if ($hcText -notmatch 'Verb\s*=\s*"runas"') { $hits += "host\HelferClient.cs startet den Helfer nicht mehr per runas" }
if (([regex]::Matches($hcText, 'Process\.Start\s*\(')).Count -ne 1) { $hits += "host\HelferClient.cs hat nicht genau einen Prozessstart (den runas-Start des Helfers)" }
# Program.cs: genau EIN Prozessstart (der Neustart nach dem Update); ein zweiter waere ein neuer Weg am Helfer vorbei.
$pgText = [IO.File]::ReadAllText((Join-Path $root 'host\Program.cs'), [Text.Encoding]::UTF8)
if (([regex]::Matches($pgText, 'Process\.Start\s*\(')).Count -ne 1) { $hits += "host\Program.cs hat nicht genau einen Prozessstart (den Neustart nach dem Update in NeueFassungStarten)" }
Test-Result "Host startet keine Werkzeuge selbst (kein DISM, SFC, PowerShell; Process.Start an drei Stellen: runas Helfer, runas Installer, Neustart nach dem Update nicht erhoeht)" ($hits.Count -eq 0) $hits

# 4. Trockenlauf jeder Kennung: --helfer --plan <datei> --trocken laeuft die Pruefung jedes
# Schritts und die Lesezugriffe, startet aber keinen Prozess und schreibt nichts am System.
# Exit 0, je Kennung eine schritt-Zeile im Laufprotokoll, waehrenddessen kein Kindprozess.
# Die Liste der Kennungen wird gegen helfer\Katalog.cs gehalten: eine neue Massnahme, die hier
# fehlt, faellt sofort auf.
#
# "Schreibt nichts am System" wird nicht nur an Kindprozessen gemessen: mehrere Massnahmen greifen
# ohne Prozess ein, und ihre Trocken-Weiche ist die einzige Sperre (B47). speicher.aufraeumen mit
# schluessel=temp leert %TEMP% im eigenen Prozess (StorageScan.LeereOrdner) - deshalb liegt
# vorher eine Waechterdatei in %TEMP%, die hinterher noch da sein muss. wiederherstellungspunkt.anlegen
# ruft WMI CreateRestorePoint - die hoechste Folgenummer muss gleich bleiben (nur erhoeht lesbar,
# sonst uebersprungen). zeitplan.loeschen entfernt zeitplan.json - Existenz vorher == nachher.
$hits = @()
$kennungen = @('tiefenpruefung', 'dateien.reparieren', 'wiederherstellungspunkt.anlegen', 'wiederherstellungspunkt.zurueck',
               'werkzeug', 'wartung.auto', 'apps.entfernen', 'netz.diagnose', 'treiber.sichern', 'zeitplan.anlegen',
               'zeitplan.loeschen', 'speicher.aufraeumen', 'registrierung.entfernen', 'selbststart.loeschen')
$katalogText = [IO.File]::ReadAllText((Join-Path $root 'helfer\Katalog.cs'), [Text.Encoding]::UTF8)
$imKatalog = @([regex]::Matches($katalogText, 'Kennung\s*=\s*"([a-z.]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
foreach ($k in $imKatalog) { if ($kennungen -notcontains $k) { $hits += "Kennung '$k' steht im Katalog, aber nicht im Trockenlauf dieser Probe" } }
foreach ($k in $kennungen) { if ($imKatalog -notcontains $k) { $hits += "Kennung '$k' fehlt in helfer\Katalog.cs" } }
$planId = 'probe-trocken-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$treiberOrdner = Join-Path $env:TEMP ('WW_treiber_' + $planId)
$protokoll = Join-Path $protokollOrdner ('lauf-' + $planId + '.jsonl')
$planDatei = $null
$marker = Join-Path $env:TEMP 'ww-probe-marker.txt'
$zeitplanDatei = Join-Path $env:ProgramData 'WindowsWartung\zeitplan.json'
$sicherungText = 'Folgenummer der Wiederherstellungspunkte nicht lesbar (nicht erhoeht)'
# Seit 8.3 prueft wiederherstellungspunkt.zurueck auch im Trockenlauf, ob der Punkt mit der
# Folgenummer vorhanden ist (Vertrag M4 1: "nur die Folgenummer gegen die Liste pruefen"; fehlt er,
# Problem mit Exit 1). Deshalb die Nummer eines vorhandenen Punkts aus root\default SystemRestore;
# ist die Liste nicht lesbar oder leer (CI-Runner ohne Systemschutz), faellt der Schritt weg, und
# die Ergebniszeile sagt es.
$punktFolge81 = $null
try {
    $punkte81 = @(Get-CimInstance -Namespace 'root\default' -ClassName SystemRestore -ErrorAction Stop | Sort-Object SequenceNumber)
    if ($punkte81.Count -gt 0) { $punktFolge81 = "$([int]$punkte81[0].SequenceNumber)" }
} catch { }
$ohneSchritt = @()
$schritte = @(
    @{ kennung = 'tiefenpruefung'; parameter = @{} },
    @{ kennung = 'dateien.reparieren'; parameter = @{} },
    @{ kennung = 'wiederherstellungspunkt.anlegen'; parameter = @{ beschreibung = 'Probe Trockenlauf' } })
if ($null -ne $punktFolge81) { $schritte += @{ kennung = 'wiederherstellungspunkt.zurueck'; parameter = @{ folge = $punktFolge81 } } }
else { $ohneSchritt += 'wiederherstellungspunkt.zurueck (kein Wiederherstellungspunkt lesbar, der Schritt braucht eine vorhandene Folgenummer)' }
$schritte += @(
    @{ kennung = 'werkzeug'; parameter = @{ id = '0'; sicherung = '0' } },
    @{ kennung = 'wartung.auto'; parameter = @{ schluessel = '' } },
    # Ein PackageFullName, den AppxCleaner.IsRemovable annimmt: Name aus dem Katalog, gueltige Zeichen.
    @{ kennung = 'apps.entfernen'; parameter = @{ pakete = 'Microsoft.BingWeather_4.53.52792.0_x64__8wekyb3d8bbwe' } },
    @{ kennung = 'netz.diagnose'; parameter = @{ ziel = 'localhost' } },
    @{ kennung = 'treiber.sichern'; parameter = @{ ordner = $treiberOrdner } },
    @{ kennung = 'zeitplan.anlegen'; parameter = @{ modus = 'daily'; stunde = '12'; minute = '0' } },
    @{ kennung = 'zeitplan.loeschen'; parameter = @{} },
    @{ kennung = 'speicher.aufraeumen'; parameter = @{ schluessel = 'temp' } },
    @{ kennung = 'registrierung.entfernen'; parameter = @{ kennungen = '0123456789ab' } },
    @{ kennung = 'selbststart.loeschen'; parameter = @{} }
)
try {
    if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt - vorher build.ps1 laufen lassen" }
    else {
        [IO.File]::WriteAllText($marker, "Waechterdatei der Trockenlauf-Probe: muss den Lauf ueberleben.`n", (New-Object Text.UTF8Encoding $false))
        $seqVorher = Lies-Sicherungsnummer
        $zeitplanVorher = Test-Path $zeitplanDatei
        $planDatei = Schreibe-Plan $planId $schritte
        $lauf = Starte-Helfer @('--helfer', '--plan', ('"' + $planDatei + '"'), '--trocken') $true
        if ($null -eq $lauf.ExitCode) { $hits += "ExitCode des Helfers nicht lesbar (Prozess-Handle nicht gebunden)" }
        elseif ($lauf.ExitCode -ne 0) {
            # Der Grund muss in der Ausgabe stehen: Protokoll und app.log liegen auf dem Runner und sind danach weg.
            $hits += "Trockenlauf endete mit Exit $($lauf.ExitCode) statt 0 (Plan $planId; Grund aus $protokoll und logs\app.log):"
            $hits += Lies-Laufgrund $protokoll $planId 'Trockenlauf:'
        }
        if ($lauf.Kinder.Count -gt 0) { $hits += "Der Trockenlauf hat Kindprozesse gestartet: " + ($lauf.Kinder -join ', ') }
        if (-not (Test-Path $marker)) { $hits += "Der Trockenlauf hat die Waechterdatei in %TEMP% geloescht: $marker (speicher.aufraeumen hat den Temp-Ordner trotz --trocken geleert)" }
        $seqNachher = Lies-Sicherungsnummer
        if ($null -ne $seqVorher) {
            $sicherungText = "Folgenummer der Wiederherstellungspunkte $seqVorher gehalten"
            if ($seqNachher -ne $seqVorher) { $hits += "Die hoechste Folgenummer der Wiederherstellungspunkte hat sich geaendert: $seqVorher -> $seqNachher (der Trockenlauf hat einen Punkt angelegt)" }
        }
        if ((Test-Path $zeitplanDatei) -ne $zeitplanVorher) { $hits += "zeitplan.json vorher=$zeitplanVorher, nachher=$(Test-Path $zeitplanDatei): der Trockenlauf hat den Zeitplan angefasst" }
        if (-not (Test-Path $protokoll)) { $hits += "Laufprotokoll fehlt: $protokoll" }
        else {
            $zeilen = Lies-Laufprotokoll $protokoll
            foreach ($s in $schritte) {
                $k = $s.kennung
                $da = @($zeilen | Where-Object { $_.art -eq 'schritt' -and $_.kennung -eq $k })
                if ($da.Count -lt 1) { $hits += "Keine schritt-Zeile fuer '$k' im Laufprotokoll" }
            }
            $ende = @($zeilen | Where-Object { $_.art -eq 'ende' -and $_.kennung -eq 'plan' })
            if ($ende.Count -ne 1) { $hits += "Keine ende-Zeile des Plans im Laufprotokoll ($($ende.Count))" }
            elseif (-not $ende[0].fachmann.trocken) { $hits += "Die ende-Zeile traegt nicht trocken=true" }
            elseif ($ende[0].fachmann.exit -ne 0) { $hits += "Die ende-Zeile meldet exit $($ende[0].fachmann.exit)" }
        }
        if (Test-Path $treiberOrdner) { $hits += "Der Trockenlauf hat den Treiber-Ordner angelegt: $treiberOrdner" }
    }
}
finally {
    if ($planDatei) { Remove-Item $planDatei -ErrorAction SilentlyContinue }
    Remove-Item $protokoll, $marker -ErrorAction SilentlyContinue
    Remove-Item $treiberOrdner -Recurse -Force -ErrorAction SilentlyContinue
}
$ohneSchrittText = ''
if ($ohneSchritt.Count -gt 0) { $ohneSchrittText = '; weggelassen: ' + ($ohneSchritt -join '; ') }
Test-Result "Trockenlauf jeder Kennung: Exit 0, je Kennung eine schritt-Zeile, kein Kindprozess, Waechterdatei bleibt ($($schritte.Count) von $($kennungen.Count) Kennungen; $sicherungText$ohneSchrittText)" ($hits.Count -eq 0) $hits

# 5. Ablehnung: alles wird VOR dem ersten Eingriff geprueft. Eine unbekannte Kennung, ein
# Werkzeug, das es nicht gibt, ein Ziel mit Leerzeichen (Einschleusung in ping/tracert) und ein
# leerer Plan enden mit Exit 2 und einer abgelehnt-Zeile; keine anfang-Zeile, kein Prozess.
# Ohne --trocken: die Ablehnung selbst ist das Netz, das hier gemessen wird.
#
# Je Parameterpruefung des Katalogs ein Ablehnungsfall (B48): Probe 4 faehrt nur gueltige Werte,
# und eine Pruefung, die still durchlaesst (IsCritical-Sperre weg, windir-Block weg), bliebe
# sonst unsichtbar - der erhoehte Helfer wuerde dann ShellExperienceHost entfernen oder pnputil
# nach System32 schreiben. Das kritische Paket wird gegen die Critical-Liste in src\AppxCleaner.cs
# gehalten, damit die Probe nicht die falsche Sperre misst.
$hits = @()
$kritischesPaket = 'Microsoft.Windows.ShellExperienceHost_10.0.26100.1_neutral_neutral_cw5n1h2txyewy'
$appxText = [IO.File]::ReadAllText((Join-Path $root 'src\AppxCleaner.cs'), [Text.Encoding]::UTF8)
$kritischListe = [regex]::Match($appxText, '(?s)Critical\s*=\s*new string\[\]\s*\{(.*?)\};')
$paketName = $kritischesPaket.Split('_')[0].ToLowerInvariant()
$imSchutz = $false
if ($kritischListe.Success) {
    foreach ($m in [regex]::Matches($kritischListe.Groups[1].Value, '"([^"]+)"')) { if ($paketName.Contains($m.Groups[1].Value)) { $imSchutz = $true } }
}
if (-not $imSchutz) { $hits += "src\AppxCleaner.cs: die Critical-Liste deckt '$paketName' nicht ab - der Fall 'kritisches Paket' misst dann nicht die IsCritical-Sperre" }
$faelle = @(
    @{ name = 'Kennung format.c'; schritte = @(@{ kennung = 'format.c'; parameter = @{} }) },
    @{ name = 'werkzeug id 999'; schritte = @(@{ kennung = 'werkzeug'; parameter = @{ id = '999' } }) },
    @{ name = 'werkzeug sicherung "ja"'; schritte = @(@{ kennung = 'werkzeug'; parameter = @{ id = '0'; sicherung = 'ja' } }) },
    @{ name = 'netz.diagnose ziel "a b"'; schritte = @(@{ kennung = 'netz.diagnose'; parameter = @{ ziel = 'a b' } }) },
    @{ name = 'wiederherstellungspunkt.anlegen beschreibung mit Sonderzeichen'; schritte = @(@{ kennung = 'wiederherstellungspunkt.anlegen'; parameter = @{ beschreibung = 'Probe "x"; & y' } }) },
    @{ name = 'wiederherstellungspunkt.zurueck folge "0"'; schritte = @(@{ kennung = 'wiederherstellungspunkt.zurueck'; parameter = @{ folge = '0' } }) },
    @{ name = 'wartung.auto schluessel "format"'; schritte = @(@{ kennung = 'wartung.auto'; parameter = @{ schluessel = 'format' } }) },
    @{ name = 'apps.entfernen kritisches Paket'; schritte = @(@{ kennung = 'apps.entfernen'; parameter = @{ pakete = $kritischesPaket } }) },
    @{ name = 'treiber.sichern ordner unter %WINDIR%'; schritte = @(@{ kennung = 'treiber.sichern'; parameter = @{ ordner = (Join-Path $env:WINDIR 'System32') } }) },
    @{ name = 'zeitplan.anlegen modus "x"'; schritte = @(@{ kennung = 'zeitplan.anlegen'; parameter = @{ modus = 'x'; stunde = '12'; minute = '0' } }) },
    @{ name = 'speicher.aufraeumen schluessel "downloads"'; schritte = @(@{ kennung = 'speicher.aufraeumen'; parameter = @{ schluessel = 'downloads' } }) },
    @{ name = 'registrierung.entfernen kennungen "ab;c/d"'; schritte = @(@{ kennung = 'registrierung.entfernen'; parameter = @{ kennungen = 'ab;c/d' } }) },
    @{ name = 'leerer Plan'; schritte = @() }
)
foreach ($fall in $faelle) {
    $planId = 'probe-ablehnung-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $protokoll = Join-Path $protokollOrdner ('lauf-' + $planId + '.jsonl')
    $planDatei = $null
    try {
        if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt"; break }
        $planDatei = Schreibe-Plan $planId $fall.schritte
        $lauf = Starte-Helfer @('--helfer', '--plan', ('"' + $planDatei + '"')) $true
        if ($lauf.ExitCode -ne 2) { $hits += "$($fall.name): Exit $($lauf.ExitCode) statt 2" }
        if ($lauf.Kinder.Count -gt 0) { $hits += "$($fall.name): Kindprozesse " + ($lauf.Kinder -join ', ') }
        if (-not (Test-Path $protokoll)) { $hits += "$($fall.name): kein Laufprotokoll $protokoll" }
        else {
            $zeilen = Lies-Laufprotokoll $protokoll
            if (@($zeilen | Where-Object { $_.art -eq 'abgelehnt' }).Count -lt 1) { $hits += "$($fall.name): keine Protokollzeile art=abgelehnt" }
            if (@($zeilen | Where-Object { $_.art -eq 'anfang' }).Count -gt 0) { $hits += "$($fall.name): trotz Ablehnung ist etwas angelaufen (anfang-Zeile)" }
        }
    }
    finally {
        if ($planDatei) { Remove-Item $planDatei -ErrorAction SilentlyContinue }
        Remove-Item $protokoll -ErrorAction SilentlyContinue
    }
}
Test-Result "Helfer lehnt unbekannte Kennungen, ungueltige Parameter und leere Plaene ab (Exit 2, nichts laeuft; $($faelle.Count) Faelle)" ($hits.Count -eq 0) $hits

# Die Plan-Id wird Dateiname (lauf-<id>.jsonl). Aus einer Plandatei ersetzt --plan eine Id mit
# Pfadzeichen (die Pipe lehnt sie ab): der Lauf endet mit 0, die Datei liegt unter protokoll\
# mit einer neuen Id, und nirgends entsteht eine Datei "evil".
$hits = @()
$wwOrdner = Join-Path $env:ProgramData 'WindowsWartung'
$planDatei = $null
$neueDateien = @()
try {
    if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt" }
    else {
        $vorher = @{}
        foreach ($f in @(Get-ChildItem $wwOrdner -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\logs\\' })) { $vorher[$f.FullName] = $true }
        $planDatei = Schreibe-Plan 'x\..\evil' @(@{ kennung = 'netz.diagnose'; parameter = @{ ziel = 'localhost' } })
        $lauf = Starte-Helfer @('--helfer', '--plan', ('"' + $planDatei + '"'), '--trocken') $true
        if ($lauf.ExitCode -ne 0) { $hits += "Exit $($lauf.ExitCode) statt 0" }
        $neueDateien = @(Get-ChildItem $wwOrdner -File -Recurse -ErrorAction SilentlyContinue |
                         Where-Object { $_.FullName -notmatch '\\logs\\' -and -not $vorher.ContainsKey($_.FullName) })
        if ($neueDateien.Count -ne 1) { $hits += "$($neueDateien.Count) neue Dateien statt genau einer: " + (($neueDateien | ForEach-Object { $_.FullName }) -join ', ') }
        foreach ($f in $neueDateien) {
            if ($f.DirectoryName.TrimEnd('\') -ne $protokollOrdner.TrimEnd('\')) { $hits += "Datei ausserhalb von protokoll\: $($f.FullName)" }
            if ($f.Name -notmatch '^lauf-[A-Za-z0-9._-]{1,64}\.jsonl$') { $hits += "Dateiname nicht nach dem Muster lauf-<Id>.jsonl: $($f.Name)" }
            if ($f.Name -match 'evil') { $hits += "Die unzulaessige Id wurde uebernommen: $($f.Name)" }
        }
        if (Test-Path (Join-Path $protokollOrdner 'evil.jsonl')) { $hits += "protokoll\evil.jsonl wurde angelegt" }
        if (Test-Path (Join-Path $wwOrdner 'evil.jsonl')) { $hits += "WindowsWartung\evil.jsonl wurde angelegt" }
    }
}
finally {
    if ($planDatei) { Remove-Item $planDatei -ErrorAction SilentlyContinue }
    foreach ($f in $neueDateien) { Remove-Item $f.FullName -ErrorAction SilentlyContinue }
    Remove-Item (Join-Path $protokollOrdner 'evil.jsonl'), (Join-Path $wwOrdner 'evil.jsonl') -ErrorAction SilentlyContinue
}
Test-Result "Eine Plan-Id mit Pfadzeichen wird ersetzt, nichts landet ausserhalb von protokoll\" ($hits.Count -eq 0) $hits

# 6. Die Pipe: derselbe Weg, den der Host nach dem UAC-Klick geht, hier ohne Erhoehung erlaubt.
# Der Helfer wird mit --pipe <name> --sid <eigene SID> gestartet, der Client verbindet sich wie
# host\Ausfuehrer.cs (Identification-Token, eine JSON-Zeile je Nachricht). ping liefert die
# Version, Unbekanntes und Auftraege ohne id werden abgelehnt, ende beendet den Prozess binnen
# 5 s. Erhoeht (CI-Runner) laeuft zusaetzlich messen: das Systembild muss erhoeht=true tragen.
$hits = @()
$erhoeht = $false
try { $erhoeht = (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) } catch { }
$pipeName = 'ww-test-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$pipeSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$pipeUtf8 = New-Object Text.UTF8Encoding $false
$helferProc = $null; $pipe = $null; $pipeLeser = $null
function Pipe-Sende([string]$json) {
    $b = $pipeUtf8.GetBytes($json + "`n")
    $pipe.Write($b, 0, $b.Length)
    $pipe.Flush()
}
# Eine Zeile lesen, hoechstens ms warten (ein stummer Helfer darf die Suite nicht festhalten).
function Pipe-Lese([int]$ms) {
    $task = $pipeLeser.ReadLineAsync()
    if ($task.Wait($ms)) { return $task.Result }
    return $null
}
function Pipe-Antwort([string]$roh) {
    if ($null -eq $roh) { return $null }
    try { return ($roh | ConvertFrom-Json) } catch { return $null }
}
$messenText = 'messen-Probe uebersprungen (nicht erhoeht)'
try {
    if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt" }
    else {
        $helferProc = Start-Process -FilePath $exeApp -ArgumentList @('--helfer', '--pipe', $pipeName, '--sid', $pipeSid) -PassThru
        $null = $helferProc.Handle
        # Auf die Pipe warten, statt Connect im Kreis drehen zu lassen (WaitNamedPipe kehrt ohne Pipe sofort zurueck).
        $uhr = [Diagnostics.Stopwatch]::StartNew()
        $da = $false
        while (-not $da -and $uhr.ElapsedMilliseconds -lt 15000 -and -not $helferProc.HasExited) {
            try { $da = @([IO.Directory]::GetFiles('\\.\pipe\')) -contains ('\\.\pipe\' + $pipeName) } catch { $da = $false }
            if (-not $da) { Start-Sleep -Milliseconds 100 }
        }
        if ($helferProc.HasExited) {
            # Im Pipe-Modus gibt es kein Laufprotokoll (die Zeile "Laufprotokoll fehlt" ist erwartet); der Grund
            # steht in den letzten helfer-Zeilen aus logs\app.log ("helfer: Start abgelehnt, ..."), die Lies-Laufgrund ausgibt.
            $hits += "Der Helfer endete sofort mit Exit $($helferProc.ExitCode) (Grund aus logs\app.log, Zeile 'helfer: Start abgelehnt'):"
            $hits += Lies-Laufgrund (Join-Path $protokollOrdner 'lauf-(pipe-ohne-plan).jsonl') '' 'Pipe:'
        }
        else {
            $pipe = New-Object IO.Pipes.NamedPipeClientStream('.', $pipeName, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::None, [Security.Principal.TokenImpersonationLevel]::Identification)
            $pipe.Connect(15000)
            $pipeLeser = New-Object IO.StreamReader($pipe, $pipeUtf8, $false, 65536, $true)

            Pipe-Sende '{"id":"a1","typ":"ping","nutzlast":{}}'
            $a = Pipe-Antwort (Pipe-Lese 20000)
            if ($null -eq $a) { $hits += "ping: keine Antwort binnen 20 s" }
            else {
                if ($a.typ -ne 'ergebnis' -or $a.antwortAuf -ne 'a1') { $hits += "ping: Antwort typ=$($a.typ) antwortAuf=$($a.antwortAuf) statt ergebnis/a1" }
                if ($a.nutzlast.version -ne $assemblyVersion) { $hits += "ping: version=$($a.nutzlast.version) statt $assemblyVersion (bin\ veraltet?)" }
                if ($erhoeht -and $a.nutzlast.erhoeht -ne '1') { $hits += "ping: erhoeht=$($a.nutzlast.erhoeht), obwohl die Shell erhoeht ist" }
            }

            Pipe-Sende '{"typ":"unsinn","id":"a2"}'
            $a = Pipe-Antwort (Pipe-Lese 20000)
            if ($null -eq $a) { $hits += "unsinn: keine Antwort binnen 20 s" }
            elseif ($a.typ -ne 'abgelehnt' -or $a.antwortAuf -ne 'a2') { $hits += "unsinn: typ=$($a.typ) antwortAuf=$($a.antwortAuf) statt abgelehnt/a2" }

            Pipe-Sende '{"typ":"ping"}'
            $a = Pipe-Antwort (Pipe-Lese 20000)
            if ($null -eq $a) { $hits += "ohne id: keine Antwort binnen 20 s" }
            elseif ($a.typ -ne 'abgelehnt') { $hits += "ohne id: typ=$($a.typ) statt abgelehnt" }
            elseif ("$($a.nutzlast.grund)" -notmatch 'id') { $hits += "ohne id: der Grund nennt die fehlende id nicht: $($a.nutzlast.grund)" }

            if ($erhoeht) {
                $messenText = 'messen erhoeht: Systembild mit erhoeht=true'
                Pipe-Sende '{"id":"a3","typ":"messen","nutzlast":{}}'
                $fortschritte = 0
                $messUhr = [Diagnostics.Stopwatch]::StartNew()
                do {
                    $roh = Pipe-Lese 90000
                    $a = Pipe-Antwort $roh
                    if ($null -eq $a) { break }
                    if ($a.typ -eq 'fortschritt') { $fortschritte++ }
                } while ($a.typ -eq 'fortschritt' -and $messUhr.ElapsedMilliseconds -lt 180000)
                if ($null -eq $a) { $hits += "messen: keine Abschlussantwort ($fortschritte Fortschrittsmeldungen, $([int]$messUhr.Elapsed.TotalSeconds) s)" }
                elseif ($a.typ -ne 'ergebnis' -or $a.antwortAuf -ne 'a3') { $hits += "messen: typ=$($a.typ) antwortAuf=$($a.antwortAuf) statt ergebnis/a3" }
                else {
                    $bildJson = "$($a.nutzlast.systembildJson)"
                    if ($bildJson.Length -lt 1000) { $hits += "messen: systembildJson fehlt oder ist zu kurz ($($bildJson.Length) Zeichen)" }
                    elseif ($bildJson -notmatch '"erhoeht"\s*:\s*true') { $hits += "messen: das Systembild traegt nicht erhoeht=true" }
                }
            }

            Pipe-Sende '{"id":"a9","typ":"ende","nutzlast":{}}'
            $a = Pipe-Antwort (Pipe-Lese 20000)
            if ($null -eq $a) { $hits += "ende: keine Antwort binnen 20 s" }
            elseif ($a.typ -ne 'ergebnis' -or $a.antwortAuf -ne 'a9') { $hits += "ende: typ=$($a.typ) antwortAuf=$($a.antwortAuf) statt ergebnis/a9" }
            if (-not $helferProc.WaitForExit(5000)) { $hits += "Der Helfer lief 5 s nach ende noch" }
            elseif ($helferProc.ExitCode -ne 0) { $hits += "Der Helfer endete mit Exit $($helferProc.ExitCode) statt 0" }
        }
    }
}
catch { $hits += "Pipe-Probe: " + $_.Exception.Message }
finally {
    if ($pipeLeser) { try { $pipeLeser.Dispose() } catch { } }
    if ($pipe) { try { $pipe.Dispose() } catch { } }
    if ($helferProc -and -not $helferProc.HasExited) {
        try { $helferProc.Kill() } catch { }
        $hits += "Der Helfer musste beendet werden"
    }
}
Test-Result "Pipe: ping, Ablehnung, ende ($messenText)" ($hits.Count -eq 0) $hits

# 6b. Zugriffsschutz der Pipe am Verhalten gemessen, nicht am PipeSecurity-Objekt (das prueft die
# Helferprobe, B50). Zwei Laeufe:
#   (a) --sid S-1-1-0 ("Jeder"): SidGueltig lehnt Gruppen und Systemkonten beim Start ab - Exit 7
#       binnen 10 s, und unter \\.\pipe\ entsteht keine Pipe dieses Namens.
#   (b) --sid mit der SID eines fremden Kontos (wohlgeformt, gibt es nicht): die Pipe traegt eine
#       Beschreibung, die nur dieses Konto und SYSTEM zulaesst. Der Verbindungsversuch mit dem
#       eigenen Konto scheitert an Windows (UnauthorizedAccessException), bevor der Helfer eine
#       Zeile liest. Kommt die Verbindung trotzdem zustande, wendet Pipe.Server die Beschreibung
#       nicht an - ausser die Suite laeuft als SYSTEM (S-1-5-18, in der Beschreibung enthalten):
#       dann greift die zweite Schicht, die erste Anfrage wird abgelehnt (oder die Verbindung
#       endet) und der Helfer endet mit Exit 4. Ohne Verbindung wartete der Helfer 10 Minuten,
#       deshalb wird er hinterher beendet; das ist hier kein Fehler.
$hits = @()
$fremdeSid = 'S-1-5-21-1111111111-2222222222-3333333333-1001'
$binSystem = ($pipeSid -eq 'S-1-5-18')
$helferProc = $null; $pipe = $null; $pipeLeser = $null
$schicht = 'keine Verbindung versucht'
try {
    if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt" }
    else {
        # (a) Jeder
        $pipeNameA = 'ww-test-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
        $helferProc = Start-Process -FilePath $exeApp -ArgumentList @('--helfer', '--pipe', $pipeNameA, '--sid', 'S-1-1-0') -PassThru
        $null = $helferProc.Handle
        $uhr = [Diagnostics.Stopwatch]::StartNew()
        $pipeGesehen = $false
        while (-not $helferProc.HasExited -and $uhr.ElapsedMilliseconds -lt 10000) {
            try { if (@([IO.Directory]::GetFiles('\\.\pipe\')) -contains ('\\.\pipe\' + $pipeNameA)) { $pipeGesehen = $true } } catch { }
            Start-Sleep -Milliseconds 100
        }
        if (-not $helferProc.HasExited) {
            try { $helferProc.Kill() } catch { }
            try { $helferProc.WaitForExit(5000) | Out-Null } catch { }
            $hits += "--sid S-1-1-0: der Helfer lief nach 10 s noch (musste beendet werden) statt mit Exit 7 zu enden"
        }
        else {
            $helferProc.WaitForExit()
            if ($helferProc.ExitCode -ne 7) { $hits += "--sid S-1-1-0: Exit $($helferProc.ExitCode) statt 7 (SidGueltig laesst 'Jeder' durch?)" }
        }
        if ($pipeGesehen) { $hits += "--sid S-1-1-0: unter \\.\pipe\ ist trotzdem eine Pipe '$pipeNameA' entstanden" }
        try { if (@([IO.Directory]::GetFiles('\\.\pipe\')) -contains ('\\.\pipe\' + $pipeNameA)) { $hits += "--sid S-1-1-0: die Pipe '$pipeNameA' besteht nach dem Ende noch" } } catch { }
        $helferProc = $null

        # (b) fremdes Konto. Eine Pipe, auf die das eigene Konto keinen Zugriff hat, taucht in der
        # Auflistung von \\.\pipe\ nicht auf (am 13.09.2026 gemessen: 15 s lang "nicht da", Connect
        # aber sofort verweigert). Deshalb wartet hier Connect selbst: bis 15 s, solange die Pipe
        # fehlt (TimeoutException), sofortige Ablehnung, sobald sie da ist.
        $pipeNameB = 'ww-test-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
        $helferProc = Start-Process -FilePath $exeApp -ArgumentList @('--helfer', '--pipe', $pipeNameB, '--sid', $fremdeSid) -PassThru
        $null = $helferProc.Handle
        $pipe = New-Object IO.Pipes.NamedPipeClientStream('.', $pipeNameB, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::None, [Security.Principal.TokenImpersonationLevel]::Identification)
        $verbunden = $false
        try { $pipe.Connect(15000); $verbunden = $true }
        catch {
            # PowerShell verpackt die .NET-Ausnahme eines Methodenaufrufs (5.1 und 7 gleich).
            $ex = $_.Exception
            if ($ex -is [Management.Automation.MethodInvocationException] -and $ex.InnerException) { $ex = $ex.InnerException }
            if ($ex -is [UnauthorizedAccessException]) { $schicht = 'Windows verweigert die Verbindung des eigenen Kontos (Beschreibung der Pipe wirkt)' }
            elseif ($helferProc.HasExited) {
                # Kein Laufprotokoll im Pipe-Modus (siehe Probe 6); der Grund kommt aus den letzten helfer-Zeilen in logs\app.log.
                $hits += "fremde SID: der Helfer endete mit Exit $($helferProc.ExitCode), bevor eine Verbindung moeglich war (SidGueltig lehnt eine wohlgeformte Konto-SID ab? Grund aus logs\app.log):"
                $hits += Lies-Laufgrund (Join-Path $protokollOrdner 'lauf-(pipe-ohne-plan).jsonl') '' 'fremde SID:'
            }
            else { $hits += "fremde SID: Connect scheiterte mit $($ex.GetType().Name) statt UnauthorizedAccessException: $($ex.Message)" }
        }
        if ($verbunden) {
            if (-not $binSystem) { $hits += "fremde SID: die Verbindung mit dem eigenen Konto kam zustande - die Pipe traegt die Sicherheitsbeschreibung nicht (Pipe.Server ohne PipeSicherheit?)" }
            $pipeLeser = New-Object IO.StreamReader($pipe, $pipeUtf8, $false, 65536, $true)
            Pipe-Sende '{"id":"b1","typ":"ping","nutzlast":{}}'
            $a = Pipe-Antwort (Pipe-Lese 10000)
            if ($null -ne $a -and $a.typ -ne 'abgelehnt') { $hits += "fremde SID: die erste Anfrage wurde mit typ=$($a.typ) beantwortet statt abgelehnt" }
            if (-not $helferProc.WaitForExit(10000)) { $hits += "fremde SID: der Helfer lief 10 s nach der Verbindung noch statt mit Exit 4 zu enden" }
            elseif ($helferProc.ExitCode -ne 4) { $hits += "fremde SID: Exit $($helferProc.ExitCode) statt 4" }
            else { $schicht = 'zweite Schicht: Aufrufer-SID geprueft, erste Anfrage abgelehnt, Exit 4' }
        }
    }
}
catch { $hits += "Zugriffsschutz-Probe: " + $_.Exception.Message }
finally {
    if ($pipeLeser) { try { $pipeLeser.Dispose() } catch { } }
    if ($pipe) { try { $pipe.Dispose() } catch { } }
    # Der Helfer ohne Verbindung wartet bis zur Leerlaufgrenze (10 min): hier gewollt beendet.
    if ($helferProc -and -not $helferProc.HasExited) {
        try { $helferProc.Kill() } catch { }
        try { $helferProc.WaitForExit(5000) | Out-Null } catch { }
    }
}
Test-Result "Pipe-Zugriffsschutz: S-1-1-0 wird beim Start abgelehnt (Exit 7), fremde SID sperrt das eigene Konto aus ($schicht)" ($hits.Count -eq 0) $hits

# 7. Helferprobe (Roslyn): Sicherheitsbeschreibung der Pipe, Rechte auf dem Laufzeitordner,
# einmalige Uebernahme alter Dateien, Lauf-Id als Dateiname - alles in einem Wegwerf-Ordner.
# Pipe.cs zieht Messung und Ausfuehrung nach sich, deshalb wird alles ausser host\ uebersetzt
# (src\CommandRunner.cs und src\AutoRunner.cs haengen am Host). Ohne Argument laufen alle
# Gruppen der Probe, seit 8.2 also auch Sicherungsdatei und Rueckweg-Inhalt; die Gruppe
# "v8.2: Massnahmen" faehrt diese beiden noch einmal getrennt als eigene Pruefungen.
# Seit 8.3 kommt host\Selbstpruefung.cs dazu (helfer\Massnahmen\Programm.cs prueft die neue Fassung
# damit; die Datei haengt nur an src\AppLog.cs, nicht an WinForms oder WebView2) und die zwei
# Compression-Verweise fuer das ZIP-Paket; die Quellenliste steht einmal in Helferprobe-Quellen,
# die Verweise in $helferprobeRefs, weil drei Pruefungen dieselbe Probe uebersetzen (7, v8.2, v8.3).
function Helferprobe-Quellen {
    $quellen = @()
    $quellen += Get-ChildItem (Join-Path $root 'helfer\*.cs') | ForEach-Object { $_.FullName }
    $quellen += Get-ChildItem (Join-Path $root 'helfer\Massnahmen\*.cs') -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
    $quellen += Get-ChildItem (Join-Path $root 'kern\*.cs') | ForEach-Object { $_.FullName }
    $quellen += Get-ChildItem (Join-Path $root 'kern\Regeln\*.cs') | ForEach-Object { $_.FullName }
    $quellen += Get-ChildItem (Join-Path $root 'sammler\*.cs') | Where-Object { $_.Name -ne 'AufzeichnenCli.cs' } | ForEach-Object { $_.FullName }
    $quellen += Get-ChildItem (Join-Path $root 'sammler\Quellen\*.cs') | ForEach-Object { $_.FullName }
    $quellen += Get-ChildItem (Join-Path $root 'src\*.cs') | Where-Object { @('CommandRunner.cs', 'AutoRunner.cs', 'AssemblyInfo.cs') -notcontains $_.Name } | ForEach-Object { $_.FullName }
    $quellen += (Join-Path $root 'host\Selbstpruefung.cs')
    $quellen += (Join-Path $root 'tests\HelferProbe.cs')
    return ,$quellen
}
$helferprobeRefs = @('System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll', 'System.Management.dll',
                     'System.ServiceProcess.dll', 'System.Runtime.Serialization.dll', 'System.Xml.dll',
                     'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll')
$hits = @()
$exe = Join-Path $env:TEMP ('WW_HelferProbe_' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.exe')
try {
    $quellen = Helferprobe-Quellen
    $bauOut = & (Join-Path $root 'tools\csc.ps1') -Out $exe -Target exe -Sources $quellen -Refs $helferprobeRefs 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { $hits += "Helferprobe liess sich nicht uebersetzen: " + (($bauOut | Select-Object -Last 3) -join ' | ') }
    else {
        & $exe
        if ($LASTEXITCODE -ne 0) { $hits += "mindestens eine Teilpruefung der Helferprobe ist rot" }
    }
}
catch { $hits += "Helferprobe: " + $_.Exception.Message }
finally { Remove-Item $exe -ErrorAction SilentlyContinue }
Test-Result "Helferprobe: Pipe nur fuer SID und SYSTEM, Users-Rechte, einmalige Uebernahme, Lauf-Id als Dateiname" ($hits.Count -eq 0) $hits

# 8. Startpruefung: --selbstpruefung <datei> schreibt vier Werte und je Abweichung eine Zeile.
# Unveraendert: Exit 0 und uiStimmt=true. app.js um ein Byte verlaengert: Exit 6 (unsigniert,
# Dev-Bau und CI vor der Signatur) bzw. 5 (signiert) und die Zeile "veraendert: app.js". Eine
# fremde Datei unter ui\ ist nur ein Hinweis (Updater und Installer spiegeln ui\ nicht): Exit 0.
# Das Original kommt in jedem Fall zurueck (finally), und das wird NACH dem finally nachgemessen
# (B53): probe-fremd.js muss weg sein - release.yml packt bin\ui nach den Tests ins ZIP und der
# Installer nimmt ..\bin\ui\* mit, jede Installation meldete die Datei dann bei jedem Start als
# "ohne Eintrag in der Pruefliste". Und bin\ui\app.js muss Byte fuer Byte das Original sein (SHA-256
# vorher == nachher), nicht nur "irgendwie zurueckkopiert".
$hits = @()
$selbstAusgabe = Join-Path $env:TEMP ('WW_selbst_' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.txt')
$appJs = Join-Path $root 'bin\ui\app.js'
$appJsSicherung = Join-Path $env:TEMP ('WW_appjs_' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.bak')
$fremdeDatei = Join-Path $root 'bin\ui\probe-fremd.js'
$appJsHashVorher = $null
if (Test-Path $appJs) { $appJsHashVorher = (Get-FileHash $appJs -Algorithm SHA256).Hash }
function Selbstpruefung-Lauf {
    Remove-Item $selbstAusgabe -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $exeApp -ArgumentList @('--selbstpruefung', ('"' + $selbstAusgabe + '"')) -Wait -PassThru
    $null = $p.Handle
    $text = ''
    if (Test-Path $selbstAusgabe) { $text = [IO.File]::ReadAllText($selbstAusgabe, [Text.Encoding]::UTF8) }
    return New-Object PSObject -Property @{ Exit = $p.ExitCode; Text = $text }
}
try {
    if (-not (Test-Path $exeApp) -or -not (Test-Path $appJs)) { $hits += "bin\WindowsWartung.exe oder bin\ui\app.js fehlt - vorher build.ps1 laufen lassen" }
    else {
        $r1 = Selbstpruefung-Lauf
        if ($r1.Exit -ne 0) { $hits += "Unveraenderte Fassung: Exit $($r1.Exit) statt 0: " + ($r1.Text -replace '\s+', ' ') }
        if ($r1.Text -notmatch '(?m)^uiStimmt=true') { $hits += "Unveraenderte Fassung: Ausgabe ohne uiStimmt=true: " + ($r1.Text -replace '\s+', ' ') }
        $signiert = ($r1.Text -match '(?m)^signiert=true')

        Copy-Item $appJs $appJsSicherung -Force
        [IO.File]::AppendAllText($appJs, ' ', $pipeUtf8)
        $r2 = Selbstpruefung-Lauf
        $erwartetExit = 6
        if ($signiert) { $erwartetExit = 5 }
        if ($r2.Exit -ne $erwartetExit) { $hits += "Veraendertes app.js: Exit $($r2.Exit) statt $erwartetExit (signiert=$signiert)" }
        if ($r2.Text -notmatch '(?m)^uiStimmt=false') { $hits += "Veraendertes app.js: Ausgabe ohne uiStimmt=false" }
        if ($r2.Text -notmatch 'ver\u00E4ndert: app\.js') { $hits += "Veraendertes app.js: die Ausgabe nennt app.js nicht als veraendert: " + ($r2.Text -replace '\s+', ' ') }
        Copy-Item $appJsSicherung $appJs -Force

        [IO.File]::WriteAllText($fremdeDatei, "// Probe: fremde Datei, gehoert nicht zur Pruefliste`n", $pipeUtf8)
        $r3 = Selbstpruefung-Lauf
        if ($r3.Exit -ne 0) { $hits += "Fremde Datei bin\ui\probe-fremd.js: Exit $($r3.Exit) statt 0 (nur Hinweis erwartet): " + ($r3.Text -replace '\s+', ' ') }
        if ($r3.Text -notmatch '(?m)^uiStimmt=true') { $hits += "Fremde Datei: Ausgabe ohne uiStimmt=true" }
    }
}
catch { $hits += "Startpruefung: " + $_.Exception.Message }
finally {
    # Das Zurueckspielen darf die Suite nicht abbrechen; ob es geklappt hat, sagt der Hash-Vergleich unten.
    if (Test-Path $appJsSicherung) {
        try { Copy-Item $appJsSicherung $appJs -Force } catch { $hits += "bin\ui\app.js liess sich nicht zurueckspielen: " + $_.Exception.Message }
        Remove-Item $appJsSicherung -ErrorAction SilentlyContinue
    }
    # Defender haelt eine frisch geschriebene .js gelegentlich kurz offen: bis zu 5 Versuche, dann
    # entscheidet der Nachweis unten - nicht ein stilles SilentlyContinue.
    for ($versuch = 1; $versuch -le 5; $versuch++) {
        if (-not (Test-Path $fremdeDatei)) { break }
        try { Remove-Item $fremdeDatei -Force -ErrorAction Stop } catch { Start-Sleep -Milliseconds 300 }
    }
    Remove-Item $selbstAusgabe -ErrorAction SilentlyContinue
}
if (Test-Path $fremdeDatei) { $hits += "bin\ui\probe-fremd.js liess sich nicht entfernen - sie wanderte ins ZIP und in den Installer" }
if ($appJsHashVorher) {
    $appJsHashNachher = ''
    if (Test-Path $appJs) { $appJsHashNachher = (Get-FileHash $appJs -Algorithm SHA256).Hash }
    if ($appJsHashNachher -ne $appJsHashVorher) { $hits += "bin\ui\app.js ist nach der Probe nicht mehr das Original (SHA-256 $appJsHashNachher statt $appJsHashVorher)" }
}
Test-Result "Startpruefung: unveraendert 0, veraendertes app.js wird erkannt, fremde Datei nur Hinweis, hinterher weg und app.js unveraendert" ($hits.Count -eq 0) $hits

# 9. Laufzeitdaten liegen in der Ablage (ProgramData): Verlauf, app.log und Zeitplan ueber
# Kern.Ablage; LocalApplicationData nur noch als Quelle der einmaligen Uebernahme (Methoden
# Alter*). Der Installer gibt dem Ordner users-modify, sonst kann die Oberflaeche ohne Rechte
# nicht schreiben, was der erhoehte Helfer angelegt hat.
#
# Einmalig wird die Uebernahme erst durch das Umbenennen der alten Profildatei auf *.uebernommen
# (AppLog.UebernahmeAbschliessen, Vertrag Abschnitt 12), nicht durch Ablage.Uebernehmen allein:
# das kopiert immer, wenn am neuen Ort nichts liegt. Faellt der Aufruf in History.PfadErmitteln
# weg, holt der naechste Start nach "Verlauf leeren" den alten Verlauf erneut aus dem Profil (B52).
# Deshalb muss jede der drei Dateien den Aufruf haben - die Definition in AppLog.cs zaehlt nicht,
# dort muss er in PfadErmitteln stehen.
$hits = @()
$ablageDateien = [ordered]@{ 'src\History.cs' = 'Ablage\.Verlauf\(\)'; 'src\AppLog.cs' = 'Ablage\.Logs\(\)'; 'src\Scheduler.cs' = 'Ablage\.Maschinenweit\(\)' }
foreach ($rel in $ablageDateien.Keys) {
    $zeilen = [IO.File]::ReadAllLines((Join-Path $root $rel), [Text.Encoding]::UTF8)
    $text = $zeilen -join "`n"
    if ($text -notmatch $ablageDateien[$rel]) { $hits += "$rel nutzt nicht $($ablageDateien[$rel])" }
    if ($text -notmatch 'Ablage\.Uebernehmen\(') { $hits += "$rel uebernimmt die alte Datei nicht (Ablage.Uebernehmen fehlt)" }
    $abschluesse = 0
    foreach ($z in $zeilen) {
        if ($z -match '^\s*(///|//)') { continue }
        if ($z -match 'static\s+bool\s+UebernahmeAbschliessen\s*\(') { continue }
        if ($z -match 'UebernahmeAbschliessen\(') { $abschluesse++ }
    }
    if ($abschluesse -lt 1) { $hits += "$rel ruft UebernahmeAbschliessen nicht auf - die alte Profildatei bliebe liegen, die Uebernahme waere nicht einmalig" }
    if ($rel -eq 'src\AppLog.cs') {
        $pfadErmitteln = [regex]::Match($text, '(?s)static string PfadErmitteln\(\)\s*\{.*?\n        \}')
        if (-not $pfadErmitteln.Success) { $hits += "src\AppLog.cs: PfadErmitteln nicht auffindbar" }
        elseif ($pfadErmitteln.Value -notmatch 'UebernahmeAbschliessen\(') { $hits += "src\AppLog.cs: PfadErmitteln ruft UebernahmeAbschliessen nicht auf" }
    }
    for ($i = 0; $i -lt $zeilen.Count; $i++) {
        if ($zeilen[$i] -match '^\s*(///|//)') { continue }
        if ($zeilen[$i] -notmatch 'LocalApplicationData') { continue }
        $inUebernahme = $false
        for ($j = [Math]::Max(0, $i - 5); $j -lt $i; $j++) { if ($zeilen[$j] -match 'static string Alter\w*\(') { $inUebernahme = $true } }
        if (-not $inUebernahme) { $hits += "${rel}:$($i + 1)  LocalApplicationData ausserhalb der Uebernahme-Quelle: $($zeilen[$i].Trim())" }
    }
}
$iss = [IO.File]::ReadAllText((Join-Path $root 'installer\WindowsWartung.iss'), [Text.Encoding]::UTF8)
if ($iss -notmatch '(?m)^\[Dirs\]') { $hits += "installer\WindowsWartung.iss hat keinen Abschnitt [Dirs]" }
if ($iss -notmatch '(?m)^Name:\s*"\{commonappdata\}\\WindowsWartung"\s*;.*Permissions:\s*users-modify') { $hits += "installer\WindowsWartung.iss gibt {commonappdata}\WindowsWartung nicht users-modify" }
Test-Result "Laufzeitdaten liegen in der Ablage (ProgramData), Uebernahme wird abgeschlossen, Installer setzt users-modify" ($hits.Count -eq 0) $hits

# 10. RunJobs und die Klasse Job sind weg: jeder Lauf mit Rechten ist ein Plan aus Kennungen.
# Ein Rest davon waere ein zweiter Weg am Katalog vorbei. Gesucht wird der Bezeichner im Code:
# Zeichenketten und Zeilenkommentare werden vorher entfernt, denn seit dem Gegenlesen haengt
# jeder Werkzeugprozess an einem Win32-Job-Objekt (helfer\Werkzeuge.cs, Vertrag Abschnitt 14),
# und dessen Protokollzeilen ("Job-Objekt fuer ...") sind keine Klasse Job.
$hits = @()
foreach ($f in (Get-ChildItem (Join-Path $root 'src'),(Join-Path $root 'host'),(Join-Path $root 'helfer'),(Join-Path $root 'kern'),(Join-Path $root 'sammler') -File -Filter *.cs -Recurse)) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*(///|//)') { continue }
        $code = $line -replace '"([^"\\]|\\.)*"', '""'
        $code = $code -replace '//.*$', ''
        if ($code -cmatch '\bRunJobs\b|\bJob\b') { $hits += "$($f.Name):$i  $($line.Trim())" }
    }
}
Test-Result "Kein RunJobs und keine Job-Klasse mehr (jeder Lauf ist ein Plan)" ($hits.Count -eq 0) $hits

Write-Host "`nv8.2: Massnahmen" -ForegroundColor Cyan

# Meilenstein 3 (docs\M3-ENTWURF.md, Abschnitte 1, 3, 8 und 10.1): der Helfer kennt 20 weitere
# Kennungen, die am System eingreifen (Firewall, UAC, SmartScreen, DNS, Dienste, Aufgaben,
# Startprogramme, Updates, Geraete, Laufwerke), jede mit Sicherung vor dem Eingriff und einem
# Rueckweg aus dieser Sicherung. Die Pruefungen hier fahren die gebaute bin\WindowsWartung.exe
# nur mit --trocken (Ausnahme: Pruefung 9, netz.dnscache.leeren, nur erhoeht); die Sicherungsdatei
# und der Rueckweg-Inhalt laufen in-process in der Helferprobe gegen Wegwerf-Ordner. Der echte
# Ordner C:\ProgramData\WindowsWartung\sicherungen\massnahmen wird nur gezaehlt, nie beschrieben.

$m3Ordner = Join-Path $root 'helfer\Massnahmen'
$sicherungenOrdner = Join-Path $env:ProgramData 'WindowsWartung\sicherungen\massnahmen'
function Zaehle-Sicherungen { return @(Get-ChildItem $sicherungenOrdner -File -ErrorAction SilentlyContinue).Count }
# Die Werte, die die Stufe-2-Massnahmen schreiben wuerden: vorher == nachher beweist, dass der
# Trockenlauf sie nicht angefasst hat (nur lesen; ein fehlender Wert liest sich als "(fehlt)").
# Fuenfter Wert (Gegenlesen 18.09.2026): systemschutz.punkt.anlegen setzt im echten Lauf die
# 24-Stunden-Drossel SystemRestorePointCreationFrequency = 0 (helfer\Katalog.cs DrosselAufheben);
# die Trocken-Weiche davor ist die einzige Sperre, und weder Folgenummer noch Kindprozess noch
# Dateizahl saehen einen Registry-Schreibzugriff.
function Lies-Schutzwerte {
    $paare = @(
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer', 'SmartScreenEnabled'),
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System', 'EnableLUA'),
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System', 'ConsentPromptBehaviorAdmin'),
        @('HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem', 'DisableDeleteNotification'),
        @('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore', 'SystemRestorePointCreationFrequency')
    )
    $teile = @()
    foreach ($p in $paare) {
        $wert = '(fehlt)'
        try {
            $eintrag = Get-ItemProperty -Path $p[0] -Name $p[1] -ErrorAction Stop
            if ($null -ne $eintrag) { $wert = "$($eintrag.$($p[1]))" }
        } catch { }
        $teile += ($p[1] + '=' + $wert)
    }
    return ($teile -join ', ')
}
# Lesewerte der Objekte, an denen Plan A und Plan B ansetzen (Gegenlesen 18.09.2026): drei Plan-A-
# Schritte schreiben keine Sicherungsdatei und starten keinen Kindprozess (netz.dhcp.erneuern ueber
# WMI RenewDHCPLease, volume.scan und volume.optimieren ueber MSFT_Volume), ihre Trocken-Weiche ist
# die einzige Sperre; fiele sie weg, saehe keine der uebrigen Nachher-Messungen es. Deshalb je Objekt
# ein Wert, den der echte Eingriff aendern wuerde: DHCPLeaseObtained und DNSServerSearchOrder des
# gewaehlten Adapters, ConfigManagerErrorCode des Plan-B-Geraets, StartMode von Spooler und wuauserv,
# Enabled des Firewall-Profils Privat (profil=2), Settings.Enabled der Aufgabe ScheduledDefrag, die
# StartupApproved-Bytes des Run-Werts. Nur lesen; fehlt ein Wert, steht "(fehlt)", und vorher ==
# nachher gilt dann trotzdem. Rueckgabe: geordnetes Woerterbuch Name -> Wert.
function Lies-Objektwerte([object]$adapterIndex, [string]$geraetId, [string]$runWert) {
    $werte = [ordered]@{}
    if ($null -ne $adapterIndex) {
        $lease = '(fehlt)'; $dns = '(fehlt)'
        try {
            $nac = Get-CimInstance Win32_NetworkAdapterConfiguration -Filter "InterfaceIndex = $adapterIndex" -ErrorAction Stop | Select-Object -First 1
            if ($null -ne $nac) {
                $lease = '(leer)'
                if ($null -ne $nac.DHCPLeaseObtained) { $lease = ([DateTime]$nac.DHCPLeaseObtained).ToString('s') }
                $dns = (@($nac.DNSServerSearchOrder) -join '|')
            }
        } catch { }
        $werte["Adapter$adapterIndex.DHCPLeaseObtained"] = $lease
        $werte["Adapter$adapterIndex.DNSServerSearchOrder"] = $dns
    }
    if ($geraetId.Length -gt 0 -and $geraetId -ne 'PCI\VEN_0000&DEV_0000\0') {
        $code = '(fehlt)'
        try {
            # WQL-Literal: Rueckstrich und Apostroph verdoppelt bzw. maskiert (die Id kommt aus WMI selbst).
            $wql = $geraetId -replace '\\', '\\' -replace "'", "\'"
            $pnp = Get-CimInstance Win32_PnPEntity -Filter "PNPDeviceID = '$wql'" -ErrorAction Stop | Select-Object -First 1
            if ($null -ne $pnp) { $code = "$($pnp.ConfigManagerErrorCode)" }
        } catch { }
        $werte['Geraet.ConfigManagerErrorCode'] = $code
    }
    foreach ($n in @('Spooler', 'wuauserv')) {
        $art = '(fehlt)'
        try {
            $d = Get-CimInstance Win32_Service -Filter "Name = '$n'" -ErrorAction Stop | Select-Object -First 1
            if ($null -ne $d) { $art = "$($d.StartMode)" }
        } catch { }
        $werte["$n.StartMode"] = $art
    }
    $fwAn = '(fehlt)'
    try { $fwAn = "$((Get-NetFirewallProfile -Name Private -ErrorAction Stop).Enabled)" } catch { }
    $werte['FirewallPrivat.Enabled'] = $fwAn
    $aufgabe = '(fehlt)'
    try { $aufgabe = "$((Get-ScheduledTask -TaskPath '\Microsoft\Windows\Defrag\' -TaskName 'ScheduledDefrag' -ErrorAction Stop).Settings.Enabled)" } catch { }
    $werte['ScheduledDefrag.Enabled'] = $aufgabe
    if ($runWert.Length -gt 0) {
        $bytes = '(fehlt)'
        try {
            $roh = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' -Name $runWert -ErrorAction Stop).$runWert
            if ($null -ne $roh) { $bytes = (@($roh) | ForEach-Object { ([byte]$_).ToString('x2') }) -join '' }
        } catch { }
        $werte['StartupApproved.' + $runWert] = $bytes
    }
    return $werte
}

# Die Helferprobe (tests\HelferProbe.cs, in-process, Wegwerf-Ordner) wird fuer diese Gruppe einmal
# uebersetzt: Pruefung 1 liest ueber ihre Gruppe kennungen die Laufzeitmenge der M3-Kennungen,
# die Pruefungen 3 und 4 fahren die Gruppen sicherung und rueckweg; entfernt wird sie im finally
# der Pruefungen 3 und 4. Uebersetzt wird wie in Probe 7 alles ausser host\.
$exeM3 = $null
$exeM3Fehler = ''
try {
    $exeM3Pfad = Join-Path $env:TEMP ('WW_HelferProbeM3_' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.exe')
    $quellen = Helferprobe-Quellen
    $bauOut = & (Join-Path $root 'tools\csc.ps1') -Out $exeM3Pfad -Target exe -Sources $quellen -Refs $helferprobeRefs 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exeM3Pfad)) { $exeM3Fehler = "Helferprobe liess sich nicht uebersetzen: " + (($bauOut | Select-Object -Last 3) -join ' | ') }
    else { $exeM3 = $exeM3Pfad }
}
catch { $exeM3Fehler = "Helferprobe: " + $_.Exception.Message }

# 1. Trockenlauf der M3-Kennungen. Die Grundmenge ist die Laufzeitmenge: die Helferprobe ruft
# MassnahmenM3.Alle() auf und gibt jede Kennung als Zeile "kennung: ..." aus (Gruppe kennungen).
# Ein Grep ueber Massnahmenbau.Neu("...") reichte nicht: eine Massnahme, die wie wartung.massnahmen
# per Objektinitialisierer angemeldet wird, blieb ihm verborgen (Gegenlesen 18.09.2026, Mutation in
# Hardware.cs blieb gruen). Der Grep bleibt als Gegenprobe: jede Massnahmenbau.Neu("...") in
# helfer\Massnahmen\*.cs (auch ueber eine Konstante wie KDnsCache, aufgeloest ueber "const string
# Name = ..." derselben Datei) muss in der Laufzeitmenge liegen, sonst ist der Aufruf tot. Die feste
# Liste hier muss genau die Laufzeitmenge sein: eine neue Kennung ohne Trockenlauf faellt so auf,
# eine entfernte ebenso. rueckweg braucht eine echte Sicherungsdatei unter dem geschuetzten Ordner
# und laeuft deshalb nicht im Trockenlauf; seine Ablehnungen misst Pruefung 2, seinen Inhalt Pruefung 4.
#
# Plan A: alle Kennungen, deren Parameter maschinenunabhaengig sind. Jede Vorbedingung, die der
# Helfer aus dem Systemzustand liest, wird hier vorher mit DEMSELBEN Kriterium gelesen, und bei
# Nichterfuellung faellt der Schritt weg (die Ergebniszeile nennt ihn): sonst misst die Probe den
# Rechner statt den Trockenlauf. Der DHCP-Adapter kommt aus Win32_NetworkAdapterConfiguration
# (IPEnabled und DHCPEnabled, mit Adresse), wie AdapterPruefen in helfer\Massnahmen\Netz.cs - bis
# 18.09.2026 kam er aus Get-NetIPInterface, und der CI-Lauf 35354428200 (Azure-VM) endete mit Exit 2,
# also mit einer Ablehnung VOR dem ersten Schritt; von den Parameterpruefungen des Plans liest nur
# AdapterPruefen den Systemzustand, und die beiden Sichten muessen nicht dieselben Anschluesse
# fuehren (der Grund selbst stand damals nicht in der Ausgabe, deshalb Lies-Laufgrund unten).
# Ebenso: Spooler und wuauserv als Win32_Service (DienstLesen),
# ScheduledDefrag ueber Schedule.Service (AufgabeLesen), C: als MSFT_Volume mit DriveType 3 und fuer
# den Scan NTFS/ReFS (VolumeLesen), Defender ueber MSFT_MpComputerStatus.AMServiceEnabled
# (DefenderVorbedingung), die SmartScreen-Richtlinie EnableSmartScreen = 0 (SmartScreenWarn).
# Eine Ablehnung (Exit 2) oder ein Problem (Exit 1) bleibt rot, aber die Details nennen den Grund
# (Lies-Laufgrund): abgelehnt-Zeile, letzte Befunde, letzte app.log-Zeilen. Erwartet: Exit 0 in unter
# 60 s (ein echter Scan oder ein echtes Optimieren von C: dauert Minuten), je Kennung eine
# schritt-Zeile, kein Kindprozess, keine Datei unter sicherungen\massnahmen, Waechterdatei in %TEMP%
# bleibt, Folgenummer der Wiederherstellungspunkte unveraendert, die vier Registrierungswerte und
# die Lesewerte aus Lies-Objektwerte vorher == nachher.
# Plan B: Objekte, die es auf dem Rechner geben muss (ein Run-Wert unter HKLM, ein Update aus dem
# WUA-Cache, ein Geraet mit Problemcode 22). Fehlt eines, bleibt der Ersatz stehen, und die Massnahme
# meldet das fehlende Objekt: Exit 0 oder 1, nie 2 (2 hiesse: die Parameterpruefung lehnt einen
# gueltigen Wert ab), kein Kindprozess, keine Datei; das Geraet traegt nachher denselben Problemcode.
$hits = @()
$m3Erwartet = @('netz.dnscache.leeren', 'netz.dhcp.erneuern', 'netz.dns.zuruecksetzen',
                'sicherheit.signaturen.aktualisieren', 'sicherheit.schnellscan', 'sicherheit.firewall.einschalten',
                'sicherheit.smartscreen.warn', 'sicherheit.uac.standard',
                'volume.scan', 'volume.optimieren', 'datentraeger.trim.einschalten', 'systemschutz.punkt.anlegen',
                'leistung.autostart.deaktivieren', 'leistung.aufgabe.deaktivieren', 'leistung.dienst.startart',
                'update.dienst.aktivieren', 'update.verbergen', 'geraet.aktivieren', 'rueckweg', 'wartung.massnahmen',
                # M4 (docs\M4-ENTWURF.md 1, 8.2 Punkt 1): die 13 Kennungen der Stufe 3 und der neuen Stufe 2;
                # dateien.reparieren, tiefenpruefung und wiederherstellungspunkt.zurueck bleiben M2-Kennungen
                # aus helfer\Katalog.cs (ohne Massnahmenbau.Neu) und stehen deshalb nicht hier.
                'netz.hosts.bereinigen', 'netz.winsock.zuruecksetzen', 'netz.ip.zuruecksetzen',
                'sicherheit.firewall.eingehend.blockieren', 'sicherheit.firewall.zuruecksetzen',
                'volume.spotfix', 'volume.reparatur', 'geraet.treiber.neu', 'leistung.winlogon.zuruecksetzen',
                'speicher.papierkorb.leeren', 'stabilitaet.speichertest.planen', 'update.zuruecksetzen', 'update.anwenden')
# Laufzeitmenge: Gruppe kennungen der Helferprobe (MassnahmenM3.Alle()).
$m3ImCode = @()
if ($null -eq $exeM3) { $hits += $exeM3Fehler }
else {
    $ausgabe = @(& $exeM3 kennungen 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { $hits += "Gruppe kennungen der Helferprobe ist rot:"; $hits += @($ausgabe | Where-Object { $_ -match '\[FEHL\]' } | Select-Object -First 8) }
    foreach ($z in $ausgabe) { if ($z -match '^\s*kennung:\s*([a-z0-9.]+)\s*$') { $m3ImCode += $matches[1] } }
    if ($m3ImCode.Count -eq 0) { $hits += "Die Helferprobe hat keine Zeile 'kennung: ...' ausgegeben (MassnahmenM3.Alle() leer oder Gruppe kennungen fehlt)" }
}
$m3ImCode = @($m3ImCode | Sort-Object -Unique)
# Gegenprobe aus dem Code: jede Massnahmenbau.Neu("...") muss in der Laufzeitmenge liegen.
$m3Statisch = @()
foreach ($f in (Get-ChildItem $m3Ordner -File -Filter *.cs)) {
    $text = [IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8)
    $konstanten = @{}
    foreach ($m in [regex]::Matches($text, 'const\s+string\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*"([a-z.]+)"')) { $konstanten[$m.Groups[1].Value] = $m.Groups[2].Value }
    # Seit 8.3 auch qualifiziert (UpdatesMassnahmen.KennungReset, ProgrammMassnahmen.Kennung): der Klassenname
    # vor dem Punkt wird abgestreift, die Konstante muss weiter in DERSELBEN Datei stehen.
    foreach ($m in [regex]::Matches($text, 'Massnahmenbau\.Neu\(\s*(?:"([a-z.]+)"|(?:[A-Za-z_][A-Za-z0-9_]*\.)?([A-Za-z_][A-Za-z0-9_]*))')) {
        if ($m.Groups[1].Success) { $m3Statisch += $m.Groups[1].Value }
        elseif ($konstanten.ContainsKey($m.Groups[2].Value)) { $m3Statisch += $konstanten[$m.Groups[2].Value] }
        else { $hits += "$($f.Name): Massnahmenbau.Neu($($m.Groups[2].Value)) - die Konstante ist in der Datei nicht aufloesbar" }
    }
}
foreach ($k in @($m3Statisch | Sort-Object -Unique)) { if ($m3ImCode -notcontains $k) { $hits += "Massnahmenbau.Neu('$k') steht in helfer\Massnahmen\, aber MassnahmenM3.Alle() liefert die Kennung nicht (toter Aufruf)" } }
foreach ($k in $m3ImCode) { if ($m3Erwartet -notcontains $k) { $hits += "Kennung '$k' kommt aus MassnahmenM3.Alle(), steht aber nicht in der festen Liste dieser Probe" } }
foreach ($k in $m3Erwartet) { if ($m3ImCode -notcontains $k) { $hits += "Kennung '$k' fehlt in MassnahmenM3.Alle() (helfer\Massnahmen\ meldet sie nicht mehr an)" } }

# Adapter: dieselbe Sicht wie AdapterPruefen/AdapterLesen in helfer\Massnahmen\Netz.cs - die erste
# Win32_NetworkAdapterConfiguration (nach InterfaceIndex) mit IPEnabled, DHCPEnabled und mindestens
# einer Adresse. Bis 18.09.2026 kam der Index aus Get-NetIPInterface (Dhcp=Enabled,
# ConnectionState=Connected); die beiden Sichten muessen nicht dieselben Anschluesse fuehren, und
# AdapterPruefen ist die einzige Parameterpruefung des Plans, die den Systemzustand liest - der
# naechstliegende Grund fuer das Exit 2 der CI. Der Helfer prueft nur WMI, also waehlt die Probe nur aus WMI.
$adapterIndex = $null
$adapterText = ''
try {
    $nac = @(Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled = TRUE AND DHCPEnabled = TRUE' -ErrorAction Stop |
             Where-Object { $null -ne $_.InterfaceIndex -and @($_.IPAddress | Where-Object { "$_".Length -gt 0 }).Count -gt 0 } |
             Sort-Object InterfaceIndex)
    if ($nac.Count -gt 0) { $adapterIndex = [int]$nac[0].InterfaceIndex; $adapterText = "$($nac[0].Description)" }
} catch { }
# Defender: nur mit laufendem Dienst messen die beiden Sicherheits-Massnahmen den Trockenlauf
# (DefenderVorbedingung: Klasse fehlt oder AMServiceEnabled falsch -> "nicht bestanden", Exit 1).
$defenderAn = $false
try {
    $mp = Get-CimInstance -Namespace 'root\Microsoft\Windows\Defender' -ClassName MSFT_MpComputerStatus -ErrorAction Stop | Select-Object -First 1
    if ($null -ne $mp -and $mp.AMServiceEnabled) { $defenderAn = $true }
} catch { }
# SmartScreen: eine Richtlinie EnableSmartScreen = 0 ueberstimmt den lokalen Wert, und die Massnahme
# meldet dann zu Recht "nicht bestanden" (Exit 1), was hier nicht der Trockenlauf ist, der gemessen wird.
$smartScreenRichtlinie = $null
try { $smartScreenRichtlinie = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' -Name 'EnableSmartScreen' -ErrorAction Stop).EnableSmartScreen } catch { }
# C: wie VolumeLesen: MSFT_Volume mit DriveLetter C, DriveType 3 (fest); der Scan zusaetzlich nur NTFS/ReFS.
# Liest sich die Klasse nicht (Namespace fehlt, WMI haengt), gilt das wie "nicht vorhanden", und die Zeile sagt es.
$volumeC = $null
$volumeCGrund = 'MSFT_Volume kennt kein Laufwerk C:'
try {
    $volumeC = Get-CimInstance -Namespace 'root\Microsoft\Windows\Storage' -ClassName MSFT_Volume -Filter "DriveLetter = 'C'" -ErrorAction Stop | Select-Object -First 1
    if ($null -ne $volumeC -and [int]$volumeC.DriveType -ne 3) { $volumeCGrund = "C: ist kein festes Laufwerk (DriveType $($volumeC.DriveType))"; $volumeC = $null }
} catch { $volumeCGrund = 'MSFT_Volume nicht lesbar: ' + $_.Exception.Message; $volumeC = $null }
$volumeCScan = $null -ne $volumeC -and @('NTFS', 'ReFS') -contains "$($volumeC.FileSystem)"
# Dienste wie DienstLesen: Win32_Service mit dem Namen; fehlt er, meldet der Schritt "nicht vorhanden" (Exit 1).
$dienstDa = @{}
foreach ($n in @('Spooler', 'wuauserv')) {
    $dienstDa[$n] = $false
    try { if ($null -ne (Get-CimInstance Win32_Service -Filter "Name = '$n'" -ErrorAction Stop | Select-Object -First 1)) { $dienstDa[$n] = $true } } catch { }
}
# Aufgabe wie AufgabeLesen: COM Schedule.Service, GetFolder(Ordner).GetTask(Name); nicht vorhanden wirft 0x80070002.
$defragDa = $false
$svc = $null
try {
    $svc = New-Object -ComObject 'Schedule.Service'
    $svc.Connect()
    if ($null -ne $svc.GetFolder('\Microsoft\Windows\Defrag').GetTask('ScheduledDefrag')) { $defragDa = $true }
} catch { }
finally { if ($null -ne $svc) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($svc) } }

$weggelassen = @()
$planA = @(@{ kennung = 'netz.dnscache.leeren'; parameter = @{} })
if ($null -ne $adapterIndex) {
    $planA += @{ kennung = 'netz.dns.zuruecksetzen'; parameter = @{ adapter = "$adapterIndex" } }
    $planA += @{ kennung = 'netz.dhcp.erneuern'; parameter = @{ adapter = "$adapterIndex" } }
} else { $weggelassen += 'netz.dns.zuruecksetzen und netz.dhcp.erneuern (keine Win32_NetworkAdapterConfiguration mit IPEnabled, DHCPEnabled und Adresse)' }
if ($defenderAn) {
    $planA += @{ kennung = 'sicherheit.signaturen.aktualisieren'; parameter = @{} }
    $planA += @{ kennung = 'sicherheit.schnellscan'; parameter = @{} }
} else { $weggelassen += 'sicherheit.signaturen.aktualisieren und sicherheit.schnellscan (Defender-Dienst nicht an)' }
$planA += @(@{ kennung = 'sicherheit.firewall.einschalten'; parameter = @{ profil = '2' } })
if ("$smartScreenRichtlinie" -ne '0') { $planA += @{ kennung = 'sicherheit.smartscreen.warn'; parameter = @{} } }
else { $weggelassen += 'sicherheit.smartscreen.warn (Richtlinie EnableSmartScreen = 0)' }
$planA += @(
    @{ kennung = 'sicherheit.uac.standard'; parameter = @{} },
    @{ kennung = 'datentraeger.trim.einschalten'; parameter = @{} }
)
if ($volumeCScan) { $planA += @{ kennung = 'volume.scan'; parameter = @{ buchstabe = 'C' } } }
elseif ($null -ne $volumeC) { $weggelassen += "volume.scan (C: hat das Dateisystem $($volumeC.FileSystem), der Scan gilt nur NTFS/ReFS)" }
else { $weggelassen += "volume.scan ($volumeCGrund)" }
if ($null -ne $volumeC) { $planA += @{ kennung = 'volume.optimieren'; parameter = @{ buchstabe = 'C' } } }
else { $weggelassen += "volume.optimieren ($volumeCGrund)" }
$planA += @(@{ kennung = 'systemschutz.punkt.anlegen'; parameter = @{} })
if ($defragDa) { $planA += @{ kennung = 'leistung.aufgabe.deaktivieren'; parameter = @{ pfad = '\Microsoft\Windows\Defrag\ScheduledDefrag' } } }
else { $weggelassen += 'leistung.aufgabe.deaktivieren (Aufgabe \Microsoft\Windows\Defrag\ScheduledDefrag nicht in der Aufgabenplanung)' }
if ($dienstDa['Spooler']) { $planA += @{ kennung = 'leistung.dienst.startart'; parameter = @{ name = 'Spooler'; startart = 'Manual' } } }
else { $weggelassen += 'leistung.dienst.startart (Dienst Spooler nicht vorhanden)' }
if ($dienstDa['wuauserv']) { $planA += @{ kennung = 'update.dienst.aktivieren'; parameter = @{ name = 'wuauserv' } } }
else { $weggelassen += 'update.dienst.aktivieren (Dienst wuauserv nicht vorhanden)' }
$planA += @(@{ kennung = 'wartung.massnahmen'; parameter = @{ freigaben = 'netz.dnscache.leeren' } })

# Plan B: Run-Wert (erster Wertname ohne Semikolon), UpdateId aus dem WUA-Cache (Online=false,
# IsInstalled=0, hoechstens 60 s in einem eigenen Prozess), Geraet mit Problemcode 22.
$runWert = $null
try {
    $runKey = Get-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -ErrorAction Stop
    foreach ($n in $runKey.GetValueNames()) { if ($n.Length -gt 0 -and $n.IndexOf(';') -lt 0) { $runWert = $n; break } }
} catch { }
$updateId = '00000000-0000-0000-0000-000000000000'
$wuaJob = $null
try {
    $wuaJob = Start-Job -ScriptBlock {
        $s = (New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher()
        $s.Online = $false
        $r = $s.Search('IsInstalled=0')
        if ($r.Updates.Count -gt 0) { $r.Updates.Item(0).Identity.UpdateID } else { '' }
    }
    if (Wait-Job $wuaJob -Timeout 60) {
        $wuaErg = "$(Receive-Job $wuaJob -ErrorAction SilentlyContinue)".Trim()
        if ($wuaErg -match '^[0-9a-fA-F-]{36}$') { $updateId = $wuaErg }
    }
} catch { }
finally { if ($wuaJob) { Stop-Job $wuaJob -ErrorAction SilentlyContinue; Remove-Job $wuaJob -Force -ErrorAction SilentlyContinue } }
$geraetId = 'PCI\VEN_0000&DEV_0000\0'
try {
    $g = @(Get-CimInstance Win32_PnPEntity -Filter 'ConfigManagerErrorCode = 22' -ErrorAction Stop | Where-Object { "$($_.PNPDeviceID)".Length -gt 0 })
    if ($g.Count -gt 0) { $geraetId = "$($g[0].PNPDeviceID)" }
} catch { }
$planB = @(
    @{ kennung = 'update.verbergen'; parameter = @{ updateId = $updateId } },
    @{ kennung = 'geraet.aktivieren'; parameter = @{ instanzId = $geraetId } }
)
if ($null -ne $runWert) { $planB += @{ kennung = 'leistung.autostart.deaktivieren'; parameter = @{ hive = 'HKLM'; ort = 'Run'; name = $runWert } } }
else { $weggelassen += 'leistung.autostart.deaktivieren (kein Wert unter HKLM\...\CurrentVersion\Run)' }

$marker = Join-Path $env:TEMP 'ww-probe-marker-m3.txt'
# Gemessen 18.09.2026: root\default verweigert die SystemRestore-Abfrage auch erhoeht unter pwsh 7
# ("Zugriff verweigert"), unter Windows PowerShell 5.1 (CI) liefert sie die Punkte.
$sicherungText = 'Folgenummer nicht lesbar (nicht erhoeht, oder root\default verweigert wie unter pwsh 7)'
$lesewerteText = 'Lesewerte nicht gemessen'
$planASekunden = '?'
$planBText = ''
$planADatei = $null; $planBDatei = $null
$protokollA = $null; $protokollB = $null
try {
    if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt - vorher build.ps1 laufen lassen" }
    else {
        [IO.File]::WriteAllText($marker, "Waechterdatei der M3-Trockenlauf-Probe: muss den Lauf ueberleben.`n", (New-Object Text.UTF8Encoding $false))
        $dateienVorher = Zaehle-Sicherungen
        $seqVorher = Lies-Sicherungsnummer
        $werteVorher = Lies-Schutzwerte
        $lesewerteVorher = Lies-Objektwerte $adapterIndex $geraetId $runWert

        # ---- Plan A ----
        $planId = 'probe-m3a-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
        $protokollA = Join-Path $protokollOrdner ('lauf-' + $planId + '.jsonl')
        $planADatei = Schreibe-Plan $planId $planA
        $lauf = Starte-Helfer @('--helfer', '--plan', ('"' + $planADatei + '"'), '--trocken') $true
        $planASekunden = $lauf.Sekunden
        if ($null -eq $lauf.ExitCode) { $hits += "Plan A: ExitCode des Helfers nicht lesbar (Prozess-Handle nicht gebunden)" }
        elseif ($lauf.ExitCode -ne 0) {
            # Der Grund muss in der Ausgabe stehen (CI-Lauf 35354428200: Protokoll und app.log lagen
            # auf dem Runner, die Zeile nannte nur den Pfad). 2 = abgelehnt (Parameterpruefung, VOR dem
            # ersten Schritt), 1 = ein Schritt meldete ein Problem, 3 = Ausnahme.
            $hits += "Plan A: Trockenlauf endete mit Exit $($lauf.ExitCode) statt 0 (Plan $planId, $($planA.Count) Schritte, Adapter $adapterIndex; Grund aus $protokollA und logs\app.log):"
            $hits += Lies-Laufgrund $protokollA $planId 'Plan A:'
        }
        if ($lauf.Kinder.Count -gt 0) { $hits += "Plan A: der Trockenlauf hat Kindprozesse gestartet: " + ($lauf.Kinder -join ', ') }
        # Zeitgrenze: ein Trockenlauf liest nur; ein echter Scan (MSFT_Volume.Repair) oder ein echtes
        # Optimieren von C: dauert Minuten und hinterlaesst weder Datei noch Kindprozess.
        if ($lauf.Sekunden -ge 60) { $hits += "Plan A: der Trockenlauf brauchte $($lauf.Sekunden) s (Grenze 60 s): ein Schritt hat vermutlich echt gearbeitet (Scan oder Optimieren)" }
        if (-not (Test-Path $protokollA)) { $hits += "Plan A: Laufprotokoll fehlt: $protokollA" }
        else {
            $zeilen = Lies-Laufprotokoll $protokollA
            if (@($zeilen | Where-Object { $_.art -eq 'abgelehnt' }).Count -gt 0) {
                # Vor dem ersten Schritt abgelehnt: keine anfang-, schritt- oder ende-Zeile. Der Grund
                # steht oben (Lies-Laufgrund); eine Zaehlung je Kennung waere nur Laerm davor.
                $hits += "Plan A: der Plan wurde vor dem ersten Schritt abgelehnt, keine der $($planA.Count) Kennungen ist gelaufen"
            }
            else {
                foreach ($s in $planA) {
                    $k = $s.kennung
                    $da = @($zeilen | Where-Object { $_.art -eq 'schritt' -and $_.kennung -eq $k })
                    if ($da.Count -lt 1) { $hits += "Plan A: keine schritt-Zeile fuer '$k' im Laufprotokoll" }
                    elseif ($lauf.ExitCode -ne 0) {
                        # Die schritt-Zeile sagt nur "ist beendet"; den Satz dazu traegt die nachweis-Zeile
                        # (bestanden=false) oder die fehler-Zeile derselben Kennung. Ein Problem ohne
                        # beides (Katalog.Problem nach einer [bad]-Zeile) steht nur in app.log, siehe oben.
                        foreach ($z in $da) {
                            if (-not $z.fachmann.problem) { continue }
                            $warum = @($zeilen | Where-Object { $_.kennung -eq $k -and ($_.art -eq 'fehler' -or ($_.art -eq 'nachweis' -and $_.fachmann.bestanden -eq $false)) } | ForEach-Object { $_.laie })
                            if ($warum.Count -eq 0) { $warum = @('ohne nachweis- oder fehler-Zeile; der Satz steht als [bad]-Zeile in app.log') }
                            $hits += "Plan A: '$k' meldete ein Problem: " + ($warum -join ' | ')
                        }
                    }
                }
                # Kein bestandener Nachweis im Trockenlauf (Vertrag 8.7): jede nachweis-Zeile traegt trocken=true.
                foreach ($z in @($zeilen | Where-Object { $_.art -eq 'nachweis' })) {
                    if ($z.fachmann.bestanden -eq $true) { $hits += "Plan A: '$($z.kennung)' meldet im Trockenlauf einen bestandenen Nachweis: " + $z.laie }
                }
                $ende = @($zeilen | Where-Object { $_.art -eq 'ende' -and $_.kennung -eq 'plan' })
                if ($ende.Count -ne 1) { $hits += "Plan A: keine ende-Zeile des Plans ($($ende.Count))" }
                elseif (-not $ende[0].fachmann.trocken) { $hits += "Plan A: die ende-Zeile traegt nicht trocken=true" }
            }
        }

        # ---- Plan B ----
        $planId = 'probe-m3b-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
        $protokollB = Join-Path $protokollOrdner ('lauf-' + $planId + '.jsonl')
        $planBDatei = Schreibe-Plan $planId $planB
        $laufB = Starte-Helfer @('--helfer', '--plan', ('"' + $planBDatei + '"'), '--trocken') $true
        if ($null -eq $laufB.ExitCode) { $hits += "Plan B: ExitCode des Helfers nicht lesbar" }
        elseif ($laufB.ExitCode -ne 0 -and $laufB.ExitCode -ne 1) {
            $hits += "Plan B: Exit $($laufB.ExitCode) statt 0 oder 1 (2 = ein gueltiger Parameter wurde abgelehnt; Plan $planId; Grund aus $protokollB und logs\app.log):"
            $hits += Lies-Laufgrund $protokollB $planId 'Plan B:'
        }
        if ($laufB.Kinder.Count -gt 0) { $hits += "Plan B: Kindprozesse " + ($laufB.Kinder -join ', ') }
        $planBText = "Plan B Exit $($laufB.ExitCode)"
        if (-not (Test-Path $protokollB)) { $hits += "Plan B: Laufprotokoll fehlt: $protokollB" }
        else {
            $zeilen = Lies-Laufprotokoll $protokollB
            if (@($zeilen | Where-Object { $_.art -eq 'abgelehnt' }).Count -gt 0) {
                $hits += "Plan B: der Plan wurde vor dem ersten Schritt abgelehnt, keine der $($planB.Count) Kennungen ist gelaufen"
            }
            else {
                $fehlend = @()
                foreach ($s in $planB) {
                    $k = $s.kennung
                    $da = @($zeilen | Where-Object { $_.art -eq 'schritt' -and $_.kennung -eq $k })
                    if ($da.Count -lt 1) { $hits += "Plan B: keine schritt-Zeile fuer '$k'" }
                    elseif ($da[0].fachmann.problem) { $fehlend += $k }
                }
                if ($laufB.ExitCode -eq 1 -and $fehlend.Count -eq 0) { $hits += "Plan B: Exit 1, aber keine schritt-Zeile nennt ein Problem" }
                if ($fehlend.Count -gt 0) { $planBText += ', Objekt fehlt bei ' + ($fehlend -join ', ') }
                foreach ($z in @($zeilen | Where-Object { $_.art -eq 'nachweis' })) {
                    if ($z.fachmann.bestanden -eq $true) { $hits += "Plan B: '$($z.kennung)' meldet im Trockenlauf einen bestandenen Nachweis: " + $z.laie }
                }
            }
        }

        # ---- Nachher ----
        if (-not (Test-Path $marker)) { $hits += "Die Waechterdatei in %TEMP% ist weg: $marker" }
        $dateienNachher = Zaehle-Sicherungen
        if ($dateienNachher -ne $dateienVorher) { $hits += "Unter $sicherungenOrdner liegen nachher $dateienNachher statt $dateienVorher Dateien: der Trockenlauf hat eine Sicherung geschrieben" }
        $seqNachher = Lies-Sicherungsnummer
        if ($null -ne $seqVorher) {
            $sicherungText = "Folgenummer $seqVorher gehalten"
            if ($seqNachher -ne $seqVorher) { $hits += "Die hoechste Folgenummer der Wiederherstellungspunkte hat sich geaendert: $seqVorher -> $seqNachher" }
        }
        $werteNachher = Lies-Schutzwerte
        if ($werteNachher -ne $werteVorher) { $hits += "Registrierungswerte vorher [$werteVorher] nachher [$werteNachher]: der Trockenlauf hat geschrieben" }
        # Zweiter Riegel fuer die Drossel: steht SystemRestorePointCreationFrequency auf diesem Rechner
        # schon auf 0 (Entwicklerrechner nach einem echten Lauf), liest sich 0 -> 0 auch dann, wenn
        # DrosselAufheben(k) vor die Trocken-Weiche gerutscht ist. Deshalb strukturell: genau ein Aufruf
        # in helfer\Katalog.cs, und der steht in Wiederherstellungspunkt hinter dem "return false;"
        # des "if (k.Trocken)"-Blocks.
        $katalogText = [IO.File]::ReadAllText((Join-Path $root 'helfer\Katalog.cs'))
        $drosselAufrufe = [regex]::Matches($katalogText, 'DrosselAufheben\(k\);')
        if ($drosselAufrufe.Count -ne 1) { $hits += "helfer\Katalog.cs ruft DrosselAufheben(k) $($drosselAufrufe.Count) mal statt genau einmal: die Drossel-Weiche ist nicht mehr an einer Stelle pruefbar" }
        else {
            $fnStart = $katalogText.IndexOf('static bool Wiederherstellungspunkt(')
            $trockenStart = if ($fnStart -ge 0) { $katalogText.IndexOf('if (k.Trocken)', $fnStart) } else { -1 }
            $trockenEnde = if ($trockenStart -ge 0) { $katalogText.IndexOf('return false;', $trockenStart) } else { -1 }
            $aufruf = $drosselAufrufe[0].Index
            if ($fnStart -lt 0 -or $trockenStart -lt 0 -or $trockenEnde -lt 0 -or -not ($fnStart -lt $trockenStart -and $trockenEnde -lt $aufruf)) {
                $hits += "helfer\Katalog.cs: DrosselAufheben(k) steht nicht hinter dem if (k.Trocken)-Block von Wiederherstellungspunkt (Funktion $fnStart, Weiche $trockenStart, return $trockenEnde, Aufruf $aufruf): der Trockenlauf wuerde SystemRestorePointCreationFrequency schreiben"
            }
        }
        # Lesewerte der Objekte (Adapter, Geraet, Dienste, Firewall, Aufgabe, Run-Wert) je einzeln,
        # damit die Zeile das Wertepaar des Objekts nennt, das sich bewegt hat.
        $lesewerteNachher = Lies-Objektwerte $adapterIndex $geraetId $runWert
        foreach ($k in @($lesewerteVorher.Keys)) {
            $v = "$($lesewerteVorher[$k])"; $n = "$($lesewerteNachher[$k])"
            if ($n -ne $v) { $hits += "Lesewert $k vorher [$v] nachher [$n]: der Trockenlauf hat an diesem Objekt eingegriffen" }
        }
        $lesewerteText = "$($lesewerteVorher.Count) Lesewerte unveraendert"
    }
}
finally {
    foreach ($d in @($planADatei, $planBDatei, $protokollA, $protokollB, $marker)) { if ($d) { Remove-Item $d -ErrorAction SilentlyContinue } }
}
$weggelassenText = ''
if ($weggelassen.Count -gt 0) { $weggelassenText = '; weggelassen: ' + ($weggelassen -join '; ') }
$adapterInfo = 'ohne Adapter'
if ($null -ne $adapterIndex) { $adapterInfo = "Adapter $adapterIndex $adapterText" }
Test-Result "Trockenlauf der M3-Kennungen: Plan A Exit 0 ($($planA.Count) Schritte, $planASekunden s, $adapterInfo), $planBText, kein Kindprozess, keine Sicherungsdatei, 5 Registrierungswerte (SmartScreen, UAC, TRIM, Drossel der Wiederherstellungspunkte) und $lesewerteText ($($m3ImCode.Count) Kennungen zur Laufzeit; $sicherungText$weggelassenText)" ($hits.Count -eq 0) $hits

# 2. Ablehnungen: jede Parameterpruefung einer M3-Kennung hat hier einen Fall, der VOR dem ersten
# Eingriff mit Exit 2 und einer abgelehnt-Zeile enden muss (keine anfang-Zeile, kein Prozess).
# Die Faelle sind die Einschleusungen, die der Helfer abfangen muss: WQL ("10 OR 1=1", a'b),
# Pfade statt Buchstaben, ein Semikolon im Wertnamen (Listentrenner), Anfuehrungszeichen und
# Elternverweise im Aufgabenpfad, ein Defender-Dienst auf Disabled, eine fremde Datei als
# Sicherung (Pfad nach oben, keine .json, nicht vorhanden) und Freigaben, die nicht freischaltbar
# sind (netz.dhcp.erneuern greift in die Verbindung ein, volume.optimieren haengt an keiner Regel).
# --trocken steht dabei, weil die Ablehnung VOR der Trocken-Weiche faellt: kommt ein Fall durch,
# laeuft er trotzdem nur trocken (zweites Netz, die Suite laeuft in der CI erhoeht). Gegenprobe:
# MSSQL$SQLEXPRESS ist ein gueltiger Dienstname ($ erlaubt) und darf NICHT mit 2 enden; ohne den
# Dienst meldet der Schritt das fehlende Objekt (Exit 1), mit ihm laeuft er trocken (Exit 0).
$hits = @()
$m3Faelle = @(
    @{ name = 'netz.dhcp.erneuern adapter "x"'; schritte = @(@{ kennung = 'netz.dhcp.erneuern'; parameter = @{ adapter = 'x' } }) },
    @{ name = 'netz.dhcp.erneuern adapter "10 OR 1=1"'; schritte = @(@{ kennung = 'netz.dhcp.erneuern'; parameter = @{ adapter = '10 OR 1=1' } }) },
    @{ name = 'sicherheit.firewall.einschalten profil "3"'; schritte = @(@{ kennung = 'sicherheit.firewall.einschalten'; parameter = @{ profil = '3' } }) },
    @{ name = 'volume.scan buchstabe "C:\.."'; schritte = @(@{ kennung = 'volume.scan'; parameter = @{ buchstabe = 'C:\..' } }) },
    @{ name = 'volume.scan buchstabe "CD"'; schritte = @(@{ kennung = 'volume.scan'; parameter = @{ buchstabe = 'CD' } }) },
    @{ name = 'leistung.autostart.deaktivieren ort "Foo"'; schritte = @(@{ kennung = 'leistung.autostart.deaktivieren'; parameter = @{ hive = 'HKLM'; ort = 'Foo'; name = 'a' } }) },
    @{ name = 'leistung.autostart.deaktivieren name "a;b"'; schritte = @(@{ kennung = 'leistung.autostart.deaktivieren'; parameter = @{ hive = 'HKLM'; ort = 'Run'; name = 'a;b' } }) },
    @{ name = 'leistung.aufgabe.deaktivieren pfad mit Anfuehrungszeichen'; schritte = @(@{ kennung = 'leistung.aufgabe.deaktivieren'; parameter = @{ pfad = '\A"B' } }) },
    @{ name = 'leistung.aufgabe.deaktivieren pfad "\Fremd\..\Update"'; schritte = @(@{ kennung = 'leistung.aufgabe.deaktivieren'; parameter = @{ pfad = '\Fremd\..\Update' } }) },
    @{ name = 'leistung.aufgabe.deaktivieren pfad unter Windows Defender'; schritte = @(@{ kennung = 'leistung.aufgabe.deaktivieren'; parameter = @{ pfad = '\Microsoft\Windows\Windows Defender\Probe' } }) },
    @{ name = 'leistung.dienst.startart WinDefend Disabled'; schritte = @(@{ kennung = 'leistung.dienst.startart'; parameter = @{ name = 'WinDefend'; startart = 'Disabled' } }) },
    @{ name = "leistung.dienst.startart name a'b"; schritte = @(@{ kennung = 'leistung.dienst.startart'; parameter = @{ name = "a'b"; startart = 'Manual' } }) },
    @{ name = 'update.dienst.aktivieren name Spooler'; schritte = @(@{ kennung = 'update.dienst.aktivieren'; parameter = @{ name = 'Spooler' } }) },
    @{ name = 'update.verbergen updateId "abc"'; schritte = @(@{ kennung = 'update.verbergen'; parameter = @{ updateId = 'abc' } }) },
    @{ name = "geraet.aktivieren instanzId a'b"; schritte = @(@{ kennung = 'geraet.aktivieren'; parameter = @{ instanzId = "a'b" } }) },
    @{ name = 'rueckweg sicherung "..\..\zeitplan.json"'; schritte = @(@{ kennung = 'rueckweg'; parameter = @{ sicherung = '..\..\zeitplan.json' } }) },
    @{ name = 'rueckweg sicherung "hosts"'; schritte = @(@{ kennung = 'rueckweg'; parameter = @{ sicherung = 'hosts' } }) },
    @{ name = 'rueckweg sicherung nicht vorhanden'; schritte = @(@{ kennung = 'rueckweg'; parameter = @{ sicherung = ('probe-gibt-es-nicht-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.json') } }) },
    @{ name = 'wartung.massnahmen freigaben leer'; schritte = @(@{ kennung = 'wartung.massnahmen'; parameter = @{ freigaben = '' } }) },
    @{ name = 'wartung.massnahmen freigaben "foo"'; schritte = @(@{ kennung = 'wartung.massnahmen'; parameter = @{ freigaben = 'foo' } }) },
    @{ name = 'wartung.massnahmen freigaben "wartung.auto"'; schritte = @(@{ kennung = 'wartung.massnahmen'; parameter = @{ freigaben = 'wartung.auto' } }) },
    @{ name = 'wartung.massnahmen freigaben "Bad;Chars"'; schritte = @(@{ kennung = 'wartung.massnahmen'; parameter = @{ freigaben = 'Bad;Chars' } }) },
    @{ name = 'wartung.massnahmen freigaben "netz.dhcp.erneuern"'; schritte = @(@{ kennung = 'wartung.massnahmen'; parameter = @{ freigaben = 'netz.dhcp.erneuern' } }) },
    @{ name = 'wartung.massnahmen freigaben "volume.optimieren"'; schritte = @(@{ kennung = 'wartung.massnahmen'; parameter = @{ freigaben = 'volume.optimieren' } }) }
)
foreach ($fall in $m3Faelle) {
    $planId = 'probe-m3abl-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $protokoll = Join-Path $protokollOrdner ('lauf-' + $planId + '.jsonl')
    $planDatei = $null
    try {
        if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt"; break }
        $planDatei = Schreibe-Plan $planId $fall.schritte
        $lauf = Starte-Helfer @('--helfer', '--plan', ('"' + $planDatei + '"'), '--trocken') $true
        if ($lauf.ExitCode -ne 2) { $hits += "$($fall.name): Exit $($lauf.ExitCode) statt 2" }
        if ($lauf.Kinder.Count -gt 0) { $hits += "$($fall.name): Kindprozesse " + ($lauf.Kinder -join ', ') }
        if (-not (Test-Path $protokoll)) { $hits += "$($fall.name): kein Laufprotokoll $protokoll" }
        else {
            $zeilen = Lies-Laufprotokoll $protokoll
            if (@($zeilen | Where-Object { $_.art -eq 'abgelehnt' }).Count -lt 1) { $hits += "$($fall.name): keine Protokollzeile art=abgelehnt" }
            if (@($zeilen | Where-Object { $_.art -eq 'anfang' }).Count -gt 0) { $hits += "$($fall.name): trotz Ablehnung ist etwas angelaufen (anfang-Zeile)" }
        }
    }
    finally {
        if ($planDatei) { Remove-Item $planDatei -ErrorAction SilentlyContinue }
        Remove-Item $protokoll -ErrorAction SilentlyContinue
    }
}
# Gegenprobe: ein Dienstname mit $ ist gueltig und darf nicht abgelehnt werden.
$gegenText = 'Gegenprobe nicht gelaufen'
$planId = 'probe-m3gegen-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$protokoll = Join-Path $protokollOrdner ('lauf-' + $planId + '.jsonl')
$planDatei = $null
try {
    if (Test-Path $exeApp) {
        $planDatei = Schreibe-Plan $planId @(@{ kennung = 'leistung.dienst.startart'; parameter = @{ name = 'MSSQL$SQLEXPRESS'; startart = 'Manual' } })
        $lauf = Starte-Helfer @('--helfer', '--plan', ('"' + $planDatei + '"'), '--trocken') $true
        $gegenText = 'MSSQL$SQLEXPRESS Exit ' + $lauf.ExitCode
        if ($lauf.ExitCode -ne 0 -and $lauf.ExitCode -ne 1) { $hits += "Gegenprobe MSSQL`$SQLEXPRESS: Exit $($lauf.ExitCode) statt 0 oder 1 (ein Dienstname mit `$ wird abgelehnt)" }
        if ($lauf.Kinder.Count -gt 0) { $hits += "Gegenprobe: Kindprozesse " + ($lauf.Kinder -join ', ') }
    }
}
finally {
    if ($planDatei) { Remove-Item $planDatei -ErrorAction SilentlyContinue }
    Remove-Item $protokoll -ErrorAction SilentlyContinue
}
Test-Result "Helfer lehnt ungueltige M3-Parameter, fremde Sicherungen und nicht freischaltbare Freigaben ab (Exit 2, nichts laeuft; $($m3Faelle.Count) Faelle; $gegenText)" ($hits.Count -eq 0) $hits

# 3. und 4. Sicherungsdatei-Herkunft und Rueckweg-Inhalt, in-process ueber tests\HelferProbe.cs
# (Gruppen sicherung und rueckweg der vor Pruefung 1 uebersetzten Probe $exeM3; Probe 7 oben faehrt
# dieselbe Uebersetzung mit allen Gruppen).
# Warum die Herkunft: der Rueckweg schreibt die Werte aus der Datei erhoeht zurueck (Firewall aus,
# EnableLUA 0, Dienst-Startart). Gaelte jede Datei unter sicherungen\massnahmen, koennte ein
# Benutzer ohne Rechte dort eine Datei ablegen und den erhoehten Helfer damit die Firewall
# abschalten lassen. Deshalb zaehlt nur eine Datei mit Besitzer Administratoren, genau der
# geschuetzten DACL und genau einem Namen; erhoeht (CI) wird das an einer echten Datei des Helfers
# gemessen, nicht erhoeht daran, dass Schreiben wirft und eine Datei des Nutzers abgelehnt wird.
# Warum der Inhalt: auch eine echte Datei kann Werte tragen, die der Helfer nie geschrieben haette
# (ein Dienst aus NieDeaktivieren auf Disabled, FirewallEnabled 2, "Off;calc", 8.8.8.8;evil);
# jeder Wert wird vor dem Eingriff so geprueft wie der Parameter der Hin-Massnahme.
# Erhoeht setzt die Probe voraus, dass der Token Administratoren als Standardbesitzer traegt (jeder
# UAC-erhoehte Token, der CI-Runner); die Probe nennt den Standardbesitzer in ihrer Modus-Zeile.
# Gemessen 18.09.2026: eine erhoehte Shell aus Git-Bash trug das Konto selbst als Standardbesitzer,
# und Schreiben verwarf dort jede Datei. Das ist dann ein Befund gegen Sicherungsdatei.DateiSchreiben
# (setzt den Besitzer nicht selbst), nicht gegen die Probe.
$hitsSicherung = @()
$hitsRueckweg = @()
$sicherungModus = 'Uebersetzung fehlgeschlagen'
try {
    if ($null -eq $exeM3) { $hitsSicherung += $exeM3Fehler; $hitsRueckweg += $exeM3Fehler }
    else {
        $exe = $exeM3
        $ausgabe = @(& $exe sicherung 2>&1 | ForEach-Object { "$_" })
        if ($LASTEXITCODE -ne 0) { $hitsSicherung += "mindestens eine Teilpruefung der Sicherungsdatei ist rot:"; $hitsSicherung += @($ausgabe | Where-Object { $_ -match '\[FEHL\]' } | Select-Object -First 8) }
        $modusZeile = @($ausgabe | Where-Object { $_ -match 'Sicherungsdatei-Probe laeuft' })
        if ($modusZeile.Count -gt 0) { if ($modusZeile[0] -match 'nicht erhoeht') { $sicherungModus = 'nicht erhoeht: Schreiben wirft, Nutzerdatei abgelehnt' } else { $sicherungModus = 'erhoeht: Besitzer, DACL, Hardlink an einer echten Datei' } }
        else { $hitsSicherung += "Die Sicherungsdatei-Probe ist nicht gelaufen (keine Modus-Zeile in der Ausgabe)" }
        $anzahl = @($ausgabe | Where-Object { $_ -match '^\s+\[(ok|FEHL)\]' }).Count
        if ($anzahl -lt 10) { $hitsSicherung += "Nur $anzahl Teilpruefungen der Sicherungsdatei gelaufen (erwartet mindestens 10)" }

        $ausgabe = @(& $exe rueckweg 2>&1 | ForEach-Object { "$_" })
        if ($LASTEXITCODE -ne 0) { $hitsRueckweg += "mindestens eine Teilpruefung des Rueckweg-Inhalts ist rot:"; $hitsRueckweg += @($ausgabe | Where-Object { $_ -match '\[FEHL\]' } | Select-Object -First 8) }
        $anzahl = @($ausgabe | Where-Object { $_ -match '^\s+\[(ok|FEHL)\]' }).Count
        if ($anzahl -lt 30) { $hitsRueckweg += "Nur $anzahl Teilpruefungen des Rueckweg-Inhalts gelaufen (erwartet mindestens 30)" }
    }
}
catch { $hitsSicherung += "Helferprobe: " + $_.Exception.Message; $hitsRueckweg += "Helferprobe: " + $_.Exception.Message }
finally { if ($exeM3) { Remove-Item $exeM3 -ErrorAction SilentlyContinue }; $exeM3 = $null }
Test-Result "Sicherungsdatei: nur Besitzer Administratoren, genau die geschuetzte DACL, ein Name; Namen mit Pfadzeichen abgelehnt; Trockenlauf schreibt nichts ($sicherungModus)" ($hitsSicherung.Count -eq 0) $hitsSicherung
Test-Result "Rueckweg-Inhalt: untergeschobene Werte (Dienst Disabled, FirewallEnabled 2, Off;calc, EnableLUA 7, 8.8.8.8;evil, a'b) werden abgelehnt, gueltige angenommen" ($hitsRueckweg.Count -eq 0) $hitsRueckweg

# 5. Kein PowerShell und kein eigener Prozessstart in helfer\Massnahmen\: die Massnahmen arbeiten
# ueber WMI, COM und die Registrierung im eigenen Prozess (Konzept 3.8: kein Skriptprozess, den
# Defender als ClickFix-Muster sperrt); die zwei Werkzeuge, die es brauchen (schtasks, MpCmdRun),
# laufen ueber Werkzeuge.Lauf/Schritt mit Zeitgrenze und Job-Objekt. Kommentare zaehlen nicht,
# Zeichenketten schon (ein "powershell" im Literal waere ein Aufruf).
$hits = @()
$kommentarRx = [regex]'^((?:[^"/]|"(?:[^"\\]|\\.)*"|/(?!/))*)//'
foreach ($f in (Get-ChildItem $m3Ordner -File -Filter *.cs)) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*(///|//)') { continue }
        $code = $line
        $mk = $kommentarRx.Match($line)
        if ($mk.Success) { $code = $mk.Groups[1].Value }
        if ($code -match '(?i)powershell|pwsh|cmd\.exe|-EncodedCommand|Invoke-Expression') { $hits += "$($f.Name):$i  $($line.Trim())" }
        if ($code -match 'Process\.Start|new Process\b|ProcessStartInfo|ShellExecute|CreateProcess') { $hits += "$($f.Name):$i  $($line.Trim())" }
    }
}
Test-Result "Kein PowerShell und kein Process.Start in helfer\Massnahmen\ (Prozesse nur ueber Werkzeuge.Lauf/Schritt)" ($hits.Count -eq 0) $hits

# 6. Werkzeugkasten, ersetzte Eintraege: genau drei Werkzeuge (DNS-Cache, DHCP, Schnellscan) sind
# durch eine Massnahme an der Karte ersetzt (ErsetztDurch); jede genannte Kennung muss im
# Kern-Katalog stehen, und der Host filtert sie beim Senden des Katalogs (Vertrag 0.6, 9.3). Ein
# ErsetztDurch auf eine Kennung, die es nicht gibt, liesse den Eintrag verschwinden, ohne dass eine
# Massnahme ihn ersetzt.
$hits = @()
$aktionText = [IO.File]::ReadAllText((Join-Path $root 'src\ActionCatalog.cs'), [Text.Encoding]::UTF8)
$kernKatalogText = [IO.File]::ReadAllText((Join-Path $root 'kern\Massnahmen.Katalog.cs'), [Text.Encoding]::UTF8)
$ersetzt = @([regex]::Matches($aktionText, 'ErsetztDurch\s*=\s*"([a-z.]+)"') | ForEach-Object { $_.Groups[1].Value })
if ($ersetzt.Count -ne 3) { $hits += "src\ActionCatalog.cs hat $($ersetzt.Count) ErsetztDurch-Eintraege statt 3: " + ($ersetzt -join ', ') }
foreach ($k in $ersetzt) {
    if ($kernKatalogText -notmatch ('Kennung\s*=\s*"' + [regex]::Escape($k) + '"')) { $hits += "ErsetztDurch = '$k', aber kern\Massnahmen.Katalog.cs kennt diese Kennung nicht" }
}
if ($shellText3 -notmatch 'string\.IsNullOrEmpty\(a\.ErsetztDurch\)') { $hits += "host\ShellForm.cs filtert ersetzte Werkzeuge nicht mehr mit string.IsNullOrEmpty(a.ErsetztDurch)" }
Test-Result "Werkzeugkasten: genau 3 Eintraege durch Massnahmen ersetzt, jede Kennung im Kern-Katalog, Host filtert sie" ($hits.Count -eq 0) $hits

# 7. Nachrichten der Phase 2 woertlich (Vertrag 9.1, 9.2): die dynamischen Pruefungen oben decken
# "jede Host-Nachricht hat einen case-Zweig" und "jeder Host-Befehl ist erreichbar" ab, aber eine
# Umbenennung auf beiden Seiten liefe dort still durch. Hier stehen die Namen fest.
$hits = @()
$hostM3 = ($csFiles | Where-Object { $_.DirectoryName -like '*\host' } | ForEach-Object { [IO.File]::ReadAllText($_.FullName, [Text.Encoding]::UTF8) }) -join "`n"
foreach ($n in @('massnahmeVorschau', 'freigaben')) {
    if ($hostM3 -notmatch ('type\s*=\s*"' + $n + '"')) { $hits += "Der Host sendet keine Nachricht type = ""$n""" }
    if ($jsText2 -notmatch ("case\s+'" + $n + "'")) { $hits += "ui\app.js hat keinen Zweig case '$n'" }
}
foreach ($b in @('massnahmeAusfuehren', 'massnahmeRueckweg', 'massnahmeAnsicht', 'freigabenSetzen')) {
    if ($shellText3 -notmatch ('type == "' + $b + '"')) { $hits += "host\ShellForm.cs versteht den Befehl ""$b"" nicht" }
    if ($jsText2 -notmatch ("'" + $b + "'")) { $hits += "ui\app.js sendet '$b' nirgends" }
}
foreach ($m in @('massnahme', 'rueckweg')) {
    if ($hostM3 -notmatch ('mode\s*=\s*"' + $m + '"')) { $hits += "Der Host startet keinen Ablauf mit mode = ""$m""" }
    if ($jsText2 -notmatch ("mode === '" + $m + "'")) { $hits += "ui\app.js wertet mode === '$m' nirgends aus" }
}
Test-Result "Nachrichten der Massnahmen stehen woertlich auf beiden Seiten (massnahmeVorschau, freigaben, massnahmeAusfuehren, massnahmeRueckweg, massnahmeAnsicht, freigabenSetzen)" ($hits.Count -eq 0) $hits

# 8. Freischaltbare Massnahmen: genau sechs, und volume.optimieren nicht darunter (es haengt an
# keiner Regel; ein Kaestchen in den Einstellungen, das nie feuert, waere eine Luege, Vertrag 8.1).
# Die Kernprobe (d) prueft die Zahl ueber den Katalog im Speicher; hier steht die Menge woertlich.
$hits = @()
$freiErwartet = @('netz.dnscache.leeren', 'sicherheit.signaturen.aktualisieren', 'sicherheit.schnellscan',
                  'sicherheit.firewall.einschalten', 'systemschutz.punkt.anlegen', 'volume.scan')
$freiZeilen = @([IO.File]::ReadAllLines((Join-Path $root 'kern\Massnahmen.Katalog.cs'), [Text.Encoding]::UTF8) | Where-Object { $_ -match 'Freischaltbar\s*=\s*true' })
if ($freiZeilen.Count -ne 6) { $hits += "kern\Massnahmen.Katalog.cs hat $($freiZeilen.Count) Zeilen mit Freischaltbar = true statt 6" }
$freiKennungen = @()
foreach ($z in $freiZeilen) {
    $mk = [regex]::Match($z, 'Kennung\s*=\s*"([a-z.]+)"')
    if ($mk.Success) { $freiKennungen += $mk.Groups[1].Value } else { $hits += "Freischaltbar = true ohne Kennung auf derselben Zeile: $($z.Trim())" }
}
foreach ($k in $freiKennungen) { if ($freiErwartet -notcontains $k) { $hits += "'$k' ist freischaltbar, steht aber nicht in der Sechser-Liste" } }
foreach ($k in $freiErwartet) { if ($freiKennungen -notcontains $k) { $hits += "'$k' ist nicht mehr freischaltbar" } }
if ($freiKennungen -contains 'volume.optimieren') { $hits += "volume.optimieren ist freischaltbar, haengt aber an keiner Regel" }
if ($kernKatalogText -notmatch 'Kennung\s*=\s*"volume\.optimieren"[^\r\n]*Freischaltbar\s*=\s*false') { $hits += "volume.optimieren traegt nicht Freischaltbar = false auf der Kennungszeile" }
Test-Result "Freischaltbar sind genau sechs Massnahmen, volume.optimieren nicht darunter" ($hits.Count -eq 0) $hits

# 9. CI live (nur erhoeht): netz.dnscache.leeren ohne --trocken. Die einzige M3-Massnahme, die hier
# echt laeuft: MSFT_DNSClientCache.Clear() ist WMI (kein Kindprozess), aendert keine Einstellung,
# der Zwischenspeicher fuellt sich sofort wieder. Erwartet: Exit 0, eine nachweis-Zeile mit
# bestanden = true (der Rueckgabewert 0 ist der Beleg, Vertrag Abschnitt 1), kein Kindprozess;
# das Laufprotokoll wird danach entfernt. Nicht erhoeht: uebersprungen, mit Zeile, zaehlt trotzdem.
# Erhoehung ueber die Rolle (WindowsBuiltInRole.Administrator), nicht ueber den Namen: IsInRole
# ("Administrator") vergleicht mit einem Gruppennamen, den es auf einem deutschen Windows
# ("Administratoren") nicht gibt, und liefert dort in einer erhoehten Shell false.
# Gemessen 18.09.2026 auf Windows 11 26200: ManagementClass.InvokeMethod("Clear") liefert fuer
# MSFT_DNSClientCache null (kein Ausgabeobjekt, obwohl die Klasse UInt32 Clear() deklariert), und
# helfer\Massnahmen\Netz.cs meldet dann "kein Beleg" mit Exit 1. Bleibt diese Pruefung rot, ist
# das ein Befund gegen den Beleg in Netz.cs (Rueckgabewert als einziger Nachweis), nicht gegen die Probe.
$hits = @()
$liveText = 'uebersprungen (nicht erhoeht)'
$erhoehtLive = $false
try { $erhoehtLive = (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) } catch { }
if ($erhoehtLive) {
    $planId = 'probe-m3live-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $protokoll = Join-Path $protokollOrdner ('lauf-' + $planId + '.jsonl')
    $planDatei = $null
    try {
        if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt" }
        else {
            $planDatei = Schreibe-Plan $planId @(@{ kennung = 'netz.dnscache.leeren'; parameter = @{} })
            $lauf = Starte-Helfer @('--helfer', '--plan', ('"' + $planDatei + '"')) $true
            if ($null -eq $lauf.ExitCode) { $hits += "ExitCode nicht lesbar" }
            elseif ($lauf.ExitCode -ne 0) { $hits += "netz.dnscache.leeren live: Exit $($lauf.ExitCode) statt 0 (Grund in $protokoll und logs\app.log)" }
            if ($lauf.Kinder.Count -gt 0) { $hits += "netz.dnscache.leeren hat Kindprozesse gestartet: " + ($lauf.Kinder -join ', ') }
            if (-not (Test-Path $protokoll)) { $hits += "Laufprotokoll fehlt: $protokoll" }
            else {
                $zeilen = Lies-Laufprotokoll $protokoll
                $nw = @($zeilen | Where-Object { $_.art -eq 'nachweis' -and $_.kennung -eq 'netz.dnscache.leeren' })
                if ($nw.Count -lt 1) { $hits += "keine nachweis-Zeile fuer netz.dnscache.leeren" }
                elseif ($nw[0].fachmann.bestanden -ne $true) { $hits += "nachweis-Zeile ohne bestanden=true: " + $nw[0].laie }
                elseif ($nw[0].fachmann.trocken) { $hits += "die nachweis-Zeile traegt trocken=true, obwohl der Lauf echt war" }
                $liveText = 'live erhoeht: Exit ' + $lauf.ExitCode + ', ' + $lauf.Sekunden + ' s'
            }
        }
    }
    catch { $hits += "Live-Probe: " + $_.Exception.Message }
    finally {
        if ($planDatei) { Remove-Item $planDatei -ErrorAction SilentlyContinue }
        Remove-Item $protokoll -ErrorAction SilentlyContinue
        if (Test-Path $protokoll) { $hits += "Das Laufprotokoll liess sich nicht entfernen: $protokoll" }
    }
}
Test-Result "CI live: netz.dnscache.leeren ohne --trocken meldet Exit 0 und einen bestandenen Nachweis, ohne Kindprozess ($liveText)" ($hits.Count -eq 0) $hits

Write-Host "`nv8.3: Stufe 3" -ForegroundColor Cyan

# Meilenstein 4 (docs\M4-ENTWURF.md, Abschnitte 1, 3, 8.2 und 10.5): Stufe-3-Massnahmen an der Karte
# (nie ohne Haekchen), DISM ueber die Windows-Schnittstelle statt Konsolentext, das Programm-Update als
# Plan ueber den Helfer statt einer Batch per runas, die Techniker-Ansicht, Exporte unter
# sicherungen\massnahmen\export mit Herkunft am Handle. Live laeuft hier NICHTS aus Stufe 3: jede
# Kennung faehrt nur --trocken, die Herkunft und der Rueckweg-Inhalt laufen in-process gegen einen
# Wegwerf-Ordner (Helferprobe), der Rest sind Greps und die Modulliste eines Programmstarts.

# 1. Trockenlauf der M4-Kennungen. Die Laufzeitmenge und die feste Liste prueft Pruefung 1 der Gruppe
# v8.2 (dort steht seit 8.3 auch jede M4-Kennung). Plan A: alle Stufe-3-Kennungen und die neuen
# Stufe-2-Kennungen mit maschinenunabhaengigen Parametern. Jede Vorbedingung, die der Helfer aus dem
# Systemzustand liest, wird vorher mit DEMSELBEN Kriterium gelesen, sonst misst die Probe den Rechner
# statt den Trockenlauf: eine registrierte Fremd-Firewall (SecurityCenter2 FirewallProduct, Namen wie
# Kern.Regeln.Sicherheit.IstWindowsFirewallName) laesst sicherheit.firewall.zuruecksetzen weg (der Helfer
# lehnt sonst mit Exit 2 ab, ResetPruefen); wuauserv auf Disabled laesst update.zuruecksetzen weg
# (Vorbedingung, Exit 1); C: muss ein festes MSFT_Volume sein (VolumeLesen); wiederherstellungspunkt.zurueck
# braucht eine vorhandene Folgenummer (PunktVorhanden, sonst Exit 1), gelesen aus root\default
# SystemRestore, ohne lesbare Liste faellt der Schritt weg. Erwartet: Exit 0 in unter 180 s, je Kennung
# eine schritt-Zeile, kein Kindprozess (der Trockenlauf startet weder netsh noch pnputil, bcdedit, sfc
# und initialisiert keine DISM-Sitzung), keine Datei unter sicherungen\massnahmen und export\, kein
# bestandener Nachweis, und die Lesewerte vorher == nachher: SHA-256 der hosts-Datei, Winlogon Shell
# und Userinit, DefaultInboundAction/DefaultOutboundAction/Enabled der drei Firewall-Profile,
# SoftwareDistribution und catroot2 (vorhanden, kein .old neu entstanden), StartMode von wuauserv, bits
# und cryptsvc, bcdedit /enum {bootmgr} (bootsequence), die Zahl der Papierkorb-Eintraege
# (Shell.Application, Namespace 10), das Dirty-Bit von C: (fsutil dirty query) und BootExecute
# (Session Manager): die zwei letzten sind das Einzige, was Repair(OfflineScanAndFix=true) fuer
# volume.spotfix und volume.reparatur auf dem Windows-Laufwerk hinterlaesst (kein Kindprozess,
# keine Sicherung, unter 1 s). Plan B: geraet.treiber.neu mit einem Geraet, dessen Problemcode
# weder 0 noch 22 ist (sonst irgendein Code ungleich 0, sonst der Ersatz PCI\VEN_0000&DEV_0000\0):
# Exit 0 oder 1, nie 2 (2 hiesse: ein gueltiger Parameter wurde abgelehnt), kein Kindprozess.
$hits = @()
$weggelassen = @()
$exportOrdner = Join-Path $sicherungenOrdner 'export'
function Zaehle-Exporte { return @(Get-ChildItem $exportOrdner -Force -ErrorAction SilentlyContinue).Count }
# Lesewerte der Objekte, an denen Plan A ansetzt (nur lesen; ein fehlender Wert liest sich als "(fehlt)").
function Lies-M4Werte {
    $werte = [ordered]@{}
    $hosts = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
    $h = '(fehlt)'
    try { if (Test-Path $hosts) { $h = (Get-FileHash -Path $hosts -Algorithm SHA256 -ErrorAction Stop).Hash } } catch { }
    $werte['hosts.sha256'] = $h
    foreach ($n in @('Shell', 'Userinit')) {
        $v = '(fehlt)'
        try { $v = "$((Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' -Name $n -ErrorAction Stop).$n)" } catch { }
        $werte["Winlogon.$n"] = $v
    }
    foreach ($p in @('Domain', 'Private', 'Public')) {
        $v = '(fehlt)'
        try { $fp = Get-NetFirewallProfile -Name $p -ErrorAction Stop; $v = "$($fp.Enabled)/$($fp.DefaultInboundAction)/$($fp.DefaultOutboundAction)" } catch { }
        $werte["Firewall.$p"] = $v
    }
    foreach ($o in @('SoftwareDistribution', 'System32\catroot2')) {
        $pfad = Join-Path $env:SystemRoot $o
        $werte["Ordner.$o"] = "$(Test-Path $pfad)/$(Test-Path ($pfad + '.old'))"
    }
    foreach ($n in @('wuauserv', 'bits', 'cryptsvc')) {
        $art = '(fehlt)'
        try { $d = Get-CimInstance Win32_Service -Filter "Name = '$n'" -ErrorAction Stop | Select-Object -First 1; if ($null -ne $d) { $art = "$($d.StartMode)" } } catch { }
        $werte["$n.StartMode"] = $art
    }
    $bcd = '(fehlt)'
    try { $bcd = ((& (Join-Path $env:SystemRoot 'System32\bcdedit.exe') /enum '{bootmgr}' 2>&1 | ForEach-Object { "$_" }) -join '|') } catch { }
    if ($bcd -match 'bootsequence') { $werte['bcd.bootsequence'] = 'gesetzt' } else { $werte['bcd.bootsequence'] = 'nicht gesetzt' }
    $werte['bcd.bootmgr'] = $bcd.GetHashCode()
    $pk = '(fehlt)'
    try { $sh = New-Object -ComObject Shell.Application; $pk = "$($sh.NameSpace(10).Items().Count)" } catch { }
    $werte['Papierkorb.Eintraege'] = $pk
    # M4 (Phase 3, T1): volume.spotfix und volume.reparatur auf dem Windows-Laufwerk rufen echt
    # MSFT_Volume.Repair(OfflineScanAndFix=true), also ohne Kindprozess, ohne Sicherung, ohne Export,
    # in unter 1 s; sichtbar wird das nur am Dirty-Bit (fsutil dirty query C:, Ausgabe als ganze
    # Zeile, in jeder Sprache) und an BootExecute (Session Manager, REG_MULTI_SZ, mit | verbunden).
    $dirty = '(fehlt)'
    try { $dirty = ((& (Join-Path $env:SystemRoot 'System32\fsutil.exe') dirty query 'C:' 2>&1 | ForEach-Object { "$_".Trim() } | Where-Object { $_.Length -gt 0 }) -join '|') } catch { }
    if ($dirty.Length -eq 0) { $dirty = '(leer)' }
    $werte['C.DirtyBit'] = $dirty
    $be = '(fehlt)'
    try { $be = (@((Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name 'BootExecute' -ErrorAction Stop).BootExecute) | ForEach-Object { "$_" }) -join '|' } catch { }
    $werte['SessionManager.BootExecute'] = $be
    return $werte
}
# Fremd-Firewall wie FremdFirewall in helfer\Massnahmen\Sicherheit.cs: SecurityCenter2 FirewallProduct
# ohne die Windows-Namen (Kern.Regeln.Sicherheit.IstWindowsFirewallName).
$fremdFirewall = $null
try {
    foreach ($fw in @(Get-CimInstance -Namespace 'root\SecurityCenter2' -ClassName FirewallProduct -ErrorAction Stop)) {
        $n = "$($fw.displayName)".Trim()
        if ($n.Length -eq 0) { continue }
        if ($n -match '^(Windows Defender|Microsoft Defender|Windows-Firewall|Windows Firewall)') { continue }
        if ($null -eq $fremdFirewall) { $fremdFirewall = $n }
    }
} catch { }
# wuauserv wie die Vorbedingung von update.zuruecksetzen (nicht Disabled).
$wuauservArt = '(fehlt)'
try { $d = Get-CimInstance Win32_Service -Filter "Name = 'wuauserv'" -ErrorAction Stop | Select-Object -First 1; if ($null -ne $d) { $wuauservArt = "$($d.StartMode)" } } catch { }
# Folgenummer eines vorhandenen Wiederherstellungspunkts (root\default SystemRestore; null = nicht lesbar).
$punktFolge = $null
try {
    $punkte = @(Get-CimInstance -Namespace 'root\default' -ClassName SystemRestore -ErrorAction Stop | Sort-Object SequenceNumber)
    if ($punkte.Count -gt 0) { $punktFolge = [int]$punkte[0].SequenceNumber }
} catch { }
# C: wie VolumeLesen (MSFT_Volume, DriveType 3); $volumeC stammt aus der Gruppe v8.2.
$planA4 = @(@{ kennung = 'netz.hosts.bereinigen'; parameter = @{} },
            @{ kennung = 'netz.winsock.zuruecksetzen'; parameter = @{} },
            @{ kennung = 'netz.ip.zuruecksetzen'; parameter = @{} },
            @{ kennung = 'sicherheit.firewall.eingehend.blockieren'; parameter = @{ profil = '2' } })
if ($null -eq $fremdFirewall) { $planA4 += @{ kennung = 'sicherheit.firewall.zuruecksetzen'; parameter = @{} } }
else { $weggelassen += "sicherheit.firewall.zuruecksetzen (Fremd-Firewall '$fremdFirewall' registriert)" }
if ($null -ne $volumeC) {
    $planA4 += @{ kennung = 'volume.spotfix'; parameter = @{ buchstabe = 'C' } }
    $planA4 += @{ kennung = 'volume.reparatur'; parameter = @{ buchstabe = 'C' } }
} else { $weggelassen += "volume.spotfix und volume.reparatur ($volumeCGrund)" }
$planA4 += @(@{ kennung = 'dateien.reparieren'; parameter = @{} },
             @{ kennung = 'leistung.winlogon.zuruecksetzen'; parameter = @{} },
             @{ kennung = 'speicher.papierkorb.leeren'; parameter = @{} },
             @{ kennung = 'stabilitaet.speichertest.planen'; parameter = @{} })
# update.zuruecksetzen liest auch trocken die verborgenen Updates (WUA Search IsHidden=1, Online=false,
# VerborgeneLesen); scheitert die Suche, meldet der Schritt ein Problem (Exit 1). Deshalb dieselbe Suche
# vorher in einem eigenen Prozess (hoechstens 60 s): geht sie nicht, faellt der Schritt weg.
$wuaVerborgen = $null
if ($wuauservArt -ne 'Disabled') {
    $wuaJob4 = $null
    try {
        $wuaJob4 = Start-Job -ScriptBlock {
            $s = (New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher()
            $s.Online = $false
            "$($s.Search('IsHidden=1').Updates.Count)"
        }
        if (Wait-Job $wuaJob4 -Timeout 60) { $wuaErg4 = "$(Receive-Job $wuaJob4 -ErrorAction SilentlyContinue)".Trim(); if ($wuaErg4 -match '^\d+$') { $wuaVerborgen = [int]$wuaErg4 } }
    } catch { }
    finally { if ($wuaJob4) { Stop-Job $wuaJob4 -ErrorAction SilentlyContinue; Remove-Job $wuaJob4 -Force -ErrorAction SilentlyContinue } }
}
if ($wuauservArt -eq 'Disabled') { $weggelassen += 'update.zuruecksetzen (wuauserv ist Disabled, Vorbedingung des Helfers)' }
elseif ($null -eq $wuaVerborgen) { $weggelassen += 'update.zuruecksetzen (WUA-Suche IsHidden=1 lieferte binnen 60 s kein Ergebnis)' }
else { $planA4 += @{ kennung = 'update.zuruecksetzen'; parameter = @{} } }
if ($null -ne $punktFolge) { $planA4 += @{ kennung = 'wiederherstellungspunkt.zurueck'; parameter = @{ folge = "$punktFolge" } } }
else { $weggelassen += 'wiederherstellungspunkt.zurueck (Liste der Wiederherstellungspunkte nicht lesbar oder leer)' }
# Plan B: ein Geraet mit Problemcode weder 0 noch 22 (sonst ungleich 0), Instanz-Id aus der Zeichenmenge des Kerns.
$treiberId = 'PCI\VEN_0000&DEV_0000\0'
$treiberText = 'Ersatz-Id'
try {
    $alleG = @(Get-CimInstance Win32_PnPEntity -Filter 'ConfigManagerErrorCode <> 0' -ErrorAction Stop |
               Where-Object { "$($_.PNPDeviceID)" -match '^[A-Za-z0-9\\&_{}.#()+,=@~!\- ]{1,400}$' -and "$($_.PNPDeviceID)" -notmatch '\\\\' })
    $g = @($alleG | Where-Object { [int]$_.ConfigManagerErrorCode -ne 22 })
    if ($g.Count -eq 0) { $g = $alleG }
    if ($g.Count -gt 0) { $treiberId = "$($g[0].PNPDeviceID)"; $treiberText = "Geraet mit Code $($g[0].ConfigManagerErrorCode)" }
} catch { }
$planB4 = @(@{ kennung = 'geraet.treiber.neu'; parameter = @{ instanzId = $treiberId } })
$planA4Sekunden = '?'
$planB4Text = ''
$m4Lesewerte = 'Lesewerte nicht gemessen'
$planA4Datei = $null; $planB4Datei = $null; $protokollA4 = $null; $protokollB4 = $null
try {
    if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt - vorher build.ps1 laufen lassen" }
    else {
        $dateienVorher = Zaehle-Sicherungen
        $exporteVorher = Zaehle-Exporte
        $werteVorher = Lies-M4Werte

        # ---- Plan A ----
        $planId = 'probe-m4a-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
        $protokollA4 = Join-Path $protokollOrdner ('lauf-' + $planId + '.jsonl')
        $planA4Datei = Schreibe-Plan $planId $planA4
        $lauf = Starte-Helfer @('--helfer', '--plan', ('"' + $planA4Datei + '"'), '--trocken') $true
        $planA4Sekunden = $lauf.Sekunden
        if ($null -eq $lauf.ExitCode) { $hits += "Plan A: ExitCode des Helfers nicht lesbar (Prozess-Handle nicht gebunden)" }
        elseif ($lauf.ExitCode -ne 0) {
            $hits += "Plan A: Trockenlauf endete mit Exit $($lauf.ExitCode) statt 0 (Plan $planId, $($planA4.Count) Schritte; Grund aus $protokollA4 und logs\app.log):"
            $hits += Lies-Laufgrund $protokollA4 $planId 'Plan A:'
        }
        if ($lauf.Kinder.Count -gt 0) { $hits += "Plan A: der Trockenlauf hat Kindprozesse gestartet: " + ($lauf.Kinder -join ', ') }
        if ($lauf.Sekunden -ge 180) { $hits += "Plan A: der Trockenlauf brauchte $($lauf.Sekunden) s (Grenze 180 s): ein Schritt hat vermutlich echt gearbeitet" }
        if (-not (Test-Path $protokollA4)) { $hits += "Plan A: Laufprotokoll fehlt: $protokollA4" }
        else {
            $zeilen = Lies-Laufprotokoll $protokollA4
            if (@($zeilen | Where-Object { $_.art -eq 'abgelehnt' }).Count -gt 0) {
                $hits += "Plan A: der Plan wurde vor dem ersten Schritt abgelehnt, keine der $($planA4.Count) Kennungen ist gelaufen"
            }
            else {
                foreach ($s in $planA4) {
                    $k = $s.kennung
                    $da = @($zeilen | Where-Object { $_.art -eq 'schritt' -and $_.kennung -eq $k })
                    if ($da.Count -lt 1) { $hits += "Plan A: keine schritt-Zeile fuer '$k' im Laufprotokoll" }
                    elseif ($lauf.ExitCode -ne 0) {
                        foreach ($z in $da) {
                            if (-not $z.fachmann.problem) { continue }
                            $warum = @($zeilen | Where-Object { $_.kennung -eq $k -and ($_.art -eq 'fehler' -or ($_.art -eq 'nachweis' -and $_.fachmann.bestanden -eq $false)) } | ForEach-Object { $_.laie })
                            if ($warum.Count -eq 0) { $warum = @('ohne nachweis- oder fehler-Zeile; der Satz steht als [bad]-Zeile in app.log') }
                            $hits += "Plan A: '$k' meldete ein Problem: " + ($warum -join ' | ')
                        }
                    }
                }
                foreach ($z in @($zeilen | Where-Object { $_.art -eq 'nachweis' })) {
                    if ($z.fachmann.bestanden -eq $true) { $hits += "Plan A: '$($z.kennung)' meldet im Trockenlauf einen bestandenen Nachweis: " + $z.laie }
                }
                $ende = @($zeilen | Where-Object { $_.art -eq 'ende' -and $_.kennung -eq 'plan' })
                if ($ende.Count -ne 1) { $hits += "Plan A: keine ende-Zeile des Plans ($($ende.Count))" }
                elseif (-not $ende[0].fachmann.trocken) { $hits += "Plan A: die ende-Zeile traegt nicht trocken=true" }
            }
        }

        # ---- Plan B ----
        $planId = 'probe-m4b-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
        $protokollB4 = Join-Path $protokollOrdner ('lauf-' + $planId + '.jsonl')
        $planB4Datei = Schreibe-Plan $planId $planB4
        $laufB = Starte-Helfer @('--helfer', '--plan', ('"' + $planB4Datei + '"'), '--trocken') $true
        if ($null -eq $laufB.ExitCode) { $hits += "Plan B: ExitCode des Helfers nicht lesbar" }
        elseif ($laufB.ExitCode -ne 0 -and $laufB.ExitCode -ne 1) {
            $hits += "Plan B: Exit $($laufB.ExitCode) statt 0 oder 1 (2 = ein gueltiger Parameter wurde abgelehnt; Plan $planId; Grund aus $protokollB4 und logs\app.log):"
            $hits += Lies-Laufgrund $protokollB4 $planId 'Plan B:'
        }
        if ($laufB.Kinder.Count -gt 0) { $hits += "Plan B: Kindprozesse " + ($laufB.Kinder -join ', ') }
        $planB4Text = "Plan B Exit $($laufB.ExitCode) ($treiberText)"
        if (-not (Test-Path $protokollB4)) { $hits += "Plan B: Laufprotokoll fehlt: $protokollB4" }
        else {
            $zeilen = Lies-Laufprotokoll $protokollB4
            if (@($zeilen | Where-Object { $_.art -eq 'abgelehnt' }).Count -gt 0) { $hits += "Plan B: der Plan wurde vor dem ersten Schritt abgelehnt" }
            else {
                $da = @($zeilen | Where-Object { $_.art -eq 'schritt' -and $_.kennung -eq 'geraet.treiber.neu' })
                if ($da.Count -lt 1) { $hits += "Plan B: keine schritt-Zeile fuer 'geraet.treiber.neu'" }
                elseif ($laufB.ExitCode -eq 1 -and -not $da[0].fachmann.problem) { $hits += "Plan B: Exit 1, aber die schritt-Zeile nennt kein Problem" }
                foreach ($z in @($zeilen | Where-Object { $_.art -eq 'nachweis' })) {
                    if ($z.fachmann.bestanden -eq $true) { $hits += "Plan B: '$($z.kennung)' meldet im Trockenlauf einen bestandenen Nachweis: " + $z.laie }
                }
            }
        }

        # ---- Nachher ----
        $dateienNachher = Zaehle-Sicherungen
        if ($dateienNachher -ne $dateienVorher) { $hits += "Unter $sicherungenOrdner liegen nachher $dateienNachher statt $dateienVorher Dateien: der Trockenlauf hat eine Sicherung geschrieben" }
        $exporteNachher = Zaehle-Exporte
        if ($exporteNachher -ne $exporteVorher) { $hits += "Unter $exportOrdner liegen nachher $exporteNachher statt $exporteVorher Eintraege: der Trockenlauf hat exportiert" }
        $werteNachher = Lies-M4Werte
        foreach ($k in @($werteVorher.Keys)) {
            $v = "$($werteVorher[$k])"; $n = "$($werteNachher[$k])"
            if ($n -ne $v) { $hits += "Lesewert $k vorher [$v] nachher [$n]: der Trockenlauf hat an diesem Objekt eingegriffen" }
        }
        $m4Lesewerte = "$($werteVorher.Count) Lesewerte unveraendert"
    }
}
finally {
    foreach ($d in @($planA4Datei, $planB4Datei, $protokollA4, $protokollB4)) { if ($d) { Remove-Item $d -ErrorAction SilentlyContinue } }
}
$weggelassenText = ''
if ($weggelassen.Count -gt 0) { $weggelassenText = '; weggelassen: ' + ($weggelassen -join '; ') }
Test-Result "Trockenlauf der M4-Kennungen: Plan A Exit 0 ($($planA4.Count) Schritte, $planA4Sekunden s), $planB4Text, kein Kindprozess, keine Sicherung, kein Export, $m4Lesewerte (hosts, Winlogon, Firewall, Update-Ordner, Dienste, bcdedit, Papierkorb, Dirty-Bit, BootExecute)$weggelassenText" ($hits.Count -eq 0) $hits

# 2. Ablehnungen (Exit 2, abgelehnt-Zeile, kein Prozess): die Parameterpruefungen der M4-Kennungen.
# Die Faelle, die eine untergeschobene Sicherungsdatei brauchen (rueckweg mit export=..\hosts,
# export=x.wfw, hosts-Inhalt mit NUL, IP=evil), laufen in-process in der Helferprobe (Pruefung 4),
# weil eine echte Datei unter dem geschuetzten Ordner ein Eingriff waere. Gegenprobe: eine
# Instanz-Id mit Leerzeichen und Klammern ist gueltig und darf NICHT mit 2 enden.
$hits = @()
$m4Faelle = @(
    @{ name = 'sicherheit.firewall.eingehend.blockieren profil "3"'; schritte = @(@{ kennung = 'sicherheit.firewall.eingehend.blockieren'; parameter = @{ profil = '3' } }) },
    @{ name = 'sicherheit.firewall.eingehend.blockieren ohne profil'; schritte = @(@{ kennung = 'sicherheit.firewall.eingehend.blockieren'; parameter = @{} }) },
    @{ name = 'volume.spotfix buchstabe "CD"'; schritte = @(@{ kennung = 'volume.spotfix'; parameter = @{ buchstabe = 'CD' } }) },
    @{ name = 'volume.reparatur buchstabe "C:\.."'; schritte = @(@{ kennung = 'volume.reparatur'; parameter = @{ buchstabe = 'C:\..' } }) },
    @{ name = "geraet.treiber.neu instanzId a'b"; schritte = @(@{ kennung = 'geraet.treiber.neu'; parameter = @{ instanzId = "a'b" } }) },
    @{ name = 'geraet.treiber.neu ohne instanzId'; schritte = @(@{ kennung = 'geraet.treiber.neu'; parameter = @{} }) },
    @{ name = 'update.anwenden sha256 "abc"'; schritte = @(@{ kennung = 'update.anwenden'; parameter = @{ sha256 = 'abc' } }) },
    @{ name = 'update.anwenden ohne sha256'; schritte = @(@{ kennung = 'update.anwenden'; parameter = @{} }) },
    @{ name = 'update.anwenden sha256 mit 63 Zeichen'; schritte = @(@{ kennung = 'update.anwenden'; parameter = @{ sha256 = ('a' * 63) } }) },
    @{ name = 'wiederherstellungspunkt.zurueck folge "0"'; schritte = @(@{ kennung = 'wiederherstellungspunkt.zurueck'; parameter = @{ folge = '0' } }) },
    @{ name = 'wiederherstellungspunkt.zurueck folge "1 OR 1=1"'; schritte = @(@{ kennung = 'wiederherstellungspunkt.zurueck'; parameter = @{ folge = '1 OR 1=1' } }) },
    @{ name = 'wartung.massnahmen freigaben "speicher.papierkorb.leeren" (Stufe 3 nie freischaltbar)'; schritte = @(@{ kennung = 'wartung.massnahmen'; parameter = @{ freigaben = 'speicher.papierkorb.leeren' } }) },
    @{ name = 'wartung.massnahmen freigaben "netz.winsock.zuruecksetzen"'; schritte = @(@{ kennung = 'wartung.massnahmen'; parameter = @{ freigaben = 'netz.winsock.zuruecksetzen' } }) }
)
foreach ($fall in $m4Faelle) {
    $planId = 'probe-m4abl-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $protokoll = Join-Path $protokollOrdner ('lauf-' + $planId + '.jsonl')
    $planDatei = $null
    try {
        if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt"; break }
        $planDatei = Schreibe-Plan $planId $fall.schritte
        $lauf = Starte-Helfer @('--helfer', '--plan', ('"' + $planDatei + '"'), '--trocken') $true
        if ($lauf.ExitCode -ne 2) { $hits += "$($fall.name): Exit $($lauf.ExitCode) statt 2"; $hits += Lies-Laufgrund $protokoll $planId ("  " + $fall.name + ':') }
        if ($lauf.Kinder.Count -gt 0) { $hits += "$($fall.name): Kindprozesse " + ($lauf.Kinder -join ', ') }
        if (-not (Test-Path $protokoll)) { $hits += "$($fall.name): kein Laufprotokoll $protokoll" }
        else {
            $zeilen = Lies-Laufprotokoll $protokoll
            if (@($zeilen | Where-Object { $_.art -eq 'abgelehnt' }).Count -lt 1) { $hits += "$($fall.name): keine Protokollzeile art=abgelehnt" }
            if (@($zeilen | Where-Object { $_.art -eq 'anfang' }).Count -gt 0) { $hits += "$($fall.name): trotz Ablehnung ist etwas angelaufen (anfang-Zeile)" }
        }
    }
    finally {
        if ($planDatei) { Remove-Item $planDatei -ErrorAction SilentlyContinue }
        Remove-Item $protokoll -ErrorAction SilentlyContinue
    }
}
# Gegenprobe: eine Instanz-Id mit Leerzeichen, Klammern und Raute ist gueltig (Zeichenmenge des Kerns).
$gegenText = 'Gegenprobe nicht gelaufen'
$planId = 'probe-m4gegen-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$protokoll = Join-Path $protokollOrdner ('lauf-' + $planId + '.jsonl')
$planDatei = $null
try {
    if (Test-Path $exeApp) {
        $planDatei = Schreibe-Plan $planId @(@{ kennung = 'geraet.treiber.neu'; parameter = @{ instanzId = 'ACPI\PNP0A08\0 (Probe) #1' } })
        $lauf = Starte-Helfer @('--helfer', '--plan', ('"' + $planDatei + '"'), '--trocken') $true
        $gegenText = 'Gegenprobe Instanz-Id mit Leerzeichen Exit ' + $lauf.ExitCode
        if ($lauf.ExitCode -ne 0 -and $lauf.ExitCode -ne 1) { $hits += "Gegenprobe: Exit $($lauf.ExitCode) statt 0 oder 1 (eine gueltige Instanz-Id mit Leerzeichen und Klammern wird abgelehnt)" }
        if ($lauf.Kinder.Count -gt 0) { $hits += "Gegenprobe: Kindprozesse " + ($lauf.Kinder -join ', ') }
    }
}
finally {
    if ($planDatei) { Remove-Item $planDatei -ErrorAction SilentlyContinue }
    Remove-Item $protokoll -ErrorAction SilentlyContinue
}
Test-Result "Helfer lehnt ungueltige M4-Parameter ab (Exit 2, nichts laeuft; $($m4Faelle.Count) Faelle; $gegenText)" ($hits.Count -eq 0) $hits

# 3. Bestaetigungspflicht (Vertrag 4, 7, 10.5): der Host liest bestaetigt im Verteiler von
# massnahmeAusfuehren und restoreRevert und lehnt Stufe 3 ohne true ab (Satz "Die Bestaetigung fehlt");
# die Oberflaeche sendet bestaetigt: true nur nach dem Kontrollkaestchen (data-verlust) und zeigt bei
# Stufe 3 den Verlust-Text (v.verlust) im Kasten modal-verlust; die Vorschau-Antwort traegt verlust und stufe.
$hits = @()
$cfText4 = [IO.File]::ReadAllText((Join-Path $root 'host\CheckFlow.cs'), [Text.Encoding]::UTF8)
$sfText4 = [IO.File]::ReadAllText((Join-Path $root 'host\ShellForm.cs'), [Text.Encoding]::UTF8)
$jsText4 = [IO.File]::ReadAllText((Join-Path $root 'ui\app.js'), [Text.Encoding]::UTF8)
if ($sfText4 -notmatch 'type == "massnahmeAusfuehren"\)\s*MassnahmeAusfuehren\([^;]*ToBool\(m, "bestaetigt"\)') { $hits += "host\ShellForm.cs reicht bestaetigt aus der Nachricht massnahmeAusfuehren nicht als ToBool weiter" }
if ($sfText4 -notmatch 'type == "restoreRevert"\)\s*RestoreRevert\([^;]*ToBool\(m, "bestaetigt"\)') { $hits += "host\ShellForm.cs reicht bestaetigt aus der Nachricht restoreRevert nicht weiter (Vertrag 10.5)" }
if ($cfText4 -notmatch 'void MassnahmeAusfuehren\(string kennung, string schluessel, bool bestaetigt\)') { $hits += "host\CheckFlow.cs: MassnahmeAusfuehren traegt keinen Parameter bestaetigt" }
if ($cfText4 -notmatch 'Stufe >= Massnahme\.StufeUnumkehrbar && !bestaetigt') { $hits += "host\CheckFlow.cs lehnt Stufe 3 ohne bestaetigt nicht ab (Massnahme.StufeUnumkehrbar && !bestaetigt)" }
if ($cfText4 -notmatch 'Die Best\u00e4tigung fehlt') { $hits += "host\CheckFlow.cs nennt den Grund 'Die Bestaetigung fehlt' nicht" }
if ($sfText4 -notmatch 'void RestoreRevert\(int seq, bool bestaetigt\)') { $hits += "host\ShellForm.cs: RestoreRevert traegt keinen Parameter bestaetigt" }
if ($cfText4 -notmatch 'verlust\s*=\s*m\.VerlustMit\(') { $hits += "host\CheckFlow.cs: die Vorschau-Antwort traegt kein verlust = m.VerlustMit(...)" }
# app.js: bestaetigt: true nur an einer Stelle je Weg, jeweils hinter dem Haekchen.
$jsBest = @([regex]::Matches($jsText4, 'bestaetigt:\s*true'))
if ($jsBest.Count -lt 2) { $hits += "ui\app.js sendet bestaetigt: true an $($jsBest.Count) Stellen statt an mindestens 2 (massnahmeAusfuehren und restoreRevert)" }
if ($jsText4 -notmatch "if\(bestaetigt === true\) send\(\{type:'massnahmeAusfuehren'") { $hits += "ui\app.js: massnahmeStarten sendet bestaetigt nicht nur bei === true" }
if ($jsText4 -notmatch 'data-verlust="1"') { $hits += "ui\app.js: das Kontrollkaestchen traegt kein data-verlust" }
if ($jsText4 -notmatch 'if\(!kasten\.checked\)') { $hits += "ui\app.js: der Knopf 'Jetzt ausfuehren' prueft das Kaestchen nicht noch einmal (if(!kasten.checked))" }
if ($jsText4 -notmatch "ok\.disabled = !kasten\.checked") { $hits += "ui\app.js: der Knopf ist nicht an das Kaestchen gebunden (ok.disabled = !kasten.checked)" }
if ($jsText4 -notmatch "el\('div','modal-verlust'\)") { $hits += "ui\app.js: kein Kasten modal-verlust in der Vorschau" }
if ($jsText4 -notmatch 'esc\(v\.verlust') { $hits += "ui\app.js: der Verlust-Text (v.verlust) wird in der Vorschau nicht gezeigt" }
if ($jsText4 -notmatch 'Ich habe verstanden') { $hits += "ui\app.js: das Kaestchen traegt nicht den Satz 'Ich habe verstanden, was verloren geht'" }
# Kein Weg an der Sperre vorbei: massnahmeAusfuehren geht nur aus massnahmeStarten hinaus, dort genau
# zweimal (Stufe 3 mit bestaetigt: true, darunter ohne das Feld); jede weitere Sendestelle waere ein
# Knopf, der die Vorschau und das Haekchen umgeht.
$sendeMa = @([regex]::Matches($jsText4, "type:'massnahmeAusfuehren'"))
$starten = [regex]::Match($jsText4, '(?s)function massnahmeStarten\([^)]*\)\{.*?\n\}')
if (-not $starten.Success) { $hits += "ui\app.js: function massnahmeStarten nicht auffindbar" }
else {
    $inStarten = @([regex]::Matches($starten.Value, "type:'massnahmeAusfuehren'")).Count
    if ($sendeMa.Count -ne 2 -or $inStarten -ne 2) { $hits += "ui\app.js sendet massnahmeAusfuehren an $($sendeMa.Count) Stellen ($inStarten davon in massnahmeStarten) statt genau 2 in massnahmeStarten" }
    if (@([regex]::Matches($starten.Value, 'bestaetigt:\s*true')).Count -ne 1) { $hits += "ui\app.js: massnahmeStarten sendet bestaetigt: true nicht genau einmal (im Zweig bestaetigt === true)" }
}
Test-Result "Bestaetigungspflicht: Stufe 3 laeuft nur mit bestaetigt = true (Host lehnt ab, Oberflaeche sendet es nur nach dem Haekchen, Verlust-Text in der Vorschau)" ($hits.Count -eq 0) $hits

# 4. Export-Herkunft und Rueckweg-Inhalt M4, in-process ueber tests\HelferProbe.cs (Gruppen export
# und m4rueckweg): ExportSchuetzen und ExportOeffnen wie die JSON-Pruefung (Besitzer, DACL, Hardlink,
# Reparse-Punkt, Name, Endung, Ort; erhoeht an echten Dateien, nicht erhoeht daran, dass ExportOrdner
# wirft); die Firewall-Sicherung nur mit vorhandenem, geschuetztem Export nach Muster; eine
# Startprogramm-Sicherung unter HKCU mit fremder profilSid abgelehnt, mit der eigenen angenommen;
# hosts-Inhalt (NUL, Steuerzeichen, 256 KB), IP-Adapterzeilen (IP=evil, Felder, doppelte SettingID),
# Firewall eingehend (0|1, Profil), Winlogon (beide Werte, 260 Zeichen), und die drei Kennungen ohne
# Rueckweg (Winsock, Update-Reset, Treiber) werden abgelehnt.
$hits = @()
$exportModus = 'Uebersetzung fehlgeschlagen'
$exeM4 = Join-Path $env:TEMP ('WW_HelferProbeM4_' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.exe')
try {
    $quellen = Helferprobe-Quellen
    $bauOut = & (Join-Path $root 'tools\csc.ps1') -Out $exeM4 -Target exe -Sources $quellen -Refs $helferprobeRefs 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exeM4)) { $hits += "Helferprobe liess sich nicht uebersetzen: " + (($bauOut | Select-Object -Last 3) -join ' | ') }
    else {
        $ausgabe = @(& $exeM4 export m4rueckweg 2>&1 | ForEach-Object { "$_" })
        if ($LASTEXITCODE -ne 0) { $hits += "mindestens eine Teilpruefung von export/m4rueckweg ist rot:"; $hits += @($ausgabe | Where-Object { $_ -match '\[FEHL\]' } | Select-Object -First 8) }
        $modusZeile = @($ausgabe | Where-Object { $_ -match 'Export-Probe laeuft' })
        if ($modusZeile.Count -gt 0) { if ($modusZeile[0] -match 'nicht erhoeht') { $exportModus = 'nicht erhoeht: ExportOrdner wirft, Nutzerdatei abgelehnt' } else { $exportModus = 'erhoeht: Besitzer, DACL, Hardlink, Symlink an echten Dateien' } }
        else { $hits += "Die Export-Probe ist nicht gelaufen (keine Modus-Zeile in der Ausgabe)" }
        $anzahl = @($ausgabe | Where-Object { $_ -match '^\s+\[(ok|FEHL)\]' }).Count
        if ($anzahl -lt 60) { $hits += "Nur $anzahl Teilpruefungen von export/m4rueckweg gelaufen (erwartet mindestens 60)" }
        if (@($ausgabe | Where-Object { $_ -match 'fremder profilSid' }).Count -lt 1) { $hits += "Die Teilpruefung zur fremden profilSid fehlt in der Ausgabe" }
        if (@($ausgabe | Where-Object { $_ -match 'NUL im Inhalt' }).Count -lt 1) { $hits += "Die Teilpruefung zum hosts-Inhalt mit NUL fehlt in der Ausgabe" }
        if (@($ausgabe | Where-Object { $_ -match 'IP=evil' }).Count -lt 1) { $hits += "Die Teilpruefung zur IP-Sicherung mit IP=evil fehlt in der Ausgabe" }
    }
}
catch { $hits += "Helferprobe: " + $_.Exception.Message }
finally { Remove-Item $exeM4 -ErrorAction SilentlyContinue }
Test-Result "Export-Herkunft (ExportSchuetzen, ExportOeffnen: Besitzer, DACL, Hardlink, Reparse, Name) und Rueckweg-Inhalt M4 (fremde profilSid, hosts, IP-Stack, Firewall, Winlogon) in-process ($exportModus)" ($hits.Count -eq 0) $hits

# 5. DISM nur als API (Vertrag 3.1, 3.4, 8.2 Punkt 5): in helfer\Massnahmen\ und helfer\Katalog.cs wird
# dism.exe nicht mehr gestartet (kein "dism.exe"/"DISM.exe" im Code, Kommentare zaehlen nicht, Literale
# schon); helfer\Massnahmen\System.cs bindet dismapi.dll ueber mindestens 5 DllImport-Eintraege ein.
# Kein PowerShell und kein Process.Start in helfer\Massnahmen\ deckt Pruefung 5 der Gruppe v8.2.
$hits = @()
$dismDateien = @(Get-ChildItem $m3Ordner -File -Filter *.cs) + @(Get-Item (Join-Path $root 'helfer\Katalog.cs'))
foreach ($f in $dismDateien) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*(///|//)') { continue }
        $code = $line
        $mk = $kommentarRx.Match($line)
        if ($mk.Success) { $code = $mk.Groups[1].Value }
        if ($code -match '(?i)dism\.exe') { $hits += "$($f.Name):$i  $($line.Trim())" }
    }
}
$systemCs = Join-Path $root 'helfer\Massnahmen\System.cs'
if (-not (Test-Path $systemCs)) { $hits += "helfer\Massnahmen\System.cs fehlt" }
else {
    $sysText = [IO.File]::ReadAllText($systemCs, [Text.Encoding]::UTF8)
    $dllImports = ([regex]::Matches($sysText, 'DllImport\("dismapi\.dll"')).Count
    if ($dllImports -lt 5) { $hits += "helfer\Massnahmen\System.cs bindet dismapi.dll nur $dllImports mal per DllImport ein (erwartet mindestens 5: Initialize, OpenSession, CheckImageHealth, RestoreImageHealth, CloseSession, Shutdown, GetLastErrorMessage, Delete)" }
    foreach ($fn in @('DismInitialize', 'DismOpenSession', 'DismCheckImageHealth', 'DismRestoreImageHealth', 'DismCloseSession', 'DismShutdown')) {
        if ($sysText -notmatch ('\b' + $fn + '\b')) { $hits += "helfer\Massnahmen\System.cs kennt $fn nicht" }
    }
    if ($sysText -notmatch 'dism\.zustand') { $hits += "helfer\Massnahmen\System.cs schreibt den Wert dism.zustand nicht" }
    if ($sysText -notmatch 'sfc\.zustand') { $hits += "helfer\Massnahmen\System.cs schreibt den Wert sfc.zustand nicht" }
}
# Der Host wertet DISM nur noch ueber dism.zustand, nicht ueber Konsolentext.
if ($cfText4 -notmatch 'dism\.zustand') { $hits += "host\CheckFlow.cs liest dism.zustand nicht (EvaluateFiles)" }
Test-Result "DISM nur ueber die DISM-API: kein dism.exe in helfer\Massnahmen\ und helfer\Katalog.cs, dismapi.dll per DllImport, Werte dism.zustand und sfc.zustand" ($hits.Count -eq 0) $hits

# 6. Techniker-Nachricht (Vertrag 4, 7, 8.2 Punkt 6) woertlich auf beiden Seiten: der Host sendet
# techniker, technikerText und technikerGespeichert, versteht technikerAnsicht, technikerText und
# technikerSpeichern; app.js hat die drei case-Zweige und sendet die drei Befehle.
$hits = @()
$hostM4 = ($csFiles | Where-Object { $_.DirectoryName -like '*\host' } | ForEach-Object { [IO.File]::ReadAllText($_.FullName, [Text.Encoding]::UTF8) }) -join "`n"
foreach ($n in @('techniker', 'technikerText', 'technikerGespeichert')) {
    if ($hostM4 -notmatch ('type\s*=\s*"' + $n + '"')) { $hits += "Der Host sendet keine Nachricht type = ""$n""" }
    if ($jsText4 -notmatch ("case\s+'" + $n + "'")) { $hits += "ui\app.js hat keinen Zweig case '$n'" }
}
foreach ($b in @('technikerAnsicht', 'technikerText', 'technikerSpeichern')) {
    if ($sfText4 -notmatch ('type == "' + $b + '"')) { $hits += "host\ShellForm.cs versteht den Befehl ""$b"" nicht" }
    if ($jsText4 -notmatch ("send\(\{type:'" + $b + "'")) { $hits += "ui\app.js sendet '$b' nirgends" }
}
if ($cfText4 -notmatch 'type = "techniker", leer = true') { $hits += "host\CheckFlow.cs sendet ohne Ergebnis kein techniker {leer: true}" }
if ($jsText4 -notmatch 'Erst pr\u00fcfen, dann gibt es hier etwas zu sehen') { $hits += "ui\app.js: der Satz fuer die leere Techniker-Ansicht fehlt" }
Test-Result "Techniker-Nachrichten stehen woertlich auf beiden Seiten (techniker, technikerText, technikerGespeichert; technikerAnsicht, technikerText, technikerSpeichern)" ($hits.Count -eq 0) $hits

# 7. Update-Weg trocken (Vertrag 3.6, 4, 10.1, 10.3): update.anwenden mit einer gueltigen Pruefsumme
# (64 Hex einer vorhandenen Datei) und --trocken, ohne update.zip im festen Quellpfad
# (<LocalApplicationData>\WindowsWartung\update\update.zip): Exit 1 mit der Zeile "update.zip fehlt",
# nie 2 (der Parameter war gueltig), kein Kindprozess. Liegt dort eine echte update.zip (nach einem
# Download), bleibt sie unangetastet, und die Probe erwartet Exit 1 mit der Pruefsummen-Zeile (die
# Pruefsumme einer anderen Datei passt nie). Greps: host\ShellForm.cs schreibt keine ww_update.cmd
# mehr, StarteErhoeht wird nur noch in UpdateViaInstaller aufgerufen, und der Plan update.anwenden
# geht mit sha256 ueber PlanAusfuehren.
$hits = @()
$updateText = 'nicht gelaufen'
$zipQuelle = Join-Path $env:LOCALAPPDATA 'WindowsWartung\update\update.zip'
$zipDa = Test-Path $zipQuelle
$planId = 'probe-m4upd-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$protokoll = Join-Path $protokollOrdner ('lauf-' + $planId + '.jsonl')
$planDatei = $null
try {
    if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt" }
    else {
        $sha = (Get-FileHash -Path (Join-Path $root 'build.ps1') -Algorithm SHA256).Hash.ToLowerInvariant()
        $planDatei = Schreibe-Plan $planId @(@{ kennung = 'update.anwenden'; parameter = @{ sha256 = $sha } })
        $lauf = Starte-Helfer @('--helfer', '--plan', ('"' + $planDatei + '"'), '--trocken') $true
        if ($null -eq $lauf.ExitCode) { $hits += "ExitCode nicht lesbar" }
        elseif ($lauf.ExitCode -ne 1) {
            $hits += "update.anwenden trocken ohne Paket: Exit $($lauf.ExitCode) statt 1 (2 = die gueltige Pruefsumme wurde abgelehnt; Grund aus $protokoll und logs\app.log):"
            $hits += Lies-Laufgrund $protokoll $planId 'Update:'
        }
        if ($lauf.Kinder.Count -gt 0) { $hits += "update.anwenden hat Kindprozesse gestartet: " + ($lauf.Kinder -join ', ') }
        if (-not (Test-Path $protokoll)) { $hits += "Laufprotokoll fehlt: $protokoll" }
        else {
            $zeilen = Lies-Laufprotokoll $protokoll
            $fehler = @($zeilen | Where-Object { $_.art -eq 'fehler' -and $_.kennung -eq 'update.anwenden' })
            if (@($zeilen | Where-Object { $_.art -eq 'abgelehnt' }).Count -gt 0) { $hits += "der Plan wurde abgelehnt, obwohl sha256 gueltig war" }
            if ($fehler.Count -lt 1) { $hits += "keine fehler-Zeile fuer update.anwenden im Laufprotokoll" }
            elseif ($zipDa) {
                if (@($fehler | Where-Object { $_.laie -match 'Pr\u00fcfsumme' }).Count -lt 1) { $hits += "update.zip liegt vor, aber keine fehler-Zeile nennt die Pruefsumme: " + $fehler[0].laie }
                $updateText = 'update.zip vorhanden (unangetastet), Pruefsumme passt nicht, Exit ' + $lauf.ExitCode
            }
            else {
                if (@($fehler | Where-Object { $_.laie -match 'update\.zip fehlt' }).Count -lt 1) { $hits += "keine fehler-Zeile 'update.zip fehlt': " + $fehler[0].laie }
                $updateText = 'ohne update.zip, Exit ' + $lauf.ExitCode
            }
            foreach ($z in @($zeilen | Where-Object { $_.art -eq 'nachweis' })) {
                if ($z.fachmann.bestanden -eq $true) { $hits += "update.anwenden meldet ohne Paket einen bestandenen Nachweis: " + $z.laie }
            }
        }
        if ($zipDa -and -not (Test-Path $zipQuelle)) { $hits += "Die vorhandene update.zip ist nach dem Trockenlauf weg: $zipQuelle" }
    }
}
catch { $hits += "Update-Probe: " + $_.Exception.Message }
finally {
    if ($planDatei) { Remove-Item $planDatei -ErrorAction SilentlyContinue }
    Remove-Item $protokoll -ErrorAction SilentlyContinue
}
if ($sfText4 -match 'ww_update\.cmd') { $hits += "host\ShellForm.cs nennt noch ww_update.cmd (die Austausch-Batch per runas ist seit 8.3 weg)" }
$sfZeilen = [IO.File]::ReadAllLines((Join-Path $root 'host\ShellForm.cs'), [Text.Encoding]::UTF8)
$erhoehtAufrufe = @()
$inInstaller = $false
for ($i = 0; $i -lt $sfZeilen.Count; $i++) {
    $z = $sfZeilen[$i]
    if ($z -match '^\s*(///|//)') { continue }
    if ($z -match '\b(void|string|bool)\s+UpdateViaInstaller\(') { $inInstaller = $true }
    elseif ($z -match '^\s{8}(static\s+)?(void|string|bool|int|async)\s+\w+\(') { $inInstaller = $false }
    if ($z -match 'StarteErhoeht\(' -and $z -notmatch 'static string StarteErhoeht\(') {
        if (-not $inInstaller) { $erhoehtAufrufe += "ShellForm.cs:$($i + 1)  $($z.Trim())" }
    }
}
if ($erhoehtAufrufe.Count -gt 0) { $hits += "StarteErhoeht wird ausserhalb von UpdateViaInstaller aufgerufen:"; $hits += $erhoehtAufrufe }
if ($sfText4 -notmatch 'Plan\.Neu\("Update"\)\.Mit\(\w+, "sha256"') { $hits += "host\ShellForm.cs baut den Plan update.anwenden mit sha256 nicht (Plan.Neu(""Update"").Mit(kennung, ""sha256"", ...))" }
if ($sfText4 -notmatch 'ProgrammMassnahmen\.Kennung|"update\.anwenden"') { $hits += "host\ShellForm.cs nennt die Kennung update.anwenden nicht (weder ProgrammMassnahmen.Kennung noch das Literal)" }
if ($sfText4 -match 'ZipFile\.ExtractToDirectory') { $hits += "host\ShellForm.cs entpackt das Paket noch selbst (ZipFile.ExtractToDirectory); das macht seit 8.3 der Helfer" }
Test-Result "Update-Weg trocken: update.anwenden mit gueltiger Pruefsumme endet mit Exit 1 und nennt den Grund ($updateText); keine ww_update.cmd, StarteErhoeht nur im Installer-Weg" ($hits.Count -eq 0) $hits

# 8. Zeitplan-Sperre und Zeitbudget (Vertrag 5, 10.1): Host und Helfer nutzen dieselbe Konstante
# Scheduler.ZeitplanSperre = Global\WindowsWartung.zeitplan; der Helferstart ruft
# Scheduler.ZeitbudgetAuffrischen(); der erhoehte Hoststart ruft Sicherungsdatei.OrdnerAbsichern().
$hits = @()
$schedText = [IO.File]::ReadAllText((Join-Path $root 'src\Scheduler.cs'), [Text.Encoding]::UTF8)
$helferText = [IO.File]::ReadAllText((Join-Path $root 'helfer\Helfer.cs'), [Text.Encoding]::UTF8)
$programText = [IO.File]::ReadAllText((Join-Path $root 'host\Program.cs'), [Text.Encoding]::UTF8)
if ($schedText -notmatch 'ZeitplanSperre\s*=\s*@"Global\\WindowsWartung\.zeitplan"') { $hits += "src\Scheduler.cs: die Konstante ZeitplanSperre = Global\WindowsWartung.zeitplan fehlt" }
if (([regex]::Matches($schedText, 'Global\\WindowsWartung\.zeitplan')).Count -ne 1) { $hits += "src\Scheduler.cs nennt Global\WindowsWartung.zeitplan nicht genau einmal (die Konstante ist die eine Stelle)" }
if ($schedText -notmatch 'new Mutex\(false, ZeitplanSperre') { $hits += "src\Scheduler.cs legt den Mutex nicht ueber die Konstante ZeitplanSperre an" }
if ($schedText -notmatch 'SperreWartenMs = 5000' -or $schedText -notmatch 'WaitOne\(SperreWartenMs\)') { $hits += "src\Scheduler.cs wartet nicht 5000 ms (SperreWartenMs) auf die Sperre" }
if ($schedText -notmatch 'AbandonedMutexException') { $hits += "src\Scheduler.cs behandelt AbandonedMutexException nicht (zaehlt als erworben)" }
if ($schedText -notmatch 'public static string ZeitbudgetAuffrischen\(\)') { $hits += "src\Scheduler.cs: public static string ZeitbudgetAuffrischen() fehlt" }
if ($schedText -notmatch 'PT2H' -or $schedText -notmatch 'PT4H') { $hits += "src\Scheduler.cs: ZeitbudgetAuffrischen kennt PT2H und PT4H nicht beide" }
if ($helferText -notmatch 'Scheduler\.ZeitbudgetAuffrischen\(\)') { $hits += "helfer\Helfer.cs ruft Scheduler.ZeitbudgetAuffrischen() beim Start nicht" }
if ($helferText -notmatch 'Aufraeumen\(\)') { $hits += "helfer\Helfer.cs raeumt die .vorher-Dateien beim Start nicht weg (ProgrammMassnahmen.Aufraeumen)" }
if ($programText -notmatch 'Sicherungsdatei\.OrdnerAbsichern\(\)') { $hits += "host\Program.cs ruft Sicherungsdatei.OrdnerAbsichern() beim erhoehten Start nicht" }
# Der Name steht ausserhalb von src\Scheduler.cs in keinem Code (Kommentare zaehlen nicht): eine
# zweite Stelle liefe an der Konstante vorbei und koennte still einen anderen Mutex benutzen.
foreach ($f in (Get-ChildItem (Join-Path $root 'src'),(Join-Path $root 'host'),(Join-Path $root 'helfer') -File -Filter *.cs -Recurse)) {
    if ($f.Name -eq 'Scheduler.cs') { continue }
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*(///|//)') { continue }
        $code = $line
        $mk = $kommentarRx.Match($line)
        if ($mk.Success) { $code = $mk.Groups[1].Value }
        if ($code -match 'Global\\{1,2}WindowsWartung\.zeitplan') { $hits += "$($f.Name):$i nennt den Mutex-Namen woertlich statt ueber Scheduler.ZeitplanSperre: $($line.Trim())" }
    }
}
Test-Result "Zeitplan-Sperre (Global\WindowsWartung.zeitplan, eine Konstante) und Zeitbudget (ZeitbudgetAuffrischen im Helferstart), OrdnerAbsichern im erhoehten Hoststart" ($hits.Count -eq 0) $hits

# 9. DLL-Suchpfad: System32 vor dem Programmordner (Vertrag 10.5). Eine Kopie der EXE samt ui\ und den
# drei WebView2-DLLs im Testordner, daneben %SystemRoot%\System32\version.dll als dwmapi.dll und
# cryptbase.dll (harmlose Koeder: eine fremde dwmapi.dll neben der EXE ist der klassische Weg, Code in
# ein Programm zu laden). Start mit --shot <png> --view start --shotwait 3000; nach dem Laden die
# Modulliste des Prozesses: dwmapi.dll und cryptbase.dll aus System32, WebView2Loader.dll aus dem
# Testordner (die CLR findet sie ueber den Ordner der WebView2-Assembly), Exit 0, PNG ueber 10 KB.
# Dazu die Greps: [assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)] in
# src\AssemblyInfo.cs, SetDefaultDllDirectories in host\Program.cs, kein AddDllDirectory im Code.
# Aufrufform: --shot erwartet den PNG-Pfad als naechstes Argument, sonst entsteht eine Datei "--view".
$hits = @()
$dllText = 'nicht gelaufen'
$testOrdner = Join-Path $env:TEMP ('WW_dllpfad_' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$shotPng = Join-Path $testOrdner 'start.png'
try {
    $asmText = [IO.File]::ReadAllText((Join-Path $root 'src\AssemblyInfo.cs'), [Text.Encoding]::UTF8)
    if ($asmText -notmatch '\[assembly:\s*DefaultDllImportSearchPaths\(DllImportSearchPath\.System32\)\]') { $hits += "src\AssemblyInfo.cs traegt nicht [assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]" }
    if ($programText -notmatch 'SetDefaultDllDirectories\(LOAD_LIBRARY_SEARCH_SYSTEM32\)') { $hits += "host\Program.cs ruft SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32) nicht" }
    foreach ($f in (Get-ChildItem (Join-Path $root 'src'),(Join-Path $root 'host'),(Join-Path $root 'helfer'),(Join-Path $root 'kern'),(Join-Path $root 'sammler') -File -Filter *.cs -Recurse)) {
        $i = 0
        foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
            $i++
            if ($line -match '^\s*(///|//)') { continue }
            $code = $line
            $mk = $kommentarRx.Match($line)
            if ($mk.Success) { $code = $mk.Groups[1].Value }
            if ($code -match 'AddDllDirectory|LOAD_LIBRARY_SEARCH_USER_DIRS') { $hits += "$($f.Name):$i  $($line.Trim())" }
        }
    }
    # Ohne WebView2-Laufzeit zeigt der Host beim Start einen Dialog (Program.cs WebView2Available)
    # und kein Bild; dann laufen hier nur die Greps, und die Zeile sagt es (zaehlt trotzdem, wie die
    # Live-Pruefung ohne Erhoehung). Die Laufzeit steht wie beim Loader unter EdgeUpdate\Clients.
    $webView2 = $null
    foreach ($k in @('HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
                     'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
                     'HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')) {
        try { $pv = "$((Get-ItemProperty -Path $k -Name pv -ErrorAction Stop).pv)"; if ($pv -match '^\d+\.' -and $pv -ne '0.0.0.0') { $webView2 = $pv; break } } catch { }
    }
    if (-not (Test-Path $exeApp)) { $hits += "bin\WindowsWartung.exe fehlt" }
    elseif ($null -eq $webView2) { $dllText = 'Start uebersprungen: WebView2-Laufzeit nicht registriert, nur die Greps' }
    else {
        New-Item -ItemType Directory -Path $testOrdner | Out-Null
        foreach ($n in @('WindowsWartung.exe', 'WebView2Loader.dll', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll')) {
            $q = Join-Path $root ('bin\' + $n)
            if (-not (Test-Path $q)) { $hits += "bin\$n fehlt" } else { Copy-Item $q (Join-Path $testOrdner $n) }
        }
        Copy-Item (Join-Path $root 'bin\ui') (Join-Path $testOrdner 'ui') -Recurse
        $koeder = Join-Path $env:SystemRoot 'System32\version.dll'
        foreach ($n in @('dwmapi.dll', 'cryptbase.dll')) { Copy-Item $koeder (Join-Path $testOrdner $n) }
        $exeKopie = Join-Path $testOrdner 'WindowsWartung.exe'
        $p = Start-Process -FilePath $exeKopie -ArgumentList @('--shot', ('"' + $shotPng + '"'), '--view', 'start', '--shotwait', '3000') -WorkingDirectory $testOrdner -PassThru
        $null = $p.Handle
        # Modulliste, sobald dwmapi.dll und WebView2Loader.dll geladen sind (hoechstens 12 s), dann das Ende abwarten.
        $module = @()
        $uhr = [Diagnostics.Stopwatch]::StartNew()
        while (-not $p.HasExited -and $uhr.ElapsedMilliseconds -lt 12000) {
            try { $module = @((Get-Process -Id $p.Id -ErrorAction Stop).Modules | ForEach-Object { "$($_.FileName)" }) } catch { $module = @() }
            $hatDwm = @($module | Where-Object { $_ -match '\\dwmapi\.dll$' }).Count -gt 0
            $hatLoader = @($module | Where-Object { $_ -match '\\WebView2Loader\.dll$' }).Count -gt 0
            if ($hatDwm -and $hatLoader) { break }
            Start-Sleep -Milliseconds 250
        }
        if (-not $p.WaitForExit(60000)) { try { $p.Kill() } catch { }; $hits += "Der Start mit --shot lief nach 60 s noch (beendet)" }
        $p.WaitForExit()
        $exitShot = $p.ExitCode
        $system32 = (Join-Path $env:SystemRoot 'System32').TrimEnd('\')
        $gesehen = @{}
        foreach ($n in @('dwmapi.dll', 'cryptbase.dll', 'WebView2Loader.dll')) {
            $treffer = @($module | Where-Object { $_ -match ('\\' + [regex]::Escape($n) + '$') })
            if ($treffer.Count -eq 0) { $gesehen[$n] = '(nicht geladen)' } else { $gesehen[$n] = $treffer[0] }
        }
        foreach ($n in @('dwmapi.dll', 'cryptbase.dll')) {
            $pfad = $gesehen[$n]
            if ($pfad -eq '(nicht geladen)') { $hits += "$n war binnen 12 s nicht in der Modulliste (Modulliste $($module.Count) Eintraege)" }
            elseif (-not $pfad.StartsWith($system32, [StringComparison]::OrdinalIgnoreCase)) { $hits += "$n kam aus $pfad statt aus $system32 (der Koeder neben der EXE wurde geladen)" }
        }
        $loader = $gesehen['WebView2Loader.dll']
        if ($loader -eq '(nicht geladen)') { $hits += "WebView2Loader.dll war binnen 12 s nicht in der Modulliste" }
        elseif (-not $loader.StartsWith($testOrdner, [StringComparison]::OrdinalIgnoreCase)) { $hits += "WebView2Loader.dll kam aus $loader statt aus dem Testordner $testOrdner" }
        if ($null -eq $exitShot) { $hits += "ExitCode des Starts nicht lesbar" }
        elseif ($exitShot -ne 0) { $hits += "Der Start mit --shot endete mit Exit $exitShot statt 0 (5 = Startpruefung verweigert, 6 = Abweichung)" }
        if (-not (Test-Path $shotPng)) { $hits += "Kein Bildschirmfoto unter $shotPng" }
        else {
            $groesse = (Get-Item $shotPng).Length
            if ($groesse -le 10240) { $hits += "Das Bildschirmfoto ist nur $groesse Byte gross (erwartet ueber 10 KB)" }
            $dllText = "WebView2 $webView2, Exit $exitShot, PNG $groesse Byte, dwmapi und cryptbase aus System32, WebView2Loader aus dem Testordner"
        }
    }
}
catch { $hits += "DLL-Suchpfad-Probe: " + $_.Exception.Message }
finally { Remove-Item $testOrdner -Recurse -Force -ErrorAction SilentlyContinue }
Test-Result "DLL-Suchpfad: System32 vor dem Programmordner (Koeder dwmapi.dll und cryptbase.dll neben der EXE bleiben ungeladen; $dllText)" ($hits.Count -eq 0) $hits

Write-Host ""
# Die Zahl der gelaufenen Pruefungen gegen den Kopf halten: eine Pruefung, die still wegfaellt
# (Bedingung falsch, Zweig uebersprungen), waere sonst unsichtbar.
$gelaufen = $passed + $failed
if ($gelaufen -ne $erwartet) {
    Write-Host ("  [FEHL] Es liefen {0} Pruefungen, erwartet werden {1} (Kopf von run-tests.ps1 anpassen)" -f $gelaufen, $erwartet) -ForegroundColor Red
    $failed++
}
Write-Host ("Ergebnis: {0} bestanden, {1} fehlgeschlagen (von {2} Pruefungen)" -f $passed, $failed, $erwartet) -ForegroundColor $(if ($failed) { 'Red' } else { 'Green' })
if ($failed) { exit 1 }
exit 0
