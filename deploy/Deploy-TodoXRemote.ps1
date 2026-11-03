<#
.SYNOPSIS
  Build, test, package and deploy TodoX.Web to the production server over SSH, then verify the version.

.DESCRIPTION
  Requires deploy\remote.local.json (not committed):
    { "Host": "1.2.3.4", "Port": 22, "User": "Administrator", "KeyPath": "~/.ssh/todox_deploy", "RemoteRoot": "C:/TodoXDeploy" }
  The server must have been prepared once with deploy\server\Setup-TodoXDeployServer.bat.

  Steps: tests -> Release build -> publish -> zip + SHA-256 -> scp -> remote Apply-TodoXRelease.ps1
  (backup, swap, health check on /system/version, automatic rollback on failure).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File deploy/Deploy-TodoXRemote.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File deploy/Deploy-TodoXRemote.ps1 -CheckOnly
#>
param(
    [switch] $CheckOnly,
    [switch] $SkipTests,
    [switch] $InstallSigningKey,
    [string[]] $TestProjects = @('TodoX.EditVideoTool.Tests/TodoX.EditVideoTool.Tests.csproj')
)

$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ConfigPath = Join-Path $PSScriptRoot 'remote.local.json'
if (-not (Test-Path $ConfigPath)) { throw "Missing $ConfigPath (see the header of this script)." }
$Cfg = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$Key = $Cfg.KeyPath -replace '^~', $env:USERPROFILE
$Port = if ($Cfg.Port) { $Cfg.Port } else { 22 }
$Target = "$($Cfg.User)@$($Cfg.Host)"
$RemoteRoot = if ($Cfg.RemoteRoot) { $Cfg.RemoteRoot } else { 'C:/TodoXDeploy' }
$SshOpts = @('-i', $Key, '-p', "$Port", '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=accept-new', '-o', 'ConnectTimeout=15')
$ScpOpts = @('-i', $Key, '-P', "$Port", '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=accept-new')

function Step([string] $t) { Write-Host "== $t" -ForegroundColor Cyan }
function Run([string] $exe, [string[]] $argv) {
    & $exe @argv
    if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE" }
}
function Remote([string] $command) {
    # The server's SSH shell is PowerShell (set by the setup script), so the command runs as-is.
    & ssh @SshOpts $Target $command | ForEach-Object { Write-Host "  server> $_" }
    return $LASTEXITCODE
}

Step "Connect ${Target}:$Port"
if ((Remote "if (Test-Path $RemoteRoot/deploy-config.json) { 'connected to ' + `$env:COMPUTERNAME } else { exit 3 }") -ne 0) {
    throw 'SSH connection failed or the server was not prepared with Setup-TodoXDeployServer.bat.'
}
if ($CheckOnly) { Write-Host 'RESULT: CONNECTION OK'; exit 0 }

Push-Location $Repo
try {
    $commit = (git rev-parse HEAD).Trim()
    $dirty = git status --porcelain --untracked-files=no
    if ($dirty) { throw "Working tree has uncommitted changes; commit before deploying.`n$dirty" }

    if (-not $SkipTests) {
        foreach ($p in $TestProjects) { Step "Test $p"; Run 'dotnet' @('test', $p, '-c', 'Release', '--nologo') }
    }

    Step 'Publish TodoX.Web (Release)'
    $out = Join-Path $Repo 'publish\todoX-dashboard'
    Run 'dotnet' @('publish', 'TodoX.Web/TodoX.Web.csproj', '-c', 'Release', '-o', $out, '--nologo')
    if (-not (Test-Path (Join-Path $out 'TodoX.Web.dll'))) { throw 'Publish output missing TodoX.Web.dll' }

    Step 'Package'
    $zip = Join-Path $env:TEMP "todox-web-$($commit.Substring(0, 7)).zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    # Production config and secrets stay on the server; never ship local ones.
    $items = Get-ChildItem $out | Where-Object { $_.Name -notmatch '^appsettings\..*\.json$' -and $_.Name -ne 'keys' }
    Compress-Archive -Path $items.FullName -DestinationPath $zip -CompressionLevel Optimal
    $sha = (Get-FileHash $zip -Algorithm SHA256).Hash
    Write-Host "  $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB) sha256=$sha"

    Step 'Upload'
    $remoteZip = "$RemoteRoot/incoming/$(Split-Path $zip -Leaf)"
    Run 'scp' ($ScpOpts + @($zip, "${Target}:$remoteZip"))
    $keyArg = ''
    if ($InstallSigningKey) {
        $pem = Join-Path $Repo 'TodoX.Web\keys\editvideo-license-signing.pem'
        Run 'scp' ($ScpOpts + @($pem, "${Target}:$RemoteRoot/incoming/editvideo-license-signing.pem"))
        $keyArg = " -InstallKey $RemoteRoot/incoming/editvideo-license-signing.pem"
    }

    Step 'Apply on server'
    $code = Remote "powershell -NoProfile -ExecutionPolicy Bypass -File $RemoteRoot/Apply-TodoXRelease.ps1 -Package $remoteZip -Sha256 $sha -ExpectedCommit $commit$keyArg"
    if ($code -ne 0) { throw "Remote apply failed (exit $code); server rolled back to the previous build." }
    Write-Host "RESULT: DEPLOYED $commit" -ForegroundColor Green
}
finally {
    Pop-Location
}
