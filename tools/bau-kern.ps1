# Baut Kern + Sammler als Kommandozeile (aufzeichnen.exe) - ohne Oberflaeche, ohne WebView2.
#
#   .\tools\bau-kern.ps1                     -> bin\aufzeichnen.exe
#   .\bin\aufzeichnen.exe --live             -> Systembild aufnehmen und Regeln ausgeben
#   .\bin\aufzeichnen.exe --aufzeichnen x.json
#
# Die Referenzen sind bewusst knapp: System.Management (WMI), System.ServiceProcess,
# System.Runtime.Serialization + System.Xml (JSON), System.Net.Http entfaellt (WebRequest).
# KEIN System.Windows.Forms, KEIN WebView2 - die Schichtregel des Konzepts.
param([string]$Out)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $Out) { $Out = Join-Path $root 'bin\aufzeichnen.exe' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null

& (Join-Path $root 'tools\csc.ps1') -Out $Out -Target exe `
    -Sources 'kern\*.cs','kern\Regeln\*.cs','sammler\*.cs','sammler\Quellen\*.cs' `
    -Refs 'System.Runtime.Serialization.dll','System.Xml.dll','System.Management.dll','System.ServiceProcess.dll'
"BAU OK  ->  $Out"
