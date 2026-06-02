# Baut WindowsWartung.exe mit dem eingebauten .NET-Framework-Compiler (kein SDK noetig)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe nicht gefunden: $csc" }

# Icon bei Bedarf erzeugen
if (-not (Test-Path (Join-Path $root 'assets\app.ico'))) {
    & (Join-Path $root 'tools\generate-icon.ps1')
}

New-Item -ItemType Directory -Force -Path (Join-Path $root 'bin') | Out-Null

Push-Location $root
try {
    $refs = @(
        'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll'
    ) -join ','

    $argList = @(
        '/nologo',
        '/target:winexe',
        '/out:bin\WindowsWartung.exe',
        '/win32manifest:src\app.manifest',
        "/reference:$refs",
        '/codepage:65001',
        '/langversion:5',
        '/optimize+'
    )
    if (Test-Path 'assets\app.ico') { $argList += '/win32icon:assets\app.ico' }
    $argList += 'src\*.cs'

    & $csc @argList
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($code -eq 0) {
    "`nBUILD OK  ->  bin\WindowsWartung.exe"
} else {
    "`nBUILD FEHLGESCHLAGEN (ExitCode $code)"
    exit $code
}
