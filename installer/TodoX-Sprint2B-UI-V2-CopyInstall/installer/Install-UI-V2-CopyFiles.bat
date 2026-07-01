@echo off
setlocal
title TodoX UI V2 Copy Installer

set REPO=D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS
set SRC=%~dp0..\copy-to-repo

echo ===============================================
echo TodoX UI V2 Copy Installer
echo ===============================================
echo.
echo This installer will copy prepared files to:
echo %REPO%
echo.
echo Files:
echo - TodoX.Web\Components\Layout\MainLayout.razor
echo - TodoX.Web\wwwroot\css\todox-theme.css
echo.
pause

if not exist "%REPO%\TodoX.Web\TodoX.Web.csproj" (
  echo ERROR: Repo path is wrong.
  pause
  exit /b 1
)

if not exist "%REPO%\backup" mkdir "%REPO%\backup"
set BAK=%REPO%\backup\ui-v2-copy
if exist "%BAK%" rmdir /s /q "%BAK%"
mkdir "%BAK%"
mkdir "%BAK%\Components\Layout"
mkdir "%BAK%\wwwroot\css"

copy /Y "%REPO%\TodoX.Web\Components\Layout\MainLayout.razor" "%BAK%\Components\Layout\MainLayout.razor.bak" >nul
copy /Y "%REPO%\TodoX.Web\wwwroot\css\todox-theme.css" "%BAK%\wwwroot\css\todox-theme.css.bak" >nul

xcopy "%SRC%\*" "%REPO%\" /E /Y /I

echo.
echo Build check...
cd /d "%REPO%"
dotnet build ".\TodoX.Web\TodoX.Web.csproj"

echo.
echo Done.
echo Backup folder:
echo %BAK%
pause
