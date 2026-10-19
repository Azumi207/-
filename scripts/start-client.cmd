@echo off
cd /d "%~dp0"
dotnet --list-runtimes 2>nul | findstr /C:"Microsoft.WindowsDesktop.App 9." >nul
if errorlevel 1 (
  echo Install .NET 9 Desktop Runtime x64 first: https://dotnet.microsoft.com/download/dotnet/9.0
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-ffmpeg.ps1" -Destination "%~dp0Engine"
if errorlevel 1 (
  pause
  exit /b 1
)
"%~dp0Frameflow.exe" %*
