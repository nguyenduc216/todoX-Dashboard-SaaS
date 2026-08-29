[CmdletBinding()]
param(
    [string]$OutputPath = "D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$project = Join-Path $repoRoot "TodoX.Web\TodoX.Web.csproj"
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)

Push-Location $repoRoot
try {
    $sha = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sha)) { throw "Could not resolve Git commit." }

    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($branch)) { throw "Could not resolve Git branch." }

    $buildTime = (Get-Date).ToUniversalTime().ToString("o")

    dotnet publish $project `
        -c Release `
        -o $resolvedOutput `
        -p:BuildCommit=$sha `
        -p:BuildBranch=$branch `
        -p:BuildTimeUtc=$buildTime
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

    Write-Host "Commit: $sha"
    Write-Host "Branch: $branch"
    Write-Host "BuildTime: $buildTime"
    Write-Host "PublishPath: $resolvedOutput"
    Write-Host "Version verification URL: https://dashboard.todox.vn/system/version"
}
finally {
    Pop-Location
}
