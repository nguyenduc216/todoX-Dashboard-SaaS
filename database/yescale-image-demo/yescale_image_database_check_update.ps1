[CmdletBinding()]
param(
    [string]$ConnectionString = $env:TODOX_DB_CONNECTION,
    [switch]$Apply,
    [switch]$ConfirmProduction,
    [string]$BackupFile,
    [switch]$ReportOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Convert-ToPsqlConnection {
    param([Parameter(Mandatory = $true)][string]$Value)

    if (-not $Value.Contains(';')) {
        return $Value
    }

    $mapped = @{}
    foreach ($part in $Value.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $pair = $part.Split('=', 2)
        if ($pair.Count -ne 2) { continue }
        $key = $pair[0].Trim().ToLowerInvariant()
        $val = $pair[1].Trim()
        switch ($key) {
            'host' { $mapped['host'] = $val }
            'port' { $mapped['port'] = $val }
            'database' { $mapped['dbname'] = $val }
            'username' { $mapped['user'] = $val }
            'user id' { $mapped['user'] = $val }
            'password' { $env:PGPASSWORD = $val }
            'ssl mode' { $mapped['sslmode'] = $val.ToLowerInvariant().Replace('require', 'require').Replace('prefer', 'prefer').Replace('disable', 'disable') }
            'timeout' { $mapped['connect_timeout'] = $val }
        }
    }

    return (($mapped.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' ')
}

function Invoke-PsqlScalar {
    param([Parameter(Mandatory = $true)][string]$Sql)
    $result = & psql --no-password --dbname="$script:PsqlConnection" --tuples-only --no-align --quiet --command="$Sql"
    if ($LASTEXITCODE -ne 0) { throw "Database query failed with exit code $LASTEXITCODE." }
    return (($result | Out-String).Trim())
}

function Invoke-PsqlCommand {
    param([Parameter(Mandatory = $true)][string]$Sql)
    & psql --no-password --dbname="$script:PsqlConnection" --set ON_ERROR_STOP=1 --command="$Sql"
    if ($LASTEXITCODE -ne 0) { throw "Database command failed with exit code $LASTEXITCODE." }
}

function Invoke-PsqlFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing SQL file: $Path" }
    Write-Host "`n[RUN] $(Split-Path -Leaf $Path)" -ForegroundColor Cyan
    & psql --no-password --dbname="$script:PsqlConnection" --set ON_ERROR_STOP=1 --file="$Path"
    if ($LASTEXITCODE -ne 0) { throw "SQL failed: $(Split-Path -Leaf $Path) (exit $LASTEXITCODE)." }
    Write-Host "[PASS] $(Split-Path -Leaf $Path)" -ForegroundColor Green
}

function Show-FinalReport {
    Write-Host "`n================ YESCALE IMAGE DEMO REPORT ================" -ForegroundColor Yellow
    Invoke-PsqlCommand -Sql @"
SELECT current_database() AS database_name, current_user AS database_user, now() AS checked_at;

SELECT 'BASE_OBJECTS' AS section,
       required.object_name,
       CASE WHEN to_regclass(required.object_name) IS NULL THEN 'MISSING' ELSE 'OK' END AS status
FROM (VALUES
 ('billing.token_wallets'),
 ('billing.token_transactions'),
 ('system.app_settings'),
 ('system.tenants'),
 ('auth.permissions'),
 ('auth.roles'),
 ('auth.role_permissions'),
 ('public.todox_ai_provider'),
 ('public.todox_ai_provider_capability'),
 ('billing.ai_image_billing_records'),
 ('billing.ai_image_provider_attempts'),
 ('billing.yescale_image_default_snapshot')
) required(object_name)
ORDER BY required.object_name;

SELECT 'PROVIDER' AS section, provider_code, provider_name, enabled, api_key_config_name
FROM public.todox_ai_provider
WHERE provider_code='yescale_task_image';

SELECT 'CAPABILITIES' AS section, capability_code, model_name, enabled, is_default,
       allow_user_select, unit_cost_points,
       config_json->>'provider_estimated_cost_usd' AS estimated_usd
FROM public.todox_ai_provider_capability
WHERE provider_code='yescale_task_image'
ORDER BY capability_code, is_default DESC, model_name;

SELECT 'SYSTEM_WALLET' AS section, tenant_id, wallet_code, balance, locked_balance,
       overdraft_limit, low_balance_threshold, status
FROM billing.token_wallets
WHERE wallet_scope='system' AND wallet_code='TODOX_AI_IMAGE_SYSTEM'
ORDER BY tenant_id;

SELECT 'PERMISSIONS' AS section, module, action, is_active
FROM auth.permissions
WHERE (module='ai.image' AND action='system_wallet.use')
   OR (module='ai.billing' AND action IN ('dashboard.view','reconciliation.manage'))
ORDER BY module, action;

SELECT 'BILLING_STATUS' AS section, status, count(*) AS records,
       COALESCE(sum(total_provider_estimated_cost_usd),0) AS estimated_usd,
       COALESCE(sum(total_provider_actual_cost_usd),0) AS actual_usd,
       COALESCE(sum(system_charged_points),0) AS system_points
FROM billing.ai_image_billing_records
GROUP BY status
ORDER BY status;

SELECT 'FINAL_CHECK' AS section,
       CASE WHEN EXISTS (
           SELECT 1 FROM public.todox_ai_provider
           WHERE provider_code='yescale_task_image' AND enabled=true
       ) THEN 'PASS' ELSE 'FAIL' END AS provider_enabled,
       CASE WHEN (
           SELECT count(*) FROM public.todox_ai_provider_capability
           WHERE provider_code='yescale_task_image'
             AND capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','thumbnail_generation')
             AND model_name='nano-banana-2' AND enabled=true AND is_default=true
       ) = 6 THEN 'PASS' ELSE 'FAIL' END AS six_defaults,
       CASE WHEN EXISTS (
           SELECT 1 FROM billing.token_wallets
           WHERE wallet_scope='system' AND wallet_code='TODOX_AI_IMAGE_SYSTEM' AND status='active'
       ) THEN 'PASS' ELSE 'FAIL' END AS system_wallet,
       CASE WHEN NOT EXISTS (
           SELECT 1 FROM billing.ai_image_billing_records
           WHERE status IN ('reserved','pending_reconciliation','manual_review')
       ) THEN 'PASS' ELSE 'REVIEW' END AS billing_reconciliation;
"@
    Write-Host "================ END REPORT ===============================" -ForegroundColor Yellow
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Connection string is required. Set TODOX_DB_CONNECTION or pass -ConnectionString."
}
if ($Apply -and $ReportOnly) { throw "Use either -Apply or -ReportOnly, not both." }
if ($Apply -and -not $ConfirmProduction) { throw "For database updates, pass both -Apply and -ConfirmProduction." }

if ($null -eq (Get-Command psql -ErrorAction SilentlyContinue)) {
    throw "psql was not found in PATH. Install PostgreSQL command line tools first."
}

$script:PsqlConnection = Convert-ToPsqlConnection -Value $ConnectionString
$actualDatabase = Invoke-PsqlScalar -Sql "SELECT current_database();"
if (-not [string]::Equals($actualDatabase, 'todo_saas', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Safety stop: connected database is '$actualDatabase', expected 'todo_saas'."
}
Write-Host "[PASS] Connected to database todo_saas. Connection details are hidden." -ForegroundColor Green

$baseObjects = @(
    'billing.token_wallets', 'billing.token_transactions', 'system.app_settings', 'system.tenants',
    'auth.permissions', 'auth.roles', 'auth.role_permissions',
    'public.todox_ai_provider', 'public.todox_ai_provider_capability'
)
$values = ($baseObjects | ForEach-Object { "('$_')" }) -join ','
$missing = Invoke-PsqlScalar -Sql "SELECT string_agg(v.name, ', ' ORDER BY v.name) FROM (VALUES $values) v(name) WHERE to_regclass(v.name) IS NULL;"
if (-not [string]::IsNullOrWhiteSpace($missing)) {
    throw "Foundation tables are missing: $missing. Apply the TodoX foundation database scripts first; this YEScale patch will not invent wallet/auth/provider base schemas."
}
Write-Host "[PASS] All TodoX foundation tables exist." -ForegroundColor Green

$tenantCount = [int](Invoke-PsqlScalar -Sql "SELECT count(*) FROM system.tenants;")
if ($tenantCount -ne 1) {
    throw "Safety stop: expected exactly 1 tenant for the current demo SQL, found $tenantCount. Update wallet validation to be tenant-scoped before applying."
}
Write-Host "[PASS] Tenant count is 1." -ForegroundColor Green

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sqlFiles = @(
    '01_add_or_update_billing_support.sql',
    '02_seed_yescale_image_tariffs.sql',
    '03_seed_system_wallet_and_permissions.sql',
    '04_enable_yescale_image_demo.sql',
    'verify_yescale_image_demo.sql'
)
foreach ($file in $sqlFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $scriptRoot $file) -PathType Leaf)) {
        throw "Required companion file is missing: $file. Keep this script beside the six YEScale SQL files."
    }
}
Write-Host "[PASS] All YEScale SQL files are present." -ForegroundColor Green

