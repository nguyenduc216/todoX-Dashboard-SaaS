<#
.SYNOPSIS
  Runs a manual SQL script against the TodoX todo_saas database: test first, apply only if the test passes.

.DESCRIPTION
  Every script database/manual/**/NAME.sql must have a companion NAME.test.sql that raises an exception
  when the expected result is not in place.

    1. DRY RUN : BEGIN; NAME.sql; NAME.test.sql; ROLLBACK;   -> proves it applies cleanly, nothing is kept.
    2. APPLY   : BEGIN; NAME.sql; NAME.test.sql; COMMIT;     -> only when step 1 passed and -Apply is given.
                 Any error inside the transaction rolls everything back (ON_ERROR_STOP + single transaction).

  The connection string is read from the deployed dashboard's appsettings.json (or -ConnectionSettings);
  the password is passed to psql through the environment only and never printed or logged.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/prod-sql/Invoke-TodoXSql.ps1 -SqlFile database/manual/editvideo-tool/20261103_editvideo_licensing.sql
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/prod-sql/Invoke-TodoXSql.ps1 -SqlFile database/manual/editvideo-tool/20261103_editvideo_licensing.sql -Apply
#>
param(
    [Parameter(Mandatory = $true)][string] $SqlFile,
    [switch] $Apply,
    # Logical database name from deploy/targets.json ("databases" section).
    [string] $Database = 'todo_saas',
    [string] $Psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Registry = Get-Content (Join-Path $RepoRoot 'deploy\targets.json') -Raw | ConvertFrom-Json
$DbEntry = $Registry.databases.$Database
if (-not $DbEntry) { throw "Unknown database '$Database' (see deploy/targets.json)." }
$ConnectionSettings = $DbEntry.settingsFile
$ConnectionName = $DbEntry.connectionName
# Committed ledger: which script (and which exact content) was applied to which logical database.
$Ledger = Join-Path $PSScriptRoot 'applied-ledger.tsv'
$LogDir = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$LogFile = Join-Path $LogDir "sql-$Stamp.log"

function Log([string] $Message) {
    $line = '[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $line
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
}

# --- 1. Validate inputs: only scripts under database/manual, and a test file is mandatory.
$sqlPath = (Resolve-Path (Join-Path $RepoRoot $SqlFile) -ErrorAction Stop).Path
$manualRoot = (Resolve-Path (Join-Path $RepoRoot 'database\manual')).Path
if (-not $sqlPath.StartsWith($manualRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Only scripts under database\manual are allowed: $sqlPath"
}
if ($sqlPath -like '*.test.sql') {
    throw 'Pass the migration script, not its .test.sql file.'
}
$testPath = [IO.Path]::ChangeExtension($sqlPath, '.test.sql')
if (-not (Test-Path $testPath)) {
    throw "Missing test file: $testPath (every script needs a .test.sql that raises on failure)."
}
if (-not (Test-Path $Psql)) {
    throw "psql not found: $Psql"
}

# --- 2. Connection (password only via environment).
$settings = Get-Content $ConnectionSettings -Raw | ConvertFrom-Json
$cs = $settings.ConnectionStrings.$ConnectionName
if (-not $cs) { throw "Connection string '$ConnectionName' not found in $ConnectionSettings" }
$parts = @{}
foreach ($pair in $cs.Split(';')) {
    if ($pair -match '=') {
        $k, $v = $pair.Split('=', 2)
        $parts[$k.Trim().ToLowerInvariant()] = $v.Trim()
    }
}
$dbHost = $parts['host']; $dbPort = if ($parts['port']) { $parts['port'] } else { '5432' }
$dbName = $parts['database']; $dbUser = $parts['username']
if (-not $dbUser) { $dbUser = $parts['user id'] }

$relPath = $sqlPath.Substring($RepoRoot.Length + 1).Replace('\', '/')
$sqlSha = (Get-FileHash $sqlPath -Algorithm SHA256).Hash
Log "Script : $relPath"
Log "Test   : $($testPath.Substring($RepoRoot.Length + 1))"
Log "Target : $Database -> $dbUser@$dbHost`:$dbPort/$dbName"
Log "SHA256 : $sqlSha"

if ($Apply -and (Test-Path $Ledger) -and (Select-String -Path $Ledger -SimpleMatch "`t$Database`t$relPath`t$sqlSha" -Quiet)) {
    Log 'RESULT: ALREADY APPLIED (same content) - nothing to do.'
    exit 0
}

function Invoke-Batch([string] $Mode) {
    $end = if ($Mode -eq 'apply') { 'COMMIT;' } else { 'ROLLBACK;' }
    $wrapper = Join-Path $env:TEMP "todox-sql-$Stamp-$Mode.sql"
    @(
        '\set ON_ERROR_STOP on',
        "\echo '== ${Mode}: begin'",
        'BEGIN;',
        "SET LOCAL lock_timeout = '10s';",
        "SET LOCAL statement_timeout = '5min';",
        "\i '$($sqlPath.Replace('\', '/'))'",
        "\echo '== ${Mode}: running test'",
        "\i '$($testPath.Replace('\', '/'))'",
        "\echo '== ${Mode}: test passed'",
        $end,
        "\echo '== ${Mode}: $end'"
    ) | ForEach-Object -Begin { $lines = @() } -Process { $lines += $_ } -End {
        # psql chokes on a UTF-8 BOM, which Windows PowerShell's Set-Content would add.
        [IO.File]::WriteAllLines($wrapper, $lines, (New-Object Text.UTF8Encoding $false))
    }

    $env:PGPASSWORD = $parts['password']
    $env:PGCONNECT_TIMEOUT = '15'
    $env:PGCLIENTENCODING = 'UTF8'
    # psql writes NOTICEs to stderr; Windows PowerShell would turn those into terminating errors under 'Stop'.
    # Success is decided by psql's exit code (ON_ERROR_STOP makes any SQL error non-zero).
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Psql -h $dbHost -p $dbPort -U $dbUser -d $dbName -X -q -v ON_ERROR_STOP=1 -f $wrapper 2>&1 |
            ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
        Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
        Remove-Item $wrapper -ErrorAction SilentlyContinue
    }
    foreach ($line in $output) { Log "  psql> $line" }
    return $code
}

# --- 3. Dry run (always rolled back).
Log '== STEP 1: dry run (BEGIN ... test ... ROLLBACK)'
$dry = Invoke-Batch 'dryrun'
if ($dry -ne 0) {
    Log "RESULT: DRY RUN FAILED (exit $dry). Nothing was changed. Log: $LogFile"
    exit 1
}
Log 'Dry run passed; all changes were rolled back.'

if (-not $Apply) {
    Log "RESULT: DRY RUN OK. Re-run with -Apply to commit. Log: $LogFile"
    exit 0
}

# --- 4. Apply (single transaction; any failure rolls back automatically).
Log '== STEP 2: apply (BEGIN ... test ... COMMIT)'
$applied = Invoke-Batch 'apply'
if ($applied -ne 0) {
    Log "RESULT: APPLY FAILED (exit $applied). Transaction rolled back, database unchanged. Log: $LogFile"
    exit 1
}

if (-not (Test-Path $Ledger)) {
    [IO.File]::WriteAllText($Ledger, "applied_at`tdatabase`tscript`tsha256`r`n", (New-Object Text.UTF8Encoding $false))
}
[IO.File]::AppendAllText($Ledger, ("{0}`t{1}`t{2}`t{3}`r`n" -f (Get-Date -Format o), $Database, $relPath, $sqlSha), (New-Object Text.UTF8Encoding $false))
Log "RESULT: APPLIED and verified. Log: $LogFile"
exit 0
