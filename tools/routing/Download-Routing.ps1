$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'Stop'
$d = 'H:/WEBDEPLOYED/todoX/routing'
New-Item -ItemType Directory -Force $d, "$d/dl" | Out-Null
Set-Location "$d/dl"; Remove-Item vietnam-latest.osm.pbf.md5 -ErrorAction SilentlyContinue
$files = @{
  'jre21.zip' = 'https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jre/hotspot/normal/eclipse'
  'graphhopper-web-11.1.jar' = 'https://github.com/graphhopper/graphhopper/releases/download/11.1/graphhopper-web-11.1.jar'
  'vietnam-latest.osm.pbf' = 'https://download.geofabrik.de/asia/vietnam/vietnam-261120.osm.pbf'
  'vietnam-latest.osm.pbf.md5' = 'https://download.geofabrik.de/asia/vietnam/vietnam-261120.osm.pbf.md5'
}
foreach ($k in $files.Keys) { if (-not (Test-Path $k)) { Invoke-WebRequest $files[$k] -OutFile $k -UseBasicParsing } }
$md5 = (Get-Content 'vietnam-latest.osm.pbf.md5' -Raw).Split(' ')[0].Trim()
$got = (Get-FileHash 'vietnam-latest.osm.pbf' -Algorithm MD5).Hash.ToLower()
"pbf md5 ok: $($md5 -eq $got)"
Get-ChildItem | Select-Object Name, Length | Format-Table -AutoSize | Out-String
