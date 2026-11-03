<#
.SYNOPSIS
  Deploy one or more TodoX projects to production: pending SQL first (tested), then code over SSH.

.DESCRIPTION
  Which project goes where is data, not memory:
    deploy/targets.json        (committed)  target -> project, DLL, server name, logical database, SQL folders
    deploy/remote.local.json   (local only) server name -> host, port, user, SSH key
    C:/TodoXDeploy/deploy-config.json on the server (written by Setup-TodoXDeployServer.bat)
                                             target -> IIS site folder, app pool, health URL
    tools/prod-sql/applied-ledger.tsv  (committed) SQL scripts already applied, per database
    deploy/deploy-ledger.tsv           (committed) last deployed commit per target

  remote.local.json example:
    { "servers": { "prod": { "Host": "1.2.3.4", "Port": 22, "User": "Administrator", "KeyPath": "~/.ssh/todox_deploy" } } }

  -Target auto picks every target whose pathsInRepo changed since its last deployed commit.

  Per target: tests -> pending SQL (each: dry run + test, then apply + test in one transaction)
  -> publish -> zip + SHA-256 -> scp -> Apply-TodoXRelease.ps1 on the server (backup, swap,
  health check, automatic rollback) -> ledger entry.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File deploy/Deploy-TodoXRemote.ps1 -Target auto -Plan
  powershell -NoProfile -ExecutionPolicy Bypass -File deploy/Deploy-TodoXRemote.ps1 -Target dashboard
  powershell -NoProfile -ExecutionPolicy Bypass -File deploy/Deploy-TodoXRemote.ps1 -CheckOnly
#>
param(
    [string[]] $Target = @('auto'),
    [switch] $Plan,
    [switch] $CheckOnly,
    [switch] $SkipTests,
    [switch] $InstallSigningKey
)

$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Registry = Get-Content (Join-Path $PSScriptRoot 'targets.json') -Raw | ConvertFrom-Json
$LocalPath = Join-Path $PSScriptRoot 'remote.local.json'
$DeployLedger = Join-Path $PSScriptRoot 'deploy-ledger.tsv'
$SqlLedger = Join-Path $Repo 'tools\prod-sql\applied-ledger.tsv'

function Step([string] $t) { Write-Host "== $t" -ForegroundColor Cyan }
function Run([string] $exe, [string[]] $argv) {
    & $exe @argv
    if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE" }
}

function Get-Server([string] $name) {
    if (-not (Test-Path $LocalPath)) { throw "Missing $LocalPath (see the header of this script)." }
    $s = (Get-Content $LocalPath -Raw | ConvertFrom-Json).servers.$name
    if (-not $s) { throw "Server '$name' is not defined in $LocalPath" }
    $key = $s.KeyPath -replace '^~', $env:USERPROFILE
    $port = if ($s.Port) { $s.Port } else { 22 }
    return [pscustomobject]@{
        Name = $name; Login = "$($s.User)@$($s.Host)"; Port = $port
        RemoteRoot = $(if ($s.RemoteRoot) { $s.RemoteRoot } else { 'C:/TodoXDeploy' })
        Ssh = @('-i', $key, '-p', "$port", '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=accept-new', '-o', 'ConnectTimeout=15')
        Scp = @('-i', $key, '-P', "$port", '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=accept-new')
    }
}

function Remote($server, [string] $command) {
    # The server's SSH shell is PowerShell (set by the setup script), so the command runs as-is.
    & ssh @($server.Ssh) $server.Login $command | ForEach-Object { Write-Host "  server> $_" }
    return $LASTEXITCODE
}

function Get-LastDeployed([string] $name) {
    if (-not (Test-Path $DeployLedger)) { return $null }
    $row = Get-Content $DeployLedger | Where-Object { $_ -match "^\S+\t$name\t" } | Select-Object -Last 1
    if ($row) { return $row.Split("`t")[2] }
    return $null
}

function Get-ChangedFiles([string] $since) {
    if (-not $since) { return $null }  # never deployed through this tool
    return @(git -C $Repo diff --name-only "$since..HEAD")
}

