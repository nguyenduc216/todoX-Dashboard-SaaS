@echo off
setlocal enabledelayedexpansion
title TodoX Deploy CMD Only

set REPO=D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS
set PROJECT=%REPO%\TodoX.Web\TodoX.Web.csproj
set PUBLISH=%REPO%\TodoX.Web\publish
set LEGACY=%REPO%\artifacts\publish\TodoX.Web
set SITE=TodoX Dashboard SaaS
set APPCMD=%windir%\System32\inetsrv\appcmd.exe
set LOGDIR=%REPO%\logs

if not exist "%LOGDIR%" mkdir "%LOGDIR%"
set LOG=%LOGDIR%\deploy-cmd-only.log

echo =============================================== > "%LOG%"
echo TodoX Deploy CMD Only >> "%LOG%"
echo =============================================== >> "%LOG%"

echo ===============================================
echo TodoX Deploy CMD Only
echo ===============================================
echo.

echo Repo: %REPO%
echo Project: %PROJECT%
echo Publish: %PUBLISH%
echo.

if not exist "%PROJECT%" (
  echo ERROR: Project file not found: %PROJECT%
  echo ERROR: Project file not found: %PROJECT% >> "%LOG%"
  pause
  exit /b 1
)

if not exist "%APPCMD%" (
  echo ERROR: appcmd not found: %APPCMD%
  echo ERROR: appcmd not found: %APPCMD% >> "%LOG%"
  pause
  exit /b 1
)

echo [1] Stop IIS site...
"%APPCMD%" stop site /site.name:"%SITE%" >> "%LOG%" 2>&1

echo [2] Stop IIS...
iisreset /stop >> "%LOG%" 2>&1

echo [3] Kill locked processes...
taskkill /F /IM w3wp.exe >> "%LOG%" 2>&1
taskkill /F /IM iisexpress.exe >> "%LOG%" 2>&1
taskkill /F /IM TodoX.Web.exe >> "%LOG%" 2>&1

echo [4] Remove legacy artifacts...
if exist "%LEGACY%" rmdir /s /q "%LEGACY%" >> "%LOG%" 2>&1

echo [5] Remove old publish...
if exist "%PUBLISH%" rmdir /s /q "%PUBLISH%" >> "%LOG%" 2>&1

if exist "%PUBLISH%" (
  echo ERROR: Cannot remove publish folder. Close Visual Studio/browser/IIS and run again.
  echo ERROR: Cannot remove publish folder: %PUBLISH% >> "%LOG%"
  pause
  exit /b 1
)

mkdir "%PUBLISH%" >> "%LOG%" 2>&1

echo [6] dotnet restore...
dotnet restore "%PROJECT%" >> "%LOG%" 2>&1
if errorlevel 1 goto DOTNET_ERROR

echo [7] dotnet clean Release...
dotnet clean "%PROJECT%" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto DOTNET_ERROR

echo [8] dotnet build Release...
dotnet build "%PROJECT%" -c Release --no-restore >> "%LOG%" 2>&1
if errorlevel 1 goto DOTNET_ERROR

echo [9] dotnet publish Release...
dotnet publish "%PROJECT%" -c Release -o "%PUBLISH%" --no-build >> "%LOG%" 2>&1
if errorlevel 1 goto DOTNET_ERROR

echo [10] Validate publish...
if not exist "%PUBLISH%\web.config" goto VALIDATE_ERROR
if not exist "%PUBLISH%\TodoX.Web.dll" goto VALIDATE_ERROR
if not exist "%PUBLISH%\TodoX.Web.exe" goto VALIDATE_ERROR
if not exist "%PUBLISH%\wwwroot" goto VALIDATE_ERROR

echo [11] Set IIS physical path...
"%APPCMD%" set vdir "%SITE%/" /physicalPath:"%PUBLISH%" >> "%LOG%" 2>&1
if errorlevel 1 goto IIS_ERROR

echo [12] Start IIS...
iisreset /start >> "%LOG%" 2>&1

echo [13] Start IIS site...
"%APPCMD%" start site /site.name:"%SITE%" >> "%LOG%" 2>&1

echo [14] Show IIS path...
"%APPCMD%" list vdir "%SITE%/" /text:* >> "%LOG%" 2>&1

echo.
echo ===============================================
echo SUCCESS
echo Publish folder:
echo %PUBLISH%
echo.
echo Log:
echo %LOG%
echo ===============================================
pause
exit /b 0

:DOTNET_ERROR
echo.
echo ERROR: dotnet command failed. See log:
echo %LOG%
pause
exit /b 1

:VALIDATE_ERROR
echo.
echo ERROR: Publish validation failed. See log:
echo %LOG%
pause
exit /b 1

:IIS_ERROR
echo.
echo ERROR: IIS appcmd failed. See log:
echo %LOG%
pause
exit /b 1
