<#
.SYNOPSIS
  Builds a todoX ReupVideo release (one self-contained exe + manifest.json) and, with -Upload, copies it to the
  download folder on the production server: C:/TodoXDeploy/downloads/reupvideo. The landing site serves it at
  https://todox.vn/downloads/reupvideo/latest (TodoX.Landing/Services/AppDownloads.cs) and shows the version on
  /reupvideo, the home page "Tải app" section and /editvideo.

  The manifest is uploaded last, so visitors never get a half-copied file. Older exe files on the server are
  removed after the new manifest is in place (only the latest build is kept).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/reupvideo/Build-ReupVideoRelease.ps1 -Upload
#>
param(
    [switch] $Upload,
    [string] $Notes = ''
)

$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Out = Join-Path $Repo 'publish\reupvideo-release'
$Work = Join-Path $env:TEMP 'todox-reupvideo-release'
Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Out, $Work | Out-Null

function Step([string] $t) { Write-Host "== $t" -ForegroundColor Cyan }

Step 'Publish todox-reupvideo.exe (Release, single self-contained file)'
Push-Location $Repo
try {
    & dotnet publish 'TodoX.ReupVideo/TodoX.ReupVideo.csproj' -c Release -o $Work --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
} finally { Pop-Location }

$version = ([xml](Get-Content (Join-Path $Repo 'TodoX.ReupVideo/TodoX.ReupVideo.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$appName = "todoX-ReupVideo-$version.exe"
Get-ChildItem $Out -Filter 'todoX-ReupVideo-*.exe' | Remove-Item -Force
$appPath = Join-Path $Out $appName
Copy-Item (Join-Path $Work 'todox-reupvideo.exe') $appPath -Force

Step 'Write manifest.json'
$file = [ordered]@{ name = $appName; kind = 'app'; version = $version; sha256 = (Get-FileHash $appPath -Algorithm SHA256).Hash; size = (Get-Item $appPath).Length }
$manifest = [ordered]@{ app = 'reupvideo'; version = $version; publishedAt = (Get-Date).ToUniversalTime().ToString('o'); notes = $Notes; files = @($file) }
$manifestPath = Join-Path $Out 'manifest.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
Write-Host ("  {0}  {1:N1} MB  {2}" -f $appName, ($file.size / 1MB), $file.sha256.Substring(0, 12))

if (-not $Upload) { Write-Host "RESULT: built in $Out (not uploaded)"; exit 0 }

Step 'Upload to production download folder'
$cfg = (Get-Content (Join-Path $Repo 'deploy\remote.local.json') -Raw | ConvertFrom-Json).servers.prod
$login = "$($cfg.User)@$($cfg.Host)"
$key = "$env:USERPROFILE\.ssh\todox_deploy"
$remote = 'C:/TodoXDeploy/downloads/reupvideo'
& ssh -i $key -p $cfg.Port -o BatchMode=yes $login "New-Item -ItemType Directory -Force -Path '$remote' | Out-Null; 'ok'"
if ($LASTEXITCODE -ne 0) { throw 'cannot reach the server' }
& scp -i $key -P $cfg.Port -o BatchMode=yes $appPath "${login}:$remote/$appName"
if ($LASTEXITCODE -ne 0) { throw "upload failed: $appName" }

# Check the copy before publishing it.
$remoteHash = (& ssh -i $key -p $cfg.Port -o BatchMode=yes $login "(Get-FileHash '$remote/$appName' -Algorithm SHA256).Hash" | Select-Object -Last 1).Trim()
if ($remoteHash -ne $file.sha256) { throw "uploaded file hash mismatch ($remoteHash)" }

& scp -i $key -P $cfg.Port -o BatchMode=yes $manifestPath "${login}:$remote/manifest.json"
if ($LASTEXITCODE -ne 0) { throw 'upload failed: manifest.json' }
& ssh -i $key -p $cfg.Port -o BatchMode=yes $login "Get-ChildItem '$remote' -Filter 'todoX-ReupVideo-*.exe' | Where-Object Name -ne '$appName' | Remove-Item -Force; 'cleaned'"
Write-Host "RESULT: release $version uploaded ($appName)"
