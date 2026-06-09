# Erzeugt huebsche GitHub-Release-Notes aus dem passenden CHANGELOG-Abschnitt.
# Der Rahmen ist bewusst ASCII (+ GitHub-Emoji-Shortcodes wie :sparkles:), damit es
# keine Encoding-Probleme gibt; die Umlaut-Inhalte stammen aus CHANGELOG.md (UTF-8).
#
#   .\tools\release-notes.ps1 -Tag v6.0                 # Vorschau in der Konsole
#   .\tools\release-notes.ps1 -Tag v6.0 -OutFile n.md   # in Datei schreiben (fuer gh --notes-file)
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$ChangelogPath = "CHANGELOG.md",
    [string]$OutFile = ""
)
$ErrorActionPreference = 'Stop'

$ver = $Tag.TrimStart('v', 'V', ' ').Trim()

# Abschnitt zwischen "## [<ver>]" und dem naechsten "## [" herausziehen.
$lines = Get-Content -LiteralPath $ChangelogPath -Encoding UTF8
$buf = New-Object System.Collections.Generic.List[string]
$inSection = $false
foreach ($line in $lines) {
    if ($line -match '^##\s*\[([0-9][^\]]*)\]') {
        if ($inSection) { break }
        if ($Matches[1].Trim() -eq $ver) { $inSection = $true }
        continue
    }
    if ($inSection) { $buf.Add($line) }
}
$body = ($buf -join "`n").Trim()
if (-not $body) { $body = "Siehe CHANGELOG.md fuer die Aenderungen dieser Version." }

# Sektions-Ueberschriften mit Emoji-Shortcodes versehen.
# $1 traegt das Originalwort (inkl. Umlaut) aus dem Input -> kein Umlaut im Skript noetig.
$body = $body -replace '(?m)^###\s+(Neu)\b',       '### :sparkles: $1'
$body = $body -replace '(?m)^###\s+(Ge.ndert)\b',  '### :wrench: $1'
$body = $body -replace '(?m)^###\s+(Behoben)\b',    '### :hammer: $1'
$body = $body -replace '(?m)^###\s+(Sicherheit)\b', '### :lock: $1'
$body = $body -replace '(?m)^###\s+(Entfernt)\b',   '### :wastebasket: $1'

$nl = "`n"
$notes =
    "## :hammer_and_wrench: Windows-Wartung $ver" + $nl + $nl +
    "Ein-Klick-Wartung und -Reparatur fuer Windows. Das steckt in diesem Update:" + $nl + $nl +
    $body + $nl + $nl +
    "---" + $nl + $nl +
    "### :arrow_down: Installation" + $nl +
    "- **Empfohlen:** den Installer **WindowsWartung-Setup.exe** herunterladen und starten." + $nl +
    "- **Portabel:** **WindowsWartung.zip** entpacken und **WindowsWartung.exe** starten." + $nl + $nl +
    "> :bulb: Schon installiert? Die App sucht beim Start automatisch nach neuen Versionen und installiert sie auf Wunsch selbst." + $nl

if ($OutFile) {
    [System.IO.File]::WriteAllText($OutFile, $notes, (New-Object System.Text.UTF8Encoding $false))
    Write-Host ("Release-Notes fuer " + $ver + " geschrieben: " + $OutFile)
}
else {
    try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
    Write-Output $notes
}
