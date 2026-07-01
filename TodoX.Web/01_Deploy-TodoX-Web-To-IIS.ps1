param(
    [string]$RepoRoot = "D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS",
    [string]$ProjectName = "TodoX.Web",
    [string]$SiteName = "TodoX Dashboard SaaS",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Write-Step($message) {
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "[$ts] $message" -ForegroundColor Cyan
}

function Write-Ok($message) {
    Write-Host "OK: $message" -ForegroundColor Green
}

function Write-Warn2($message) {
    Write-Host "WARN: $message" -ForegroundColor Yellow
}

$projectDir = Join-Path $RepoRoot $ProjectName
$projectFile = Join-Path $projectDir "$ProjectName.csproj"
$publishDir = Join-Path $projectDir "publish"
$backupRoot = Join-Path $projectDir "publish_backup"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupDir = Join-Path $backupRoot $timestamp
$logDir = Join-Path $RepoRoot "logs"
$logFile = Join-Path $logDir "deploy-iis-$timestamp.log"

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Start-Transcript -Path $logFile -Force | Out-Null

try {
    Write-Step "TodoX IIS deploy started"
    Write-Step "RepoRoot: $RepoRoot"
    Write-Step "Project: $projectFile"
    Write-Step "PublishDir: $publishDir"
    Write-Step "IIS Site: $SiteName"

    if (!(Test-Path $projectFile)) {
        throw "Project file not found: $projectFile"
    }

    Import-Module WebAdministration -ErrorAction Stop

    $site = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
    if ($null -eq $site) {
        throw "IIS site not found: $SiteName. Create the site first, then rerun this script."
    }

    Write-Step "Stopping IIS site if running"
    if ($site.State -eq "Started") {
        Stop-Website -Name $SiteName
        Start-Sleep -Seconds 2
        Write-Ok "Stopped site $SiteName"
    } else {
        Write-Warn2 "Site is not started: $($site.State)"
    }

    Write-Step "Stopping app pool if exists"
    $appPoolName = $site.applicationPool
    if (![string]::IsNullOrWhiteSpace($appPoolName)) {
        $pool = Get-Item "IIS:\AppPools\$appPoolName" -ErrorAction SilentlyContinue
        if ($null -ne $pool -and $pool.state -eq "Started") {
            Stop-WebAppPool -Name $appPoolName
            Start-Sleep -Seconds 2
            Write-Ok "Stopped app pool $appPoolName"
        }
    }

    Write-Step "Backing up existing publish folder if present"
    if (Test-Path $publishDir) {
        New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
        Copy-Item -Path $publishDir -Destination $backupDir -Recurse -Force
        Write-Ok "Backup created: $backupDir"
    }

    Write-Step "Cleaning publish folder"
    if (Test-Path $publishDir) {
        Remove-Item -Path $publishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

    Write-Step "Running dotnet restore"
    dotnet restore $projectFile

    Write-Step "Running dotnet publish"
    dotnet publish $projectFile -c $Configuration -o $publishDir --no-restore

    $webConfig = Join-Path $publishDir "web.config"
    if (!(Test-Path $webConfig)) {
        throw "web.config not found in publish folder. ASP.NET Core Hosting Bundle may be missing or publish failed."
    }

    Write-Step "Pointing IIS site physicalPath to publish folder"
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $publishDir
    Write-Ok "IIS physicalPath set to $publishDir"

    Write-Step "Starting app pool"
    if (![string]::IsNullOrWhiteSpace($appPoolName)) {
        Start-WebAppPool -Name $appPoolName
        Write-Ok "Started app pool $appPoolName"
    }

    Write-Step "Starting IIS site"
    Start-Website -Name $SiteName
    Write-Ok "Started site $SiteName"

    Write-Step "Verifying IIS site state"
    $siteAfter = Get-Website -Name $SiteName
    Write-Host "Site state: $($siteAfter.State)"
    Write-Host "Physical path: $($siteAfter.physicalPath)"

    Write-Step "Deploy completed successfully"
    Write-Host "Log file: $logFile" -ForegroundColor Green
}
catch {
    Write-Host "DEPLOY FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Log file: $logFile" -ForegroundColor Yellow
    throw
}
finally {
    Stop-Transcript | Out-Null
}
