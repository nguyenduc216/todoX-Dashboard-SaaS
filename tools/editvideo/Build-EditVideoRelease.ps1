<#
.SYNOPSIS
  Builds a TodoX EditVideo release: installer exe + component zips + manifest.json, then (with -Upload) copies
  them to the download folder on the production server (C:/TodoXDeploy/downloads/editvideo, served by the
  dashboard at /downloads/editvideo/... and /api/editvideo/v1/releases/latest).

  Components:
    node     - node.exe of this machine's Node.js install (official build)
    ffmpeg   - FFmpeg LGPL build (BtbN) : bin/ffmpeg.exe, bin/ffprobe.exe
    remotion - render.mjs + build/ (bundle) + production node_modules (@remotion/renderer only)
  A component zip is rebuilt only when missing; delete publish/editvideo-release/components to force it.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/editvideo/Build-EditVideoRelease.ps1 -Upload
#>
param(
    [switch] $Upload,
    [string] $Notes = '',
    [string] $FfmpegZipUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-win64-lgpl-8.1.zip',
    [string] $RemotionVersion = '4.0.540',
    [string] $WhisperZipUrl = 'https://github.com/ggml-org/whisper.cpp/releases/download/b5454/whisper-bin-x64.zip',
    [string] $WhisperModelUrl = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small-q5_1.bin'
)

$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Out = Join-Path $Repo 'publish\editvideo-release'
$Comp = Join-Path $Out 'components'
$Work = Join-Path $env:TEMP 'todox-editvideo-release'
New-Item -ItemType Directory -Force -Path $Out, $Comp, $Work | Out-Null

