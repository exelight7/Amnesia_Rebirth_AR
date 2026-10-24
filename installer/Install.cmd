@echo off
cd /d "%~dp0"
echo Amnesia Rebirth Arabic 1.0.0 - Manual installation
echo Enter your game folder when PowerShell asks for GamePath.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Manual.ps1" %*
pause
