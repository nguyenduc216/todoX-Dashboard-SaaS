@echo off
rem TodoX deploy server follow-up: landing health URL, SSH checks, router port 22022 via UPnP.
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Fix-TodoXDeployServer.ps1"
echo.
pause