function Get-PendingSql($t) {
    $pending = @()
    foreach ($dir in @($t.sqlDirs)) {
        if (-not $dir) { continue }
        foreach ($f in Get-ChildItem (Join-Path $Repo $dir) -Recurse -Filter '*.sql' | Where-Object { $_.Name -notlike '*.test.sql' }) {
            $test = [IO.Path]::ChangeExtension($f.FullName, '.test.sql')
            if (-not (Test-Path $test)) { continue }  # legacy manual scripts: never run automatically
            $rel = $f.FullName.Substring($Repo.Length + 1).Replace('\', '/')
            $sha = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
            $done = (Test-Path $SqlLedger) -and (Select-String -Path $SqlLedger -SimpleMatch "`t$($t.database)`t$rel`t$sha" -Quiet)
            if (-not $done) { $pending += $rel }
        }
    }
    return $pending
}

# ---------------------------------------------------------------- resolve targets
$names = @()
if ($Target -contains 'auto') {
    foreach ($p in $Registry.targets.PSObject.Properties) {
        $changed = Get-ChangedFiles (Get-LastDeployed $p.Name)
        $hit = $null -eq $changed -or @($changed | Where-Object { $f = $_; @($p.Value.pathsInRepo) | Where-Object { $f.StartsWith($_) } }).Count -gt 0
        if ($hit -or (Get-PendingSql $p.Value).Count -gt 0) { $names += $p.Name }
    }
}
else {
    $names = $Target
}
foreach ($n in $names) { if (-not $Registry.targets.$n) { throw "Unknown target '$n' (see deploy/targets.json)." } }

# Untested SQL changed since the last deploy is a hard stop: every production script needs a .test.sql.
foreach ($n in $names) {
    $t = $Registry.targets.$n
    $changed = Get-ChangedFiles (Get-LastDeployed $n)
    foreach ($f in @($changed)) {
        if ($f -and $f.EndsWith('.sql') -and -not $f.EndsWith('.test.sql') -and (@($t.sqlDirs) | Where-Object { $_ -and $f.StartsWith($_) })) {
            if (-not (Test-Path (Join-Path $Repo ([IO.Path]::ChangeExtension($f, '.test.sql'))))) {
                throw "SQL changed without a test file: $f. Add $([IO.Path]::ChangeExtension($f, '.test.sql')) before deploying."
            }
        }
    }
}

$commit = (git -C $Repo rev-parse HEAD).Trim()
Step "Deploy plan for $($commit.Substring(0, 7))"
if ($names.Count -eq 0) { Write-Host '  Nothing changed since the last deploy.'; exit 0 }
foreach ($n in $names) {
    $t = $Registry.targets.$n
    $sql = Get-PendingSql $t
    Write-Host "  - $n : $($t.project) -> server '$($t.server)' | db '$($t.database)' | last deployed: $(if (Get-LastDeployed $n) { (Get-LastDeployed $n).Substring(0, 7) } else { 'never' })"
    foreach ($s in $sql) { Write-Host "      SQL: $s" }
}
if ($Plan) { exit 0 }

# ---------------------------------------------------------------- connectivity
foreach ($serverName in ($names | ForEach-Object { $Registry.targets.$_.server } | Select-Object -Unique)) {
    $server = Get-Server $serverName
    Step "Connect $serverName ($($server.Login):$($server.Port))"
    if ((Remote $server "if (Test-Path $($server.RemoteRoot)/deploy-config.json) { 'connected to ' + `$env:COMPUTERNAME } else { exit 3 }") -ne 0) {
        throw "SSH to '$serverName' failed or the server was not prepared with Setup-TodoXDeployServer.bat."
    }
}
if ($CheckOnly) { Write-Host 'RESULT: CONNECTION OK'; exit 0 }

$dirty = git -C $Repo status --porcelain --untracked-files=no
if ($dirty) { throw "Working tree has uncommitted changes; commit before deploying.`n$dirty" }

# ---------------------------------------------------------------- deploy each target
foreach ($n in $names) {
    $t = $Registry.targets.$n
    $server = Get-Server $t.server
    Step "[$n] start"

    if (-not $SkipTests) {
        foreach ($p in @($t.tests)) { if ($p) { Step "[$n] test $p"; Run 'dotnet' @('test', (Join-Path $Repo $p), '-c', 'Release', '--nologo') } }
    }

    foreach ($s in (Get-PendingSql $t)) {
        Step "[$n] SQL $s -> $($t.database)"
        Run 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Repo 'tools\prod-sql\Invoke-TodoXSql.ps1'), '-SqlFile', $s, '-Database', $t.database, '-Apply')
    }

    Step "[$n] publish $($t.project)"
    $out = Join-Path $env:TEMP "todox-publish-$n"
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
    Run 'dotnet' @('publish', (Join-Path $Repo $t.project), '-c', 'Release', '-o', $out, '--nologo')
    if (-not (Test-Path (Join-Path $out $t.mainDll))) { throw "Publish output missing $($t.mainDll)" }

    $zip = Join-Path $env:TEMP "todox-$n-$($commit.Substring(0, 7)).zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    # Production config and secrets stay on the server; never ship local ones.
    $items = Get-ChildItem $out | Where-Object { $_.Name -notmatch '^appsettings\..*\.json$' -and $_.Name -ne 'keys' }
    Compress-Archive -Path $items.FullName -DestinationPath $zip -CompressionLevel Optimal
    $sha = (Get-FileHash $zip -Algorithm SHA256).Hash
    Write-Host "  $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB) sha256=$sha"

    Step "[$n] upload to $($server.Name)"
    $remoteZip = "$($server.RemoteRoot)/incoming/$(Split-Path $zip -Leaf)"
    Run 'scp' ($server.Scp + @($zip, "$($server.Login):$remoteZip"))
    $keyArg = ''
    if ($InstallSigningKey -and $n -eq 'dashboard') {
        $pem = Join-Path $Repo 'TodoX.Web\keys\editvideo-license-signing.pem'
        Run 'scp' ($server.Scp + @($pem, "$($server.Login):$($server.RemoteRoot)/incoming/editvideo-license-signing.pem"))
        $keyArg = " -InstallKey $($server.RemoteRoot)/incoming/editvideo-license-signing.pem"
    }

    Step "[$n] apply on server"
    $code = Remote $server "powershell -NoProfile -ExecutionPolicy Bypass -File $($server.RemoteRoot)/Apply-TodoXRelease.ps1 -Target $n -Package $remoteZip -Sha256 $sha -ExpectedCommit $commit$keyArg"
    if ($code -ne 0) { throw "[$n] remote apply failed (exit $code); the server rolled back to the previous build." }

    if (-not (Test-Path $DeployLedger)) {
        [IO.File]::WriteAllText($DeployLedger, "deployed_at`ttarget`tcommit`tserver`r`n", (New-Object Text.UTF8Encoding $false))
    }
    [IO.File]::AppendAllText($DeployLedger, ("{0}`t{1}`t{2}`t{3}`r`n" -f (Get-Date -Format o), $n, $commit, $server.Name), (New-Object Text.UTF8Encoding $false))
    Write-Host "RESULT: [$n] DEPLOYED $commit" -ForegroundColor Green
}
