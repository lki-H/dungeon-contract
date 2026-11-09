@echo off
setlocal
title Dungeon Contract 2.5 V8 LAN Host
pwsh.exe -NoLogo -NoProfile -File "%~dp0deploy\dc-v8-host.ps1"
if errorlevel 1 pause
