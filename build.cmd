@echo off
rem Doppelklick-Starter fuer build.ps1 (umgeht .ps1-Dateiverknuepfung + Execution-Policy)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
echo.
pause
