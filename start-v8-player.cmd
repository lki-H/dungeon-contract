@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0dc-v8-player.ps1"
if errorlevel 1 pause
