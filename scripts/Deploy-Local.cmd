@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%" >nul
if errorlevel 1 (
    echo Failed to switch to the local deploy script directory.
    exit /b 1
)

powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Deploy-Local.ps1" %*
set "EXIT_CODE=%errorlevel%"
popd >nul
if not "%EXIT_CODE%"=="0" (
    echo.
    echo Local repo deployments are disabled. Review the messages above, then press any key to close this window.
    pause >nul
)
exit /b %EXIT_CODE%
