@echo off
cd /d "%~dp0..\server"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\server\build-windows.ps1" %*
if errorlevel 1 pause
