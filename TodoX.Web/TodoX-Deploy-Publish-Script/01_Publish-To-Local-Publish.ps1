param(
[string]$Project="D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\TodoX.Web\TodoX.Web.csproj"
)

$ErrorActionPreference="Stop"

$projDir=Split-Path $Project
$publish=Join-Path $projDir "publish"

Write-Host "===================================" -ForegroundColor Yellow
Write-Host "TodoX Publish Script" -ForegroundColor Yellow
Write-Host "===================================" -ForegroundColor Yellow

Push-Location $projDir

if(Test-Path $publish){
    Write-Host "Cleaning publish..." -ForegroundColor Cyan
    Get-ChildItem $publish -Force | Remove-Item -Force -Recurse
}else{
    New-Item -ItemType Directory -Path $publish | Out-Null
}

Write-Host "Cleaning project..."
dotnet clean

Write-Host "Publishing Release..."
dotnet publish -c Release -o $publish

if($LASTEXITCODE -ne 0){
    throw "Publish failed."
}

Write-Host ""
Write-Host "Publish completed:" -ForegroundColor Green
Write-Host $publish -ForegroundColor Green

Pop-Location
