# Uebersetzt C#-Quellen mit dem Roslyn-Compiler des .NET SDK gegen die
# .NET-Framework-4.8-Referenzassemblies. Gemeinsame Grundlage fuer build.ps1 und die Proben.
#
# Beispiel:
#   .\tools\csc.ps1 -Out $env:TEMP\KernProben.exe -Sources kern\*.cs,kern\Regeln\*.cs,tests\KernProben.cs `
#                   -Refs System.Runtime.Serialization.dll -Target exe
#
# mscorlib, System, System.Core sind immer dabei.
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [Parameter(Mandatory = $true)][string[]]$Sources,
    [string[]]$Refs = @(),
    [string[]]$LocalRefs = @(),
    [ValidateSet('exe', 'winexe', 'library')][string]$Target = 'exe',
    [string]$Manifest,
    [string]$Icon,
    [string[]]$Resources = @()
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw "dotnet nicht gefunden - das .NET SDK ist zum Bauen noetig (nur zum Bauen)." }
$sdkRoot = Join-Path (Split-Path -Parent $dotnet.Source) 'sdk'
$sdkDir = Get-ChildItem $sdkRoot -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'Roslyn\bincore\csc.dll') } |
          Sort-Object { [version]$_.Name } | Select-Object -Last 1
if (-not $sdkDir) { throw "Kein Roslyn-Compiler im SDK gefunden (gesucht unter $sdkRoot\*\Roslyn\bincore\csc.dll)." }
$csc = Join-Path $sdkDir.FullName 'Roslyn\bincore\csc.dll'

$refDir = @(
    (Join-Path $sdkRoot '..\packs\Microsoft.NETFramework.ReferenceAssemblies.net48\*\build\.NETFramework\v4.8'),
    "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
) | ForEach-Object { Get-Item $_ -ErrorAction SilentlyContinue } |
    Where-Object { $_ -and (Test-Path (Join-Path $_.FullName 'mscorlib.dll')) } | Select-Object -First 1
if (-not $refDir) { throw "Referenzassemblies fuer .NET Framework 4.8 nicht gefunden." }

$alleRefs = @('mscorlib.dll', 'System.dll', 'System.Core.dll') + $Refs | Select-Object -Unique
$refArgs = $alleRefs | ForEach-Object { "/reference:$($refDir.FullName)\$_" }
$localRefArgs = $LocalRefs | ForEach-Object { "/reference:$_" }

# Quellen: Globs relativ zur Projektwurzel oder absolute Pfade.
$dateien = @()
foreach ($s in $Sources) {
    $p = if ([IO.Path]::IsPathRooted($s)) { $s } else { Join-Path $root $s }
    $hits = @(Get-ChildItem $p -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    if ($hits.Count -eq 0) { throw "Keine Quelldatei fuer '$s'." }
    $dateien += $hits
}

$argList = @($csc, '/nologo', '/nostdlib+', "/target:$Target", '/platform:x64', "/out:$Out",
             '/codepage:65001', '/langversion:latest', '/optimize+', '/warnaserror-', '/nowarn:1701,1702,0649')
if ($Manifest) { $argList += "/win32manifest:$Manifest" }
if ($Icon) { $argList += "/win32icon:$Icon" }
foreach ($r in $Resources) { $argList += "/resource:$r" }
$argList += $refArgs
$argList += $localRefArgs
$argList += $dateien

& dotnet @argList
if ($LASTEXITCODE -ne 0) { throw "Uebersetzung fehlgeschlagen (ExitCode $LASTEXITCODE): $Out" }
