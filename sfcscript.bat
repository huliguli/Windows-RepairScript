@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Windows Reparaturassistent
color 0F

rem =========================
rem  ANSI-Farben aktivieren
rem =========================
for /f %%a in ('echo prompt $E^| cmd') do set "ESC=%%a"
set  "R=%ESC%[0m"
set  "DIM=%ESC%[2m"
set  "WHT=%ESC%[97m"
set  "GRAY=%ESC%[38;2;140;145;165m"
set  "TEAL=%ESC%[38;2;78;205;196m"
set  "GREEN=%ESC%[38;2;152;195;121m"
set  "YELLOW=%ESC%[38;2;229;192;123m"
set  "RED=%ESC%[38;2;224;108;117m"
set  "BADGE=%ESC%[1;38;2;18;20;28;48;2;78;205;196m"
set  "OKBDG=%ESC%[1;38;2;18;20;28;48;2;152;195;121m"
set  "CHIP=%ESC%[1;38;2;18;20;28;48;2;78;205;196m"
set  "GCHIP=%ESC%[1;38;2;18;20;28;48;2;110;115;135m"

rem =========================
rem  Adminrechte sicherstellen (UAC-Abfrage)
rem =========================
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo   %YELLOW%Administratorrechte werden angefordert...%R%
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
echo.
echo   %BADGE%  WINDOWS REPARATURASSISTENT  %R%
echo   %GRAY%by Jonas   ^|   v2.0%R%
echo.
echo   %WHT%Was soll gemacht werden?%R%
echo.
echo   %CHIP% 1 %R%  Komplett          %GRAY%DISM + SFC%R%   %OKBDG% empfohlen %R%
echo   %CHIP% 2 %R%  Nur DISM          %GRAY%Komponentenspeicher%R%
echo   %CHIP% 3 %R%  Nur SFC           %GRAY%Systemdateien%R%
echo   %CHIP% 4 %R%  Komplett + CHKDSK %GRAY%Datentraeger pruefen%R%
echo.
echo   %GCHIP% 0 %R%  %GRAY%Beenden%R%
echo.
<nul set /p "=  %TEAL%Auswahl%R%  %DIM%(0-4)%R%  "
choice /c 12340 /n >nul
set "SEL=%errorlevel%"
if "%SEL%"=="5" exit /b 0
if "%SEL%"=="1" ( set "RUN_DISM=1" & set "RUN_SFC=1" & set "RUN_CHKDSK=0" )
if "%SEL%"=="2" ( set "RUN_DISM=1" & set "RUN_SFC=0" & set "RUN_CHKDSK=0" )
if "%SEL%"=="3" ( set "RUN_DISM=0" & set "RUN_SFC=1" & set "RUN_CHKDSK=0" )
if "%SEL%"=="4" ( set "RUN_DISM=1" & set "RUN_SFC=1" & set "RUN_CHKDSK=1" )

:MENU_POST
cls
echo.
echo   %BADGE%  AKTION DANACH  %R%
echo.
echo   %WHT%Was soll nach der Reparatur passieren?%R%
echo.
echo   %CHIP% 1 %R%  Nichts tun        %GRAY%nur Ergebnis anzeigen%R%   %OKBDG% empfohlen %R%
echo   %CHIP% 2 %R%  Herunterfahren
echo   %CHIP% 3 %R%  Neustart
echo.
<nul set /p "=  %TEAL%Auswahl%R%  %DIM%(1-3)%R%  "
choice /c 123 /n >nul
set "P=%errorlevel%"
if "%P%"=="1" set "POST_ACTION=none"
if "%P%"=="2" set "POST_ACTION=shutdown"
if "%P%"=="3" set "POST_ACTION=restart"

if not "%POST_ACTION%"=="none" (
    echo.
    set "DELAY="
    set /p "DELAY=  %TEAL%Verzoegerung in Sekunden%R% %DIM%[120]%R%  "
    if "!DELAY!"=="" set "DELAY=120"
)

rem =========================
rem  Reparatur ausfuehren
rem =========================
cls
echo.
echo   %BADGE%  REPARATUR LAEUFT  %R%   %GRAY%bitte warten%R%
echo   %GRAY%Protokoll: %LOG%%R%
echo.
>"%LOG%" echo [%DATE% %TIME%] Start - DISM=%RUN_DISM% SFC=%RUN_SFC% CHKDSK=%RUN_CHKDSK%

if "%RUN_DISM%"=="1" (
    echo   %TEAL%DISM%R% %DIM%ScanHealth%R%
    DISM /Online /Cleanup-Image /ScanHealth /LogPath:"%DISM_LOG%"
    echo   %TEAL%DISM%R% %DIM%RestoreHealth%R%
    DISM /Online /Cleanup-Image /RestoreHealth /LogPath:"%DISM_LOG%"
    >>"%LOG%" echo [%DATE% %TIME%] DISM RestoreHealth ExitCode=!errorlevel!
)

if "%RUN_SFC%"=="1" (
    echo   %TEAL%SFC%R% %DIM%scannow%R%
    sfc /scannow
    >>"%LOG%" echo [%DATE% %TIME%] SFC scannow ExitCode=!errorlevel!
)

if "%RUN_CHKDSK%"=="1" (
    echo   %TEAL%CHKDSK%R% %DIM%Pruefung fuer naechsten Neustart einplanen%R%
    echo J| chkdsk %SystemDrive% /f /r
    >>"%LOG%" echo [%DATE% %TIME%] CHKDSK eingeplant fuer %SystemDrive%
)

>>"%LOG%" echo [%DATE% %TIME%] Fertig
echo.
echo   %OKBDG%  FERTIG  %R%
echo.
echo   %GRAY%Protokoll:    %LOG%%R%
echo   %GRAY%DISM-Details: %DISM_LOG%%R%
echo   %GRAY%SFC-Details:  %windir%\Logs\CBS\CBS.log%R%
echo.

if "%POST_ACTION%"=="none" (
    pause
    exit /b 0
)

if "%POST_ACTION%"=="shutdown" (
    echo   %YELLOW%Der PC wird in %DELAY% Sekunden heruntergefahren.%R%
    echo   %GRAY%Abbrechen mit:  shutdown -a%R%
    shutdown -s -t %DELAY%
)

if "%POST_ACTION%"=="restart" (
    echo   %YELLOW%Der PC wird in %DELAY% Sekunden neu gestartet.%R%
    echo   %GRAY%Abbrechen mit:  shutdown -a%R%
    shutdown -r -t %DELAY%
)

pause
exit /b 0
