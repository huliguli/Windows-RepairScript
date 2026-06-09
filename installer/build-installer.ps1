# Baut den Installer aus dem aktuellen bin\-Ordner (setzt einen Release-Build voraus).
# Voraussetzung: Inno Setup 6 installiert (https://jrsoftware.org/isdl.php).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$exe = Join-Path $root 'bin\WindowsWartung.exe'
if (-not (Test-Path $exe)) { throw "bin\WindowsWartung.exe fehlt - zuerst:  .\build.ps1 -Release" }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) nicht gefunden. Installieren: https://jrsoftware.org/isdl.php" }

if (-not (Test-Path (Join-Path $root 'assets\wizard.bmp'))) {
    & (Join-Path $root 'tools\generate-installer-art.ps1')
}

$ver = (Get-Item $exe).VersionInfo.FileVersion
& $iscc "/DMyAppVersion=$ver" (Join-Path $root 'installer\WindowsWartung.iss')

"`nInstaller: $(Join-Path $root 'dist\WindowsWartung-Setup.exe')"
