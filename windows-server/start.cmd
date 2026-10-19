@echo off
call "%~dp0..\server\start.cmd" %*
exit /b %errorlevel%
