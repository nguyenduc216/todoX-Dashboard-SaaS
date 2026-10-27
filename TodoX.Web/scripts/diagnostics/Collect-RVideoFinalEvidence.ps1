[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Ffmpeg,
    [Parameter(Mandatory=$true)][string]$Manifest,
    [Parameter(Mandatory=$true)][string]$FinalVideo,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
# Reads source files only. All generated evidence goes into a new, separate directory.
$manifestFile = (Resolve-Path -LiteralPath $Manifest).Path
$finalFile = (Resolve-Path -LiteralPath $FinalVideo).Path
$ffmpegFile = (Resolve-Path -LiteralPath $Ffmpeg).Path
$ffprobeFile = Join-Path (Split-Path -Parent $ffmpegFile) 'ffprobe.exe'
if (!(Test-Path -LiteralPath $ffprobeFile)) { throw 'ffprobe.exe must be alongside ffmpeg.exe.' }
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidenceRoot) { throw 'Choose a new evidence directory; existing evidence is never overwritten.' }
$composition = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
$inputs = @($composition.items | Sort-Object itemOrder | ForEach-Object { $_.videoPath })
$files = @($inputs) + @($finalFile)
foreach ($file in $files) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing source file: $file" }
}
New-Item -ItemType Directory -Path $evidenceRoot | Out-Null
Copy-Item -LiteralPath $manifestFile -Destination (Join-Path $evidenceRoot 'composition.json')
$finalDirectory = Split-Path -Parent $finalFile
foreach ($name in @('concat.txt','concat-normalized.txt','ffmpeg-copy.log','ffmpeg-fallback.log','ffmpeg-music.log','copy-validation.log','fallback-validation.log','final-validation.log','input-probes.json')) {
    $logFile = Join-Path $finalDirectory $name
    if (Test-Path -LiteralPath $logFile) { Copy-Item -LiteralPath $logFile -Destination (Join-Path $evidenceRoot $name) }
}
function Invoke-MediaEvidence {
    param([string]$Executable, [string[]]$Arguments, [string]$OutputFile, [string]$ErrorFile)
    # Windows PowerShell 5.1 wraps native stderr in ErrorRecord, including normal FFmpeg logs.
    $ErrorActionPreference = 'Continue'
    & $Executable @Arguments 2> $ErrorFile | Out-File -LiteralPath $OutputFile -Encoding utf8
    return $LASTEXITCODE
}
$versionCode = Invoke-MediaEvidence $ffmpegFile @('-version') (Join-Path $evidenceRoot 'ffmpeg-version.txt') (Join-Path $evidenceRoot 'version-errors.log')
$index = 0
foreach ($file in $files) {
    $label = if ($index -eq $inputs.Count) { 'final' } else { 'scene-{0:D4}' -f $index }
    Get-FileHash -LiteralPath $file -Algorithm SHA256 | Format-List | Out-File (Join-Path $evidenceRoot "$label-sha256.txt") -Encoding utf8
    $probeCode = Invoke-MediaEvidence $ffprobeFile @('-v','error','-show_streams','-show_format','-of','json',$file) (Join-Path $evidenceRoot "$label-streams.json") (Join-Path $evidenceRoot "$label-probe-errors.log")
    $packetCode = Invoke-MediaEvidence $ffprobeFile @('-v','error','-select_streams','v:0','-show_packets','-show_entries','packet=pts_time,dts_time,duration_time,flags','-of','csv=p=0',$file) (Join-Path $evidenceRoot "$label-packets.csv") (Join-Path $evidenceRoot "$label-packet-errors.log")
    $frameCode = Invoke-MediaEvidence $ffprobeFile @('-v','error','-select_streams','v:0','-show_frames','-show_entries','frame=best_effort_timestamp_time,pkt_dts_time,duration_time','-of','csv=p=0',$file) (Join-Path $evidenceRoot "$label-frames.csv") (Join-Path $evidenceRoot "$label-frame-errors.log")
    $decodeCode = Invoke-MediaEvidence $ffmpegFile @('-hide_banner','-v','error','-xerror','-err_detect','explode','-i',$file,'-map','0:v:0','-an','-f','null','-') (Join-Path $evidenceRoot "$label-decode-stdout.txt") (Join-Path $evidenceRoot "$label-decode.log")
    $freezeCode = Invoke-MediaEvidence $ffmpegFile @('-hide_banner','-i',$file,'-map','0:v:0','-an','-vf','freezedetect=n=-60dB:d=1','-f','null','-') (Join-Path $evidenceRoot "$label-freeze-stdout.txt") (Join-Path $evidenceRoot "$label-freeze.log")
    # Decoded frame hashes distinguish repeated pictures from a browser-only display issue.
    $hashCode = Invoke-MediaEvidence $ffmpegFile @('-v','error','-i',$file,'-map','0:v:0','-an','-f','framemd5','-') (Join-Path $evidenceRoot "$label-framemd5.txt") (Join-Path $evidenceRoot "$label-hash-errors.log")
    "probe=$probeCode packet=$packetCode frame=$frameCode decode=$decodeCode freeze=$freezeCode hash=$hashCode" | Out-File (Join-Path $evidenceRoot "$label-exit-codes.txt") -Encoding utf8
    $index++
}
Write-Output "Read-only source diagnostics complete: $evidenceRoot"
