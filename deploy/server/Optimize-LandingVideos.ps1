<#
.SYNOPSIS
  One-time, server-side: re-encode landing videos already uploaded (Kho mẫu video, intro, /video-ngan)
  into web-optimized MP4 so they stream smoothly from a modest uplink. Never runs SQL.

  - Same settings as new Dashboard uploads (LandingWebVideoTranscoder): shorter side <= 720px,
    H.264 CRF 23 capped at 2.5 Mbps, AAC 128k, MP4 index at the front (+faststart).
  - Each file keeps its name, so URLs stored in the database stay valid.
  - Originals are moved to <StorageRoot>\landing\_original-videos\<area>\ before replacement.
  - Already-optimized files (<= 720p short side, <= 3.2 Mbps, index at front) are skipped, so re-running is safe.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Optimize-LandingVideos.ps1 -WhatIf
  powershell -ExecutionPolicy Bypass -File Optimize-LandingVideos.ps1
#>
param(
    [string] $StorageRoot = 'D:\TodoXData\shared-media',
    [string] $Ffmpeg = 'ffmpeg',
    [string] $Ffprobe = 'ffprobe',
    [string[]] $Areas = @('video-samples', 'intro', 'short-video'),
    # List what would change without touching any file.
    [switch] $WhatIf
)

$ErrorActionPreference = 'Stop'
$MaxShortSide = 720
$SkipBelowBitsPerSecond = 3200000

function Log([string] $m) { Write-Output ('[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $m) }

function Test-IndexAtFront([string] $path) {
    # Walk the top-level MP4 boxes: optimized files have 'moov' before 'mdat'.
    $stream = [System.IO.File]::OpenRead($path)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        while ($stream.Position + 8 -le $stream.Length) {
            $sizeBytes = $reader.ReadBytes(4); [Array]::Reverse($sizeBytes)
            $size = [BitConverter]::ToUInt32($sizeBytes, 0)
            $type = [System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))
            if ($type -eq 'moov') { return $true }
            if ($type -eq 'mdat') { return $false }
            if ($size -eq 1) {
                $largeBytes = $reader.ReadBytes(8); [Array]::Reverse($largeBytes)
                $size = [BitConverter]::ToUInt64($largeBytes, 0)
                $stream.Position += $size - 16
            } elseif ($size -lt 8) { return $false }
            else { $stream.Position += $size - 8 }
        }
        return $false
    } finally { $stream.Dispose() }
}

function Get-VideoInfo([string] $path) {
    $dims = (& $Ffprobe -v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 $path) -split ','
    $rate = & $Ffprobe -v error -show_entries format=bit_rate -of csv=p=0 $path
    [pscustomobject]@{ Width = [int]$dims[0]; Height = [int]$dims[1]; BitRate = [long]$rate }
}

& $Ffmpeg -hide_banner -version | Select-Object -First 1 | ForEach-Object { Log "Using $_" }

$changed = 0; $skipped = 0; $failed = 0
foreach ($area in $Areas) {
    $folder = Join-Path $StorageRoot "landing\$area\videos"
    if (-not (Test-Path $folder)) { Log "[$area] no folder $folder, skipped"; continue }

    foreach ($file in Get-ChildItem -Path $folder -Filter '*.mp4' -File) {
        $path = $file.FullName
        $temp = $null
        try {
            $info = Get-VideoInfo $path
            $short = [Math]::Min($info.Width, $info.Height)
            $front = Test-IndexAtFront $path
            $label = '{0} {1}x{2} {3:N1} Mbps {4:N1} MB index-front={5}' -f $file.Name, $info.Width, $info.Height, ($info.BitRate / 1e6), ($file.Length / 1MB), $front
            if ($short -le $MaxShortSide -and $info.BitRate -le $SkipBelowBitsPerSecond -and $front) {
                Log "[$area] OK   $label"; $skipped++; continue
            }
            if ($WhatIf) { Log "[$area] WOULD optimize $label"; continue }

            Log "[$area] optimizing $label"
            $temp = Join-Path $folder ($file.BaseName + '.web.tmp.mp4')
            & $Ffmpeg -hide_banner -loglevel error -y -i $path -map '0:v:0' -map '0:a?' `
                -vf "scale=w='if(lte(iw,ih),min($MaxShortSide,iw),-2)':h='if(lte(iw,ih),-2,min($MaxShortSide,ih))'" `
                -c:v libx264 -preset medium -crf 23 -maxrate 2500k -bufsize 5000k -pix_fmt yuv420p -profile:v high -level 4.1 `
                -movflags +faststart -c:a aac -b:a 128k -ar 48000 -ac 2 $temp
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path $temp) -or (Get-Item $temp).Length -eq 0) {
                throw "ffmpeg exit code $LASTEXITCODE"
            }

            $backupFolder = Join-Path $StorageRoot "landing\_original-videos\$area"
            New-Item -ItemType Directory -Force -Path $backupFolder | Out-Null
            $backup = Join-Path $backupFolder $file.Name
            Move-Item -LiteralPath $path -Destination $backup -Force
            try { Move-Item -LiteralPath $temp -Destination $path }
            catch { Move-Item -LiteralPath $backup -Destination $path; throw }
            $new = Get-VideoInfo $path
            Log ('[{0}] done {1} -> {2}x{3} {4:N1} Mbps {5:N1} MB' -f $area, $file.Name, $new.Width, $new.Height, ($new.BitRate / 1e6), ((Get-Item $path).Length / 1MB))
            $changed++
        } catch {
            Log "[$area] FAILED $($file.Name): $($_.Exception.Message)"
            if ($temp -and (Test-Path $temp)) { Remove-Item -LiteralPath $temp -Force }
            $failed++
        }
    }
}
Log "Finished: optimized=$changed alreadyOk=$skipped failed=$failed$(if ($WhatIf) { ' (WhatIf: nothing changed)' })"
