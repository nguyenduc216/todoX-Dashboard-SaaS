[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "artifacts\publish\todox-landing")
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\TodoX.Landing.csproj"
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)

if (Test-Path $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

dotnet publish $project -c Release -o $resolvedOutput --no-restore --no-build --self-contained false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host "TodoX Landing published to: $resolvedOutput"
