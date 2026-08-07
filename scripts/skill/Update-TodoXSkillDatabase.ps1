param(
    [string]$PgHost = '127.0.0.1',
    [int]$PgPort = 5432,
    [string]$Database = 'todox',
    [string]$User = 'postgres',
    [string]$PsqlPath = 'C:\Program Files\PostgreSQL\17\bin\psql.exe',
    [string]$PgDumpPath = 'C:\Program Files\PostgreSQL\17\bin\pg_dump.exe',
    [string]$TodoXRoot = 'E:\N8N.ANHDUC\todoX',
    [switch]$SkipBackup,
    [switch]$SkipContract,
    [switch]$SkipDoctor
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $env:PGPASSWORD) {
    throw 'Set PGPASSWORD in the current PowerShell session before running this script.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$migration = Join-Path $repoRoot 'database\migrations\20260807_001_todox_skill_ops.sql'
if (-not (Test-Path $PsqlPath)) { throw "psql.exe not found: $PsqlPath" }
if (-not (Test-Path $migration)) { throw "Migration not found: $migration" }

$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$logDir = Join-Path $repoRoot 'artifacts\skill-migration'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logFile = Join-Path $logDir "skill-migration_$timestamp.log"
Start-Transcript -Path $logFile -Force | Out-Null

try {
    Write-Host '1/6 Check PostgreSQL connection' -ForegroundColor Cyan
    & $PsqlPath -h $PgHost -p $PgPort -U $User -d $Database -v ON_ERROR_STOP=1 -c 'select current_user,current_database();'
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL connection check failed.' }

    if (-not $SkipBackup) {
        if (-not (Test-Path $PgDumpPath)) { throw "pg_dump.exe not found: $PgDumpPath" }
        $backupDir = Join-Path $TodoXRoot 'database\backups'
        New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
        $backupFile = Join-Path $backupDir "todox_before_skill_$timestamp.dump"
        Write-Host '2/6 Create database backup' -ForegroundColor Cyan
        & $PgDumpPath -h $PgHost -p $PgPort -U $User -d $Database -Fc -f $backupFile
        if ($LASTEXITCODE -ne 0) { throw 'Database backup failed; migration was not applied.' }
    }

    Write-Host '3/6 Apply skill migration' -ForegroundColor Cyan
    & $PsqlPath -h $PgHost -p $PgPort -U $User -d $Database -v ON_ERROR_STOP=1 -f $migration
    if ($LASTEXITCODE -ne 0) { throw 'Skill migration failed.' }

    Write-Host '4/6 Validate migration' -ForegroundColor Cyan
    & $PsqlPath -h $PgHost -p $PgPort -U $User -d $Database -v ON_ERROR_STOP=1 -c "select to_regclass('public.todox_skill_actions'), to_regclass('public.todox_skill_audit_log');"
    if ($LASTEXITCODE -ne 0) { throw 'Migration validation failed.' }

    if (-not $SkipContract) {
        Write-Host '5/6 Regenerate Database Contract when generator exists' -ForegroundColor Cyan
        $contract = Join-Path $TodoXRoot 'automation-api\todox_foundation_v36_api_v11\package1-database-contract\01-Generate-Database-Contract.ps1'
        if (Test-Path $contract) {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $contract
            if ($LASTEXITCODE -ne 0) { throw 'Database Contract generation failed.' }
        } else {
            Write-Warning "Contract generator not found: $contract"
        }
    }

    if (-not $SkipDoctor) {
        Write-Host '6/6 Run Doctor V3.6 when available' -ForegroundColor Cyan
        $doctor = Join-Path $TodoXRoot 'automation-api\todox_foundation_v36_api_v11\package2-doctor-v36\TodoX-Doctor-V3.6-RELEASE.ps1'
        if (Test-Path $doctor) {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $doctor
            if ($LASTEXITCODE -ne 0) { throw 'TodoX Doctor V3.6 reported failure.' }
        } else {
            Write-Warning "Doctor not found: $doctor"
        }
    }

    Write-Host "Completed. Log: $logFile" -ForegroundColor Green
}
finally {
    Stop-Transcript | Out-Null
}
