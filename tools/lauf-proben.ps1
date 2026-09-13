# Uebersetzt und startet die Kernproben: Regeln gegen aufgezeichnete Systembilder,
# ohne Windows-Zugriff, ohne Rechte. Rueckgabewert 0 = alles gruen.
#
#   .\tools\lauf-proben.ps1            alle Probenklassen
#
# Die Proben-EXE bindet NUR kern\ und kern\Regeln\ ein - kein sammler\, kein host\.
# Wer hier eine WMI- oder Prozess-Referenz braucht, hat die Schichtregel verletzt.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe = Join-Path $env:TEMP ('WW_KernProben_' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.exe')

& (Join-Path $root 'tools\csc.ps1') -Out $exe -Target exe `
    -Sources 'kern\*.cs','kern\Regeln\*.cs','tests\proben\*.cs' `
    -Refs 'System.Runtime.Serialization.dll','System.Xml.dll'

& $exe (Join-Path $root 'tests\aufzeichnungen')
$code = $LASTEXITCODE
Remove-Item $exe -ErrorAction SilentlyContinue
exit $code
