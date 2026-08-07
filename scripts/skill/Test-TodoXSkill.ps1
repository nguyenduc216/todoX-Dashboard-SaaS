param(
    [string]$BaseUrl = 'https://skill.todox.vn',
    [string]$JobId = '397',
    [string]$SkillKey = $env:TODOX_SKILL_API_KEY
)

$ErrorActionPreference = 'Stop'

Write-Host '1/3 Health' -ForegroundColor Cyan
$health = Invoke-RestMethod -Method Get -Uri ($BaseUrl.TrimEnd('/') + '/health')
$health | ConvertTo-Json -Depth 10
if ($health.status -ne 'ok') { throw 'Skill health is not OK.' }

if ([string]::IsNullOrWhiteSpace($SkillKey)) {
    Write-Warning 'TODOX_SKILL_API_KEY is not set. Authenticated tests skipped.'
    exit 0
}

$headers = @{ 'X-TodoX-Skill-Key' = $SkillKey }

Write-Host '2/3 Job snapshot' -ForegroundColor Cyan
$job = Invoke-RestMethod -Method Get -Headers $headers -Uri ($BaseUrl.TrimEnd('/') + "/api/skill/v1/jobs/$JobId")
$job | ConvertTo-Json -Depth 20

Write-Host '3/3 Job diagnostic' -ForegroundColor Cyan
$diag = Invoke-RestMethod -Method Get -Headers $headers -Uri ($BaseUrl.TrimEnd('/') + "/api/skill/v1/jobs/$JobId/diagnostic")
$diag | ConvertTo-Json -Depth 30

Write-Host "PASS - jobFamily=$($diag.jobFamily), jobStatus=$($diag.jobStatus), retryableScenes=$($diag.retryableSceneIndexes -join ',')" -ForegroundColor Green
