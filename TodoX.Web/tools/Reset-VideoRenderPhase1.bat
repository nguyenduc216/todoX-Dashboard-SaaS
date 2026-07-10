@echo off
setlocal
set SCRIPT_DIR=%~dp0
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Reset-VideoRenderPhase1.ps1"
if errorlevel 1 (
  echo.
  echo Repair failed.
  pause
  exit /b 1
)
echo.
echo Done.
pause
