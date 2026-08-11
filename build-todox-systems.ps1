param(
    [string]$Configuration = "Release",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$landingProject = Join-Path $root "TodoX.Landing\TodoX.Landing.csproj"
$dashboardProject = Join-Path $root "TodoX.Web\TodoX.Web.csproj"

if (-not (Test-Path -LiteralPath $landingProject)) {
    throw "Landing project not found: $landingProject"
}

if (-not (Test-Path -LiteralPath $dashboardProject)) {
    throw "Dashboard project not found: $dashboardProject"
}

$restoreArg = if ($NoRestore) { "--no-restore" } else { "" }

Write-Host "Building TodoX Landing ($Configuration)..." -ForegroundColor Yellow
dotnet build $landingProject -c $Configuration $restoreArg

Write-Host "Building TodoX Dashboard ($Configuration)..." -ForegroundColor Yellow
dotnet build $dashboardProject -c $Configuration $restoreArg

Write-Host "TodoX systems build completed." -ForegroundColor Green
