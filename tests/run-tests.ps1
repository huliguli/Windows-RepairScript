# Pruefungen, die in der CI (Tag, Push auf main, Handstart) und vor jedem Release laufen; build.ps1 ruft sie nicht auf.
#
# Hintergrund: Regeln, die nur in einem Dokument stehen, werden ueberlesen. Genau so sind
# die Farbwolken, das Korn und die falsch geschlossenen Anfuehrungszeichen ins Projekt
# gekommen. Deshalb stehen sie hier als Test - ein Verstoss bricht den Bau.
#
# Aufruf:  .\tests\run-tests.ps1
# Ergebnis: ExitCode 0 = alles gruen, 1 = mindestens ein Test rot.
#
# Stand 13.09.2026: 52 Pruefungen (39 bis 8.0, 13 mit 8.1 in der Gruppe "v8.1: Helfer und
# Rechte"; nach dem adversarischen Gegenlesen kamen die bin-Frischepruefung und der
# Pipe-Zugriffsschutz dazu). Die Zahl steht in $erwartet und wird am Ende gegen die gelaufenen
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
$erwartet = 52

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
# dabei die eigenen Rechte. Werkzeugaufrufe laufen mit false. Vier Stellen duerfen das bewusst:
#   * Shell.cs          - der Rueckfallweg von OeffneImNutzerkontext selbst (Ausnahme wie bisher).
#   * HelferClient.cs   - der Start des Helfers per Verb = runas: ohne UseShellExecute kein UAC-Dialog.
#   * ShellForm.cs      - StarteErhoeht, die Update-Batch per runas (UAC-Dialog).
#   * Werkzeuge.cs      - Detached-Werkzeuge mit eigenem Fenster (cleanmgr, mdsched, chkdsk): die
#                         BRAUCHEN die Rechte, sie sollen ja am System arbeiten.
# Die drei 8.1-Stellen muessen den Grund in DERSELBEN Zeile als Kommentar tragen (UAC oder
# Detached); ein UseShellExecute = true ohne diese Begruendung ist auch dort ein Verstoss.
$erlaubtOhneGrund = @('Shell.cs')
$erlaubtMitGrund  = @('HelferClient.cs', 'ShellForm.cs', 'Werkzeuge.cs')
$hits = @()
foreach ($f in (Get-ChildItem (Join-Path $root 'src'),(Join-Path $root 'host'),(Join-Path $root 'helfer') -File -Filter *.cs -Recurse)) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*(///|//)') { continue }
        if ($line -match 'UseShellExecute\s*=\s*true') {
            if ($erlaubtOhneGrund -contains $f.Name) { }
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
if ($probenText -notmatch ', 8 Probenklassen') { $hits += "Nicht 8 Probenklassen gelaufen (eine Datei fehlt oder 'Laufen' ist nicht mehr public static)" }
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
Test-Result "Sammler laeuft, redigiert und meldet fehlende Rechte" ($hits.Count -eq 0) $hits

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
$hits = @()
foreach ($f in (Get-ChildItem (Join-Path $root 'helfer') -File -Filter *.cs -Recurse)) {
    $i = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)) {
        $i++
        if ($line -match '^\s*(///|//)') { continue }
        if ($line -cmatch 'System\.Windows\.Forms|Microsoft\.Web|\bCommandRunner\b|\bShell\.') {
            $hits += "$($f.Name):$i  $($line.Trim())"
        }
    }
}
Test-Result "Schichtregel: helfer\ kennt kein WinForms, kein WebView2, keinen CommandRunner, kein Shell" ($hits.Count -eq 0) $hits

