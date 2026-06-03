@echo off
rem Doppelklick-Starter: baut die Release-Version (mit Admin-Manifest) nach bin\
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Release
echo.
pause
