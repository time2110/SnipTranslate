@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Package.ps1" %*
exit /b %ERRORLEVEL%
