@echo off
rem TodoX deploy server setup. Double-click; it asks for Administrator rights.
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-TodoXDeployServer.ps1"
echo.
pause
