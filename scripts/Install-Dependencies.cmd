@echo off
setlocal
cd /d "%~dp0"
for %%I in ("%~dp0.") do set "APP_ROOT=%%~fI"
powershell -ExecutionPolicy Bypass -File "%~dp0Install-Dependencies.ps1" -AppRoot "%APP_ROOT%"
exit /b %ERRORLEVEL%
