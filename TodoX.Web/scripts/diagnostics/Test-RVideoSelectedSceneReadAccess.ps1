[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Manifest,
    [Parameter(Mandatory=$true)][long]$ExpectedProjectId,
    [Parameter(Mandatory=$true)][guid]$ExpectedTenantId,
    [Parameter(Mandatory=$true)][guid]$ExpectedJobId,
    [Parameter(Mandatory=$true)][string]$ExpectedWorkerIdentity,
    [Parameter(Mandatory=$true)][int]$ExpectedSceneCount
)
$ErrorActionPreference = 'Stop'
# Does not impersonate, contact DB/network APIs, localize/download media, change ACLs or write files.
# Launch through the operator-approved worker execution context. Matching account name alone
# cannot prove the same logon token, groups, network credentials or sandbox as the live worker.
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (![string]::Equals($identity.Name, $ExpectedWorkerIdentity, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Current account differs from ExpectedWorkerIdentity. No source files were opened.'
}
$data = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
if ($data.projectId -ne $ExpectedProjectId -or [guid]$data.tenantId -ne $ExpectedTenantId -or [guid]$data.coreJobId -ne $ExpectedJobId) {
    throw 'Manifest project/tenant/core job mismatch.'
}
$items = @($data.items | Sort-Object itemOrder)
if ($ExpectedSceneCount -le 0 -or $items.Count -ne $ExpectedSceneCount) { throw 'Unexpected scene count.' }
foreach ($key in @('sceneId','sceneVideoVersionId','itemOrder')) {
    if (@($items | Select-Object -ExpandProperty $key -Unique).Count -ne $items.Count) { throw "Duplicate $key in manifest." }
}
$totalSeconds = 0
foreach ($item in $items) {
    if ($null -eq $item.sceneVideoVersionId -or [guid]$item.sceneVideoVersionId -eq [guid]::Empty -or
        $item.status -ne 'completed' -or $item.isSelected -ne $true -or $item.lineageValid -ne $true -or
        $item.sceneProjectId -ne $ExpectedProjectId -or $item.versionProjectId -ne $ExpectedProjectId -or
        [guid]$item.sceneTenantId -ne $ExpectedTenantId -or [guid]$item.versionTenantId -ne $ExpectedTenantId -or
        $item.versionSceneId -ne $item.sceneId -or $item.durationSeconds -le 0) { throw 'Selected version identity/lineage/readiness mismatch.' }
    if ([string]::IsNullOrWhiteSpace($item.videoPath) -or ![IO.Path]::IsPathRooted($item.videoPath)) {
        throw 'Selected source path is absent/relative. Operator must establish canonical resolver mapping; no file substitution is performed.'
    }
    $totalSeconds += $item.durationSeconds
}
if ($totalSeconds -ne $data.configuredTotalSeconds) { throw 'Scene durations differ from project configured total.' }
$results = @()
foreach ($item in $items) {
    $file = Get-Item -LiteralPath $item.videoPath
    if ($file.PSIsContainer -or $file.Length -le 0) { throw 'Selected source is not a nonempty file.' }
    $length = $file.Length
    $modified = $file.LastWriteTimeUtc
    # READ access only; full hash demonstrates ability to read every byte under the current token.
    $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
    $file.Refresh()
    if ($length -ne $file.Length -or $modified -ne $file.LastWriteTimeUtc) { throw 'Source changed during read; repeat preflight after files settle.' }
    $results += [pscustomobject]@{ sceneId=$item.sceneId; versionId=$item.sceneVideoVersionId; order=$item.itemOrder; path=$file.FullName; bytes=$length; sha256=$hash; readPassed=$true }
}
[pscustomobject]@{ checkedAtUtc=[DateTime]::UtcNow.ToString('o'); identity=$identity.Name; sid=$identity.User.Value; projectId=$ExpectedProjectId; tenantId=$ExpectedTenantId; jobId=$ExpectedJobId; sceneCount=$items.Count; configuredSeconds=$totalSeconds; allReadsPassed=$true; files=$results } | ConvertTo-Json -Depth 5
