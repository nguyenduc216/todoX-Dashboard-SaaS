@echo off
title TodoX Deploy Agent - Sprint 2B
setlocal

set SCRIPT_DIR=%~dp0
set PS1=%SCRIPT_DIR%TodoX-Deploy-Agent.ps1

echo ==================================================
echo TodoX Deploy Agent - Sprint 2B
echo ==================================================
echo.
echo Please run this file as Administrator.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%"

echo.
pause
