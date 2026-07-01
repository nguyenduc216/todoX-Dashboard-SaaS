@echo off
setlocal

title TodoX SaaS - Deploy TodoX.Web to IIS

echo ==================================================
echo  TodoX SaaS - Deploy TodoX.Web to IIS
echo ==================================================
echo.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Please run this file as Administrator.
    echo Right-click this BAT file and choose "Run as administrator".
    echo.
    pause
    exit /b 1
)

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%01_Deploy-TodoX-Web-To-IIS.ps1"

if not exist "%PS1%" (
    echo [ERROR] PowerShell deploy script not found:
    echo %PS1%
    echo.
    echo Please copy this BAT file into the same folder as:
    echo 01_Deploy-TodoX-Web-To-IIS.ps1
    echo.
    pause
    exit /b 1
)

echo [INFO] Running deploy script...
echo %PS1%
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS1%"

set "EXITCODE=%ERRORLEVEL%"
echo.
echo ==================================================
if "%EXITCODE%"=="0" (
    echo  Deploy completed successfully.
) else (
    echo  Deploy failed. Exit code: %EXITCODE%
    echo  Please check the log folder for details.
)
echo ==================================================
echo.
pause
exit /b %EXITCODE%
