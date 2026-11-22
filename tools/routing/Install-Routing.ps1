<#
.SYNOPSIS
  Installs TodoX's free routing service (GraphHopper 11.1 + OpenStreetMap Vietnam) on the production server, in
  H:\WEBDEPLOYED\todoX\routing (never C:). Run ON THE SERVER as Administrator:

    powershell -NoProfile -ExecutionPolicy Bypass -File C:\TodoXDeploy\Install-Routing.ps1

  1. Downloads (if missing) a portable Java 21 (Eclipse Temurin), graphhopper-web-11.1.jar and the Vietnam OSM
     extract (Geofabrik, MD5 checked) into routing\dl.
  2. Unzips Java into routing\jre (nothing is installed system-wide), copies config.yml next to it.
  3. Builds the road graph once (5-15 minutes, ~3 GB RAM).
  4. Registers the Windows scheduled task "TodoX Routing" (at startup, as SYSTEM) and starts it.
     GraphHopper listens on http://localhost:8989 only; the dashboard proxies it for licensed EditVideo apps.
  Re-run with -Rebuild to refresh the OSM data and rebuild the graph.
#>
param([switch] $Rebuild, [string] $Extract = '')

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$d = 'H:\WEBDEPLOYED\todoX\routing'
$java = "$d\jre\bin\java.exe"
$task = 'TodoX Routing'
New-Item -ItemType Directory -Force $d, "$d\dl", "$d\logs" | Out-Null

function Step($t) { Write-Host "== $t" -ForegroundColor Cyan }

# ------------------------------------------------------------------ downloads
Step 'Download'
if ($Rebuild) { Remove-Item "$d\dl\vietnam-latest.osm.pbf*" -ErrorAction SilentlyContinue }
if (-not $Extract) {
    # "latest" can point at today's file before Geofabrik has published it: take the newest dated file that exists.
    $page = (Invoke-WebRequest 'https://download.geofabrik.de/asia/vietnam/' -UseBasicParsing).Content
    $dates = [regex]::Matches($page, 'vietnam-(\d{6})\.osm\.pbf"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique -Descending
    foreach ($date in $dates) {
        try { Invoke-WebRequest "https://download.geofabrik.de/asia/vietnam/vietnam-$date.osm.pbf.md5" -UseBasicParsing -Method Head | Out-Null; $Extract = $date; break } catch { }
    }
}
$files = [ordered]@{
    'jre21.zip'                  = 'https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jre/hotspot/normal/eclipse'
    'graphhopper-web-11.1.jar'   = 'https://github.com/graphhopper/graphhopper/releases/download/11.1/graphhopper-web-11.1.jar'
    'vietnam-latest.osm.pbf'     = "https://download.geofabrik.de/asia/vietnam/vietnam-$Extract.osm.pbf"
    'vietnam-latest.osm.pbf.md5' = "https://download.geofabrik.de/asia/vietnam/vietnam-$Extract.osm.pbf.md5"
}
foreach ($name in $files.Keys) {
    if (-not (Test-Path "$d\dl\$name")) {
        Write-Host "  $name"
        Invoke-WebRequest $files[$name] -OutFile "$d\dl\$name" -UseBasicParsing
    }
}
$md5 = (Get-Content "$d\dl\vietnam-latest.osm.pbf.md5" -Raw).Split(' ')[0].Trim()
if ((Get-FileHash "$d\dl\vietnam-latest.osm.pbf" -Algorithm MD5).Hash.ToLower() -ne $md5) { throw 'Vietnam OSM file is damaged (MD5 mismatch); delete routing\dl\vietnam-latest.osm.pbf* and re-run.' }
Write-Host "  OSM Vietnam $Extract OK (MD5)"

# ------------------------------------------------------------------ java + graphhopper
Step 'Java + GraphHopper'
if (-not (Test-Path $java)) {
    Expand-Archive "$d\dl\jre21.zip" "$d\jre-tmp" -Force
    Move-Item (Get-ChildItem "$d\jre-tmp" -Directory | Select-Object -First 1).FullName "$d\jre"
    Remove-Item "$d\jre-tmp" -Recurse -Force
}
Copy-Item "$d\dl\graphhopper-web-11.1.jar" "$d\graphhopper-web.jar" -Force
Copy-Item "$d\dl\vietnam-latest.osm.pbf" "$d\vietnam-latest.osm.pbf" -Force
if (Test-Path "$PSScriptRoot\config.yml") { Copy-Item "$PSScriptRoot\config.yml" "$d\config.yml" -Force }
if (-not (Test-Path "$d\config.yml")) { throw "config.yml missing: copy tools/routing/config.yml next to this script." }
Write-Host "  Java: $((Get-Item $java).VersionInfo.ProductVersion)"

# ------------------------------------------------------------------ graph
Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue | Stop-ScheduledTask -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='java.exe'" | Where-Object { $_.CommandLine -like '*graphhopper-web.jar*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
if ($Rebuild -and (Test-Path "$d\graph-cache")) { Remove-Item "$d\graph-cache" -Recurse -Force }
if (-not (Test-Path "$d\graph-cache\properties.txt")) {
    Step 'Build road graph (5-15 minutes)'
    # Java writes its log to stderr: run it through cmd so PowerShell does not treat that as an error.
    cmd /c "`"$java`" -Xmx5g -Xms1g -jar `"$d\graphhopper-web.jar`" import `"$d\config.yml`" > `"$d\logs\import.log`" 2>&1"
    if ($LASTEXITCODE -ne 0) { throw "Graph import failed (exit $LASTEXITCODE): see $d\logs\import.log" }
}

# ------------------------------------------------------------------ service
Step "Scheduled task '$task'"
$action = New-ScheduledTaskAction -Execute $java -Argument "-Xmx3g -Xms1g -jar `"$d\graphhopper-web.jar`" server `"$d\config.yml`"" -WorkingDirectory $d
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable
Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Settings $settings -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
Start-ScheduledTask -TaskName $task

Step 'Check'
$ok = $false
foreach ($i in 1..60) {
    Start-Sleep 5
    try {
        $r = Invoke-RestMethod 'http://localhost:8989/route?point=10.817,106.765&point=10.7769,106.7009&profile=car&points_encoded=false&calc_points=false' -TimeoutSec 10
        Write-Host ("  Phước Long -> Quận 1: {0:0.0} km, {1:0} phút" -f ($r.paths[0].distance / 1000), ($r.paths[0].time / 60000))
        $ok = $true; break
    } catch { }
}
if (-not $ok) { throw "GraphHopper did not answer on localhost:8989; see $d\logs\graphhopper.log" }
Write-Host 'RESULT: routing ready on http://localhost:8989' -ForegroundColor Green