# 3. Der Host startet keine Werkzeuge mehr selbst: DISM, SFC und PowerShell kennt nur noch der
# Helfer (Katalog). Process.Start( gibt es im Host genau zweimal: der runas-Start des Helfers
# (host\HelferClient.cs) und die Update-Batch per runas in ShellForm.StarteErhoeht. Shell.cs
# (src, Oeffnen im Nutzerkontext) ist nicht Teil dieser Pruefung.
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
    for ($i = 0; $i -lt $zeilen.Count; $i++) {
        $line = $zeilen[$i]
        if ($line -match '^\s*(///|//)') { continue }
        if ($line -match '(?i)"(dism|sfc|powershell)\.exe"') { $hits += "$($f.Name):$($i + 1)  $($line.Trim())" }
        if ($line -match 'Process\.Start\s*\(') {
            $ok = $false
            if ($f.Name -eq 'HelferClient.cs') { $ok = $true }
            elseif ($f.Name -eq 'ShellForm.cs' -and $von -ge 0 -and $i -gt $von -and ($bis -lt 0 -or $i -lt $bis)) { $ok = $true }
            if (-not $ok) { $hits += "$($f.Name):$($i + 1)  $($line.Trim())" }
        }
    }
}
$hcText = [IO.File]::ReadAllText((Join-Path $root 'host\HelferClient.cs'), [Text.Encoding]::UTF8)
if ($hcText -notmatch 'Verb\s*=\s*"runas"') { $hits += "host\HelferClient.cs startet den Helfer nicht mehr per runas" }
if (([regex]::Matches($hcText, 'Process\.Start\s*\(')).Count -ne 1) { $hits += "host\HelferClient.cs hat nicht genau einen Prozessstart (den runas-Start des Helfers)" }
Test-Result "Host startet keine Werkzeuge selbst (kein DISM, SFC, PowerShell; Process.Start nur per runas)" ($hits.Count -eq 0) $hits

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
$schritte = @(
    @{ kennung = 'tiefenpruefung'; parameter = @{} },
    @{ kennung = 'dateien.reparieren'; parameter = @{} },
    @{ kennung = 'wiederherstellungspunkt.anlegen'; parameter = @{ beschreibung = 'Probe Trockenlauf' } },
    @{ kennung = 'wiederherstellungspunkt.zurueck'; parameter = @{ folge = '1' } },
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
        elseif ($lauf.ExitCode -ne 0) { $hits += "Trockenlauf endete mit Exit $($lauf.ExitCode) statt 0 (Grund in $protokoll und in logs\app.log)" }
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
            foreach ($k in $kennungen) {
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
Test-Result "Trockenlauf jeder Kennung: Exit 0, je Kennung eine schritt-Zeile, kein Kindprozess, Waechterdatei bleibt ($($kennungen.Count) Kennungen; $sicherungText)" ($hits.Count -eq 0) $hits

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
        if ($helferProc.HasExited) { $hits += "Der Helfer endete sofort mit Exit $($helferProc.ExitCode) (Grund in logs\app.log, Zeile 'helfer: Start abgelehnt')" }
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
            elseif ($helferProc.HasExited) { $hits += "fremde SID: der Helfer endete mit Exit $($helferProc.ExitCode), bevor eine Verbindung moeglich war (SidGueltig lehnt eine wohlgeformte Konto-SID ab? Grund in logs\app.log)" }
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
# (src\CommandRunner.cs und src\AutoRunner.cs haengen am Host).
$hits = @()
$exe = Join-Path $env:TEMP ('WW_HelferProbe_' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.exe')
try {
    $quellen = @()
    $quellen += Get-ChildItem (Join-Path $root 'helfer\*.cs') | ForEach-Object { $_.FullName }
    $quellen += Get-ChildItem (Join-Path $root 'kern\*.cs') | ForEach-Object { $_.FullName }
    $quellen += Get-ChildItem (Join-Path $root 'kern\Regeln\*.cs') | ForEach-Object { $_.FullName }
    $quellen += Get-ChildItem (Join-Path $root 'sammler\*.cs') | Where-Object { $_.Name -ne 'AufzeichnenCli.cs' } | ForEach-Object { $_.FullName }
    $quellen += Get-ChildItem (Join-Path $root 'sammler\Quellen\*.cs') | ForEach-Object { $_.FullName }
    $quellen += Get-ChildItem (Join-Path $root 'src\*.cs') | Where-Object { @('CommandRunner.cs', 'AutoRunner.cs', 'AssemblyInfo.cs') -notcontains $_.Name } | ForEach-Object { $_.FullName }
    $quellen += (Join-Path $root 'tests\HelferProbe.cs')
    $bauOut = & (Join-Path $root 'tools\csc.ps1') -Out $exe -Target exe -Sources $quellen `
        -Refs 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll', 'System.Management.dll',
              'System.ServiceProcess.dll', 'System.Runtime.Serialization.dll', 'System.Xml.dll' 2>&1
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
