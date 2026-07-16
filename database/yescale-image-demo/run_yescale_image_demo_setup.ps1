[CmdletBinding()]
param(
    [string]$ConnectionString = $env:TODOX_DB_CONNECTION,
    [switch]$Rollback,
    [switch]$VerifyOnly,
    [switch]$ConfirmProduction
)

$ErrorActionPreference = "Stop"

function Test-LikelyLocalDatabase {
    param([Parameter(Mandatory = $true)][string]$Value)

    $lower = $Value.ToLowerInvariant()
    return $lower.Contains("localhost") `
        -or $lower.Contains("127.0.0.1") `
        -or $lower.Contains("host=.") `
        -or $lower.Contains("database=todox_dev") `
        -or $lower.Contains("database=todox_test") `
        -or $lower.Contains("database=todox_staging") `
        -or $lower.Contains("/todox_dev") `
        -or $lower.Contains("/todox_test") `
        -or $lower.Contains("/todox_staging")
}

function Invoke-PostgresSqlFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "SQL file not found: $Path"
    }

    $name = Split-Path -Leaf $Path
    Write-Host "Running $name ..."
    & psql --set ON_ERROR_STOP=1 --no-password --dbname="$ConnectionString" --file="$Path"
    if ($LASTEXITCODE -ne 0) {
        throw "psql failed while running $name with exit code $LASTEXITCODE."
    }
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Connection string is required. Pass -ConnectionString or set TODOX_DB_CONNECTION."
}

$psql = Get-Command psql -ErrorAction SilentlyContinue
if ($null -eq $psql) {
    throw "psql was not found in PATH."
}

if ($Rollback -and $VerifyOnly) {
    throw "-Rollback and -VerifyOnly cannot be used together."
}

if (-not $ConfirmProduction -and -not (Test-LikelyLocalDatabase -Value $ConnectionString)) {
    throw "Connection does not look local/dev/staging. Re-run with -ConfirmProduction after backup and manual review."
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$setupFiles = @(
    "01_add_or_update_billing_support.sql",
    "02_seed_yescale_image_tariffs.sql",
    "03_seed_system_wallet_and_permissions.sql",
    "04_enable_yescale_image_demo.sql"
)

Write-Host "YEScale image demo setup runner"
Write-Host "Connection string received. Value is intentionally hidden."

if ($VerifyOnly) {
    Invoke-PostgresSqlFile -Path (Join-Path $scriptRoot "verify_yescale_image_demo.sql")
    Write-Host "Verification completed."
    exit 0
}

if ($Rollback) {
    Invoke-PostgresSqlFile -Path (Join-Path $scriptRoot "rollback_yescale_image_demo.sql")
    Invoke-PostgresSqlFile -Path (Join-Path $scriptRoot "verify_yescale_image_demo.sql")
    Write-Host "Rollback and verification completed."
    exit 0
}

foreach ($file in $setupFiles) {
    Invoke-PostgresSqlFile -Path (Join-Path $scriptRoot $file)
}

Invoke-PostgresSqlFile -Path (Join-Path $scriptRoot "verify_yescale_image_demo.sql")
Write-Host "YEScale image demo setup and verification completed."
