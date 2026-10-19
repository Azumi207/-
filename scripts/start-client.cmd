@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
title 帧语 - 启动
if not exist "%~dp0coreclr.dll" (
  echo 请先将整个 ZIP 解压到文件夹，再启动；不要直接在压缩包里运行。
  echo 程序已内置 .NET，无需另行安装。请勿只复制 EXE。
  pause
  exit /b 1
)
echo 正在准备帧语。首次使用需要联网下载媒体组件，请稍候。
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-ffmpeg.ps1" -Destination "%~dp0Engine"
if errorlevel 1 (
  echo 媒体组件准备失败。请检查网络、确保解压目录可写，然后重新双击此文件。
  echo 校验失败时请下载维护者更新的版本，不要关闭校验。
  pause
  exit /b 1
)
start "" "%~dp0Frameflow.exe" %*