if ($ReportOnly) {
    Show-FinalReport
    exit 0
}

if (-not $Apply) {
    Write-Host "`nPreflight passed. No database changes were made." -ForegroundColor Yellow
    Write-Host "To backup, update and verify production, rerun with: -Apply -ConfirmProduction -BackupFile <path>"
    exit 0
}

if ([string]::IsNullOrWhiteSpace($BackupFile)) {
    throw "-BackupFile is required with -Apply. Example: D:\backup\todo_saas_before_yescale.backup"
}
if ($null -eq (Get-Command pg_dump -ErrorAction SilentlyContinue)) {
    throw "pg_dump was not found in PATH. It is required for the automatic pre-update backup."
}

$backupDirectory = Split-Path -Parent $BackupFile
if (-not [string]::IsNullOrWhiteSpace($backupDirectory) -and -not (Test-Path -LiteralPath $backupDirectory)) {
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
}
if (Test-Path -LiteralPath $BackupFile) {
    throw "Backup file already exists; choose a new path so an earlier backup is never overwritten: $BackupFile"
}

Write-Host "`n[RUN] Creating pre-update database backup..." -ForegroundColor Cyan
& pg_dump --format=custom --file="$BackupFile" --dbname="$script:PsqlConnection"
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $BackupFile) -or (Get-Item $BackupFile).Length -le 0) {
    throw "Database backup failed or produced an empty file. No SQL updates were started."
}
Write-Host "[PASS] Backup created: $BackupFile" -ForegroundColor Green

foreach ($file in $sqlFiles) {
    Invoke-PsqlFile -Path (Join-Path $scriptRoot $file)
}

Show-FinalReport
Write-Host "`n[PASS] YEScale image database setup completed. Save screenshots of the full report before deploying the application." -ForegroundColor Green
