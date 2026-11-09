@echo off
pwsh.exe -NoLogo -NoProfile -File "%~dp0deploy\dc-v8-local-launch.ps1"
if errorlevel 1 pause
