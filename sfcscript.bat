@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Windows Reparaturassistent
color 0A

rem =========================
rem  Adminrechte sicherstellen (UAC-Abfrage)
rem =========================
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Administratorrechte werden angefordert...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs" >nul 2>&1
    exit /b
)

rem =========================
rem  Pfade und Protokoll
rem =========================
set "SCRIPT_DIR=%~dp0"
set "LOG_DIR=%SCRIPT_DIR%logs"
if not exist "%LOG_DIR%" md "%LOG_DIR%" >nul 2>&1
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss"') do set "STAMP=%%i"
set "LOG=%LOG_DIR%\reparatur_%STAMP%.log"
set "DISM_LOG=%LOG_DIR%\dism_%STAMP%.log"

rem =========================
rem  Standardwerte
rem =========================
set "RUN_DISM=1"
set "RUN_SFC=1"
set "RUN_CHKDSK=0"
set "POST_ACTION=none"
set "DELAY=120"

:MENU_MAIN
cls
echo ==========================================
echo         Windows Reparaturassistent
echo         by Jonas   ^|   v2.0
echo ==========================================
echo.
echo   Was soll gemacht werden?
echo.
echo    [1] Komplett  (DISM + SFC)          - empfohlen
echo    [2] Nur DISM  (Komponentenspeicher)
echo    [3] Nur SFC   (Systemdateien)
echo    [4] Komplett + CHKDSK (Datentraeger pruefen)
echo.
echo    [0] Beenden
echo.
choice /c 12340 /n /m "Deine Auswahl: "
set "SEL=%errorlevel%"
if "%SEL%"=="5" exit /b 0
if "%SEL%"=="1" ( set "RUN_DISM=1" & set "RUN_SFC=1" & set "RUN_CHKDSK=0" )
if "%SEL%"=="2" ( set "RUN_DISM=1" & set "RUN_SFC=0" & set "RUN_CHKDSK=0" )
if "%SEL%"=="3" ( set "RUN_DISM=0" & set "RUN_SFC=1" & set "RUN_CHKDSK=0" )
if "%SEL%"=="4" ( set "RUN_DISM=1" & set "RUN_SFC=1" & set "RUN_CHKDSK=1" )

:MENU_POST
cls
echo ==========================================
echo         Aktion nach der Reparatur
echo ==========================================
echo.
echo    [1] Nichts tun (Ergebnis anzeigen)   - empfohlen
echo    [2] Herunterfahren
echo    [3] Neustart
echo.
choice /c 123 /n /m "Deine Auswahl: "
set "P=%errorlevel%"
if "%P%"=="1" set "POST_ACTION=none"
if "%P%"=="2" set "POST_ACTION=shutdown"
if "%P%"=="3" set "POST_ACTION=restart"

if not "%POST_ACTION%"=="none" (
    echo.
    set "DELAY="
    set /p "DELAY=Verzoegerung in Sekunden [120]: "
    if "!DELAY!"=="" set "DELAY=120"
)

rem =========================
rem  Reparatur ausfuehren
rem =========================
cls
echo ==========================================
echo   Reparatur laeuft - bitte warten
echo ==========================================
echo   Protokoll: %LOG%
echo.
>"%LOG%" echo [%DATE% %TIME%] Start - DISM=%RUN_DISM% SFC=%RUN_SFC% CHKDSK=%RUN_CHKDSK%

if "%RUN_DISM%"=="1" (
    echo --- DISM: ScanHealth ---
    DISM /Online /Cleanup-Image /ScanHealth /LogPath:"%DISM_LOG%"
    echo --- DISM: RestoreHealth ---
    DISM /Online /Cleanup-Image /RestoreHealth /LogPath:"%DISM_LOG%"
    >>"%LOG%" echo [%DATE% %TIME%] DISM RestoreHealth ExitCode=!errorlevel!
)

if "%RUN_SFC%"=="1" (
    echo --- SFC: scannow ---
    sfc /scannow
    >>"%LOG%" echo [%DATE% %TIME%] SFC scannow ExitCode=!errorlevel!
)

if "%RUN_CHKDSK%"=="1" (
    echo --- CHKDSK: Pruefung fuer naechsten Neustart einplanen ---
    echo J| chkdsk %SystemDrive% /f /r
    >>"%LOG%" echo [%DATE% %TIME%] CHKDSK eingeplant fuer %SystemDrive%
)

>>"%LOG%" echo [%DATE% %TIME%] Fertig
echo.
echo ==========================================
echo   Fertig.
echo   Protokoll:    %LOG%
echo   DISM-Details: %DISM_LOG%
echo   SFC-Details:  %windir%\Logs\CBS\CBS.log
echo ==========================================
echo.

if "%POST_ACTION%"=="none" (
    pause
    exit /b 0
)

if "%POST_ACTION%"=="shutdown" (
    echo Der PC wird in %DELAY% Sekunden heruntergefahren.
    echo Abbrechen mit:   shutdown -a
    shutdown -s -t %DELAY%
)

if "%POST_ACTION%"=="restart" (
    echo Der PC wird in %DELAY% Sekunden neu gestartet.
    echo Abbrechen mit:   shutdown -a
    shutdown -r -t %DELAY%
)

pause
exit /b 0
