<#
.SYNOPSIS
  Server-side half of the TodoX deploy. Installed by Setup-TodoXDeployServer.ps1 into C:\TodoXDeploy and
  invoked over SSH by Deploy-TodoXRemote.ps1. Never runs SQL.

  1. Verify the uploaded zip's SHA-256.
  2. Replace this target's previous backup (only the last version is kept), check free space, stop the IIS
     app pool, back up the site folder (without keys, logs, uploads).
  3. Copy the new build in, keeping appsettings*.json, web.config and keys\ untouched.
  4. Start the pool and probe /system/version until it reports the expected commit.
  5. On any failure after step 2, restore the backup and start the pool again.
#>
param(
    # Target name registered by Setup-TodoXDeployServer.ps1 (e.g. dashboard, landing).
    [Parameter(Mandatory = $true)][string] $Target,
    [Parameter(Mandatory = $true)][string] $Package,
    [Parameter(Mandatory = $true)][string] $Sha256,
    [string] $ExpectedCommit,
    # Optional: uploaded license signing key, installed into <site>\keys only if none exists there yet.
    [string] $InstallKey
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 defaults to TLS 1.0, which modern HTTPS sites reject during the health check.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Config = Get-Content (Join-Path $Root 'deploy-config.json') -Raw | ConvertFrom-Json
Import-Module WebAdministration

$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$LogFile = Join-Path $Root "logs\apply-$Target-$Stamp.log"
New-Item -ItemType Directory -Force -Path (Split-Path $LogFile) | Out-Null
function Log([string] $m) {
    $line = '[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m
    # Write-Host, not Write-Output: output inside functions such as Test-Health would otherwise become part of
    # their return value and make a failed health check look truthy.
    Write-Host $line
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
}

$T = $Config.Targets.$Target
if (-not $T) { throw "Target '$Target' is not registered on this server. Run Setup-TodoXDeployServer.bat again to add it." }
$Site = $T.SitePath
$Pool = $T.AppPool
Log "Target: $Target -> site=$($T.SiteName) folder=$Site pool=$Pool"
$Preserve = @('appsettings.json', 'appsettings.Production.json', 'appsettings.Development.json', 'web.config')
$PreserveDirs = @('keys', 'logs', 'wwwroot\uploads')

function Wait-PoolState([string] $State) {
    for ($i = 0; $i -lt 30; $i++) {
        if ((Get-WebAppPoolState -Name $Pool).Value -eq $State) { return }
        Start-Sleep -Seconds 1
    }
    throw "App pool '$Pool' did not reach state $State"
}

function Test-Health {
    if (-not $T.HealthUrl) { return $true }
    for ($i = 0; $i -lt 20; $i++) {
        try {
            $r = Invoke-RestMethod -Uri $T.HealthUrl -TimeoutSec 15 -UseBasicParsing
            if ($r -is [string]) {
                # Plain page (e.g. landing home): HTTP 200 is the health signal; no commit to compare.
                Log 'Health OK: HTTP 200 (no version endpoint)'
                return $true
            }
            if (-not $ExpectedCommit -or ($r.commit -and $r.commit.StartsWith($ExpectedCommit))) {
                Log "Health OK: version=$($r.version) commit=$($r.commit)"
                return $true
            }
            Log "Health: running commit $($r.commit), waiting for $ExpectedCommit"
        }
        catch { Log "Health probe failed: $($_.Exception.Message)" }
        Start-Sleep -Seconds 3
    }
    return $false
}

function Copy-Release([string] $From, [string] $To) {
    # Mirror the build but never touch production config, keys or user uploads.
    $xf = $Preserve
    $xd = $PreserveDirs | ForEach-Object { Join-Path $To $_ }
    & robocopy $From $To /MIR /R:2 /W:2 /NFL /NDL /NP /XF @xf /XD @xd | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
}

Log "Package: $Package"
$actual = (Get-FileHash $Package -Algorithm SHA256).Hash
if ($actual -ne $Sha256.ToUpperInvariant()) { throw "Checksum mismatch. Expected $Sha256, got $actual" }
Log "Checksum OK: $actual"

$staging = Join-Path $Root "staging\$Stamp"
Expand-Archive -Path $Package -DestinationPath $staging -Force
if (-not (Test-Path (Join-Path $staging $T.MainDll))) { throw "Package does not contain $($T.MainDll)" }

$backup = Join-Path $Config.BackupRoot "$Target-$Stamp"
$backupDirs = $PreserveDirs | ForEach-Object { Join-Path $Site $_ }

# Only the last version is kept: drop this target's older backups first, then make sure the backup fits while
# leaving room for everything else on that drive (the database lives on C: too).
New-Item -ItemType Directory -Force -Path $Config.BackupRoot | Out-Null
Get-ChildItem $Config.BackupRoot -Directory -Filter "$Target-*" | ForEach-Object {
    Log "Removing previous backup $($_.Name)"
    Remove-Item $_.FullName -Recurse -Force
}
$needed = (Get-ChildItem $Site -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $f = $_.FullName; -not ($backupDirs | Where-Object { $f.StartsWith($_ + '\', [StringComparison]::OrdinalIgnoreCase) }) } |
    Measure-Object Length -Sum).Sum
$drive = (Get-Item $Config.BackupRoot).PSDrive
$free = (Get-PSDrive $drive.Name).Free
$reserve = 5GB
Log ('Backup needs {0:N1} GB; {1}: has {2:N1} GB free (keeps {3:N0} GB in reserve)' -f ($needed / 1GB), $drive.Name, ($free / 1GB), ($reserve / 1GB))
if ($free - $needed -lt $reserve) {
    Log "ERROR: not enough free space on $($drive.Name): for the backup. Nothing was changed."
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    exit 1
}

$stopped = $false
$backupOk = $false
try {
    Log "Stopping app pool '$Pool'"
    if ((Get-WebAppPoolState -Name $Pool).Value -ne 'Stopped') { Stop-WebAppPool -Name $Pool }
    Wait-PoolState 'Stopped'
    $stopped = $true

    Log "Backup -> $backup"
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    & robocopy $Site $backup /MIR /R:2 /W:2 /NFL /NDL /NP /XD @backupDirs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Backup failed with robocopy exit code $LASTEXITCODE" }
    $backupOk = $true

    Log 'Copying new build'
    Copy-Release $staging $Site

    if ($InstallKey) {
        $keyTarget = Join-Path $Site 'keys\editvideo-license-signing.pem'
        if (Test-Path $keyTarget) {
            Log 'Signing key already present; left unchanged'
        }
        else {
            New-Item -ItemType Directory -Force -Path (Split-Path $keyTarget) | Out-Null
            Move-Item $InstallKey $keyTarget
            Log 'Signing key installed'
        }
    }

    Log "Starting app pool '$Pool'"
    Start-WebAppPool -Name $Pool
    Wait-PoolState 'Started'
    $stopped = $false

    if (-not (Test-Health)) { throw 'Health check failed after deploy' }
    Log "RESULT: DEPLOYED $ExpectedCommit"
}
catch {
    Log "ERROR: $($_.Exception.Message) -- rolling back"
    if (-not $stopped -and (Get-WebAppPoolState -Name $Pool).Value -ne 'Stopped') { Stop-WebAppPool -Name $Pool; Wait-PoolState 'Stopped' }
    if ($backupOk) { Copy-Release $backup $Site; Log 'Backup restored' }
    else {
        # The site was not touched yet; mirroring an incomplete backup over it would delete files.
        Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
        Log 'Backup incomplete: site left as it was'
    }
    Start-WebAppPool -Name $Pool
    Log 'RESULT: ROLLED BACK'
    exit 1
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