function Step([string] $t) { Write-Host "== $t" -ForegroundColor Cyan }
function Run([string] $exe, [string[]] $argv, [string] $cwd = $Repo) {
    Push-Location $cwd
    try { & $exe @argv; if ($LASTEXITCODE -ne 0) { throw "$exe failed ($LASTEXITCODE)" } } finally { Pop-Location }
}
function Zip-Dir([string] $dir, [string] $zip) {
    if (Test-Path $zip) { Remove-Item $zip }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($dir, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
}

# ---------------------------------------------------------------- app
Step 'Publish todox-editvideo.exe (Release, single file)'
$appOut = Join-Path $Work 'app'
Run 'dotnet' @('publish', 'TodoX.EditVideoTool/TodoX.EditVideoTool.csproj', '-c', 'Release', '-o', $appOut, '--nologo')
$version = ([xml](Get-Content (Join-Path $Repo 'TodoX.EditVideoTool/TodoX.EditVideoTool.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$appName = "TodoX-EditVideo-Setup-$version.exe"
Copy-Item (Join-Path $appOut 'todox-editvideo.exe') (Join-Path $Out $appName) -Force

# ---------------------------------------------------------------- node
$nodeZip = Join-Path $Comp 'todox-node.zip'
$nodeVersion = (& node -v).Trim().TrimStart('v')
if (-not (Test-Path $nodeZip)) {
    Step "Package Node.js $nodeVersion"
    $d = Join-Path $Work 'node'; Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $d | Out-Null
    Copy-Item (Get-Command node).Source (Join-Path $d 'node.exe')
    Zip-Dir $d $nodeZip
}

# ---------------------------------------------------------------- ffmpeg (LGPL)
$ffZip = Join-Path $Comp 'todox-ffmpeg.zip'
if (-not (Test-Path $ffZip)) {
    Step 'Download + package FFmpeg (LGPL)'
    $dl = Join-Path $Work 'ffmpeg-src.zip'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $FfmpegZipUrl -OutFile $dl -UseBasicParsing
    $x = Join-Path $Work 'ffmpeg-src'; Remove-Item $x -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive $dl $x
    $bin = Get-ChildItem $x -Recurse -Filter ffmpeg.exe | Select-Object -First 1
    $d = Join-Path $Work 'ffmpeg'; Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory (Join-Path $d 'bin') | Out-Null
    Copy-Item $bin.FullName, (Join-Path $bin.DirectoryName 'ffprobe.exe') (Join-Path $d 'bin')
    $lic = Get-ChildItem $x -Recurse -Filter 'LICENSE*' | Select-Object -First 1
    if ($lic) { Copy-Item $lic.FullName (Join-Path $d 'LICENSE-ffmpeg.txt') }
    Set-Content (Join-Path $d 'SOURCE.txt') "FFmpeg LGPL build from $FfmpegZipUrl (https://github.com/BtbN/FFmpeg-Builds). Source: https://ffmpeg.org" -Encoding ascii
    Zip-Dir $d $ffZip
}

# ---------------------------------------------------------------- whisper (listen-back check)
$whZip = Join-Path $Comp 'todox-whisper-b5454-small-q5_1.zip'
if (-not (Test-Path $whZip)) {
    Step 'Download + package whisper.cpp + small multilingual model'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $dl = Join-Path $Work 'whisper-src.zip'
    Invoke-WebRequest -Uri $WhisperZipUrl -OutFile $dl -UseBasicParsing
    $x = Join-Path $Work 'whisper-src'; Remove-Item $x -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive $dl $x
    $cli = Get-ChildItem $x -Recurse -Filter whisper-cli.exe | Select-Object -First 1
    $d = Join-Path $Work 'whisper'; Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory (Join-Path $d 'bin'), (Join-Path $d 'models') | Out-Null
    Copy-Item $cli.FullName (Join-Path $d 'bin')
    Get-ChildItem $cli.DirectoryName -Filter *.dll | Where-Object { $_.Name -match '^(whisper|ggml)' } | Copy-Item -Destination (Join-Path $d 'bin')
    Invoke-WebRequest -Uri $WhisperModelUrl -OutFile (Join-Path $d 'models\ggml-small-q5_1.bin') -UseBasicParsing
    Set-Content (Join-Path $d 'SOURCE.txt') "whisper.cpp $WhisperZipUrl (MIT, https://github.com/ggml-org/whisper.cpp); model $WhisperModelUrl (MIT, OpenAI Whisper)" -Encoding ascii
    Zip-Dir $d $whZip
}

# ---------------------------------------------------------------- remotion (render runtime only)
$remZip = Join-Path $Comp "todox-remotion-$RemotionVersion.zip"
$templates = Join-Path $Repo 'TodoX.EditVideoTool.Templates'
Step 'Bundle Remotion templates'
Run 'node' @('scripts/bundle.mjs') $templates
$bundleHash = (Get-ChildItem (Join-Path $templates 'build') -Recurse -File | Sort-Object FullName | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash }) -join '' |
    ForEach-Object { [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::ASCII.GetBytes($_))).Replace('-', '').Substring(0, 12) }
$remZip = Join-Path $Comp "todox-remotion-$RemotionVersion-$bundleHash.zip"
if (-not (Test-Path $remZip)) {
    Step 'Package Remotion runtime'
    $d = Join-Path $Work 'remotion'; Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $d | Out-Null
    Copy-Item (Join-Path $templates 'render.mjs') $d
    Copy-Item (Join-Path $templates 'build') (Join-Path $d 'build') -Recurse
    Set-Content (Join-Path $d 'package.json') "{`"name`":`"todox-remotion-runtime`",`"private`":true,`"type`":`"module`"}" -Encoding ascii
    Run 'npm' @('install', '--omit=dev', '--no-audit', '--no-fund', '--save-exact', "@remotion/renderer@$RemotionVersion") $d
    Zip-Dir $d $remZip
}

# ---------------------------------------------------------------- manifest
Step 'Write manifest.json'
function Entry([string] $path, [string] $kind, [string] $component, [string] $ver) {
    [ordered]@{ name = (Split-Path $path -Leaf); kind = $kind; component = $component; version = $ver; sha256 = (Get-FileHash $path -Algorithm SHA256).Hash; size = (Get-Item $path).Length }
}
$files = @(
    (Entry (Join-Path $Out $appName) 'app' $null $version),
    (Entry $nodeZip 'component' 'node' $nodeVersion),
    (Entry $ffZip 'component' 'ffmpeg' 'n8.1-lgpl'),
    (Entry $remZip 'component' 'remotion' "$RemotionVersion-$bundleHash"),
    (Entry $whZip 'component' 'whisper' 'b5454-small-q5_1')
)
$manifest = [ordered]@{ version = $version; minSupported = '0.4.0'; publishedAt = (Get-Date).ToUniversalTime().ToString('o'); notes = $Notes; files = $files }
$manifestPath = Join-Path $Out 'manifest.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
$files | ForEach-Object { Write-Host ("  {0,-45} {1,8:N1} MB  {2}" -f $_.name, ($_.size / 1MB), $_.sha256.Substring(0, 12)) }

if (-not $Upload) { Write-Host "RESULT: built in $Out (not uploaded)"; exit 0 }

# ---------------------------------------------------------------- upload (manifest last, so clients never see a half release)
Step 'Upload to production download folder'
$cfg = (Get-Content (Join-Path $Repo 'deploy\remote.local.json') -Raw | ConvertFrom-Json).servers.prod
$login = "$($cfg.User)@$($cfg.Host)"
$remote = 'C:/TodoXDeploy/downloads/editvideo'
& ssh -i "$env:USERPROFILE\.ssh\todox_deploy" -p $cfg.Port -o BatchMode=yes $login "New-Item -ItemType Directory -Force -Path $remote | Out-Null; 'ok'"
foreach ($f in $files) {
    $local = if ($f.kind -eq 'app') { Join-Path $Out $f.name } else { Join-Path $Comp $f.name }
    Write-Host "  scp $($f.name)"
    & scp -i "$env:USERPROFILE\.ssh\todox_deploy" -P $cfg.Port -o BatchMode=yes $local "${login}:$remote/$($f.name)"
    if ($LASTEXITCODE -ne 0) { throw "upload failed: $($f.name)" }
}
& scp -i "$env:USERPROFILE\.ssh\todox_deploy" -P $cfg.Port -o BatchMode=yes $manifestPath "${login}:$remote/manifest.json"
if ($LASTEXITCODE -ne 0) { throw 'upload failed: manifest.json' }
Write-Host "RESULT: release $version uploaded"
