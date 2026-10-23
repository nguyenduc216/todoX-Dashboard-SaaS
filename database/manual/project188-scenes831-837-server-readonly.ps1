# READ ONLY: IIS/process/log evidence inventory. No stop/start/recycle/config/network/provider operation.
# Run ON EACH production host already identified by the operator. Output contains indexes, not raw log messages.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$IisApplicationName,
    [Parameter(Mandatory=$true)][string]$PhysicalPath,
    [Parameter(Mandatory=$true)][datetime]$SinceUtc,
    [string[]]$LogRoot=@()
)
$ErrorActionPreference='Stop'
$jobs=@(
 '30663c04-ea85-4738-87ed-a93d2414f694','e130accb-4423-445c-893e-46bce98b63e9',
 '0404a954-793a-4a2d-8cd1-706de24c9ca5','47d877ea-1ab9-4f28-bec8-9fc5810d45c5',
 '1f53cedf-0347-4fff-9e42-1347ba8cec60','fca7c1b7-5829-495f-90e2-e47f5e074651',
 '236d182e-2c02-4238-8f87-8b9892e7bdf3'
)
$guard='Selected version does not belong to this job/scene/logical request; submit is prohibited.'
$markers=@('RVIDEO_VIDEO_HTTP_SUBMIT_REQUEST','RVIDEO_VIDEO_HTTP_SUBMIT_RESPONSE',
 'RVIDEO_VIDEO_HTTP_SUBMIT_RESPONSE_PARSE_FAILED','SCENE_VIDEO_PROVIDER_SUBMITTED',
 'RVIDEO_VIDEO_SUBMIT_BEGIN','RVIDEO_VIDEO_SUBMIT_UNKNOWN','RVIDEO_79AI_SUBMIT_DIAGNOSTICS')
$appcmd=Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
$errors=[Collections.Generic.List[object]]::new()
function Read-AppCmdXml([string[]]$Arguments) {
    $raw=& $appcmd @Arguments 2>&1
    if($LASTEXITCODE -ne 0){ throw 'IIS inventory unavailable; run in an authorized elevated server session. No state changed.' }
    $xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null;$xml.LoadXml(($raw -join "`n"));return $xml
}
$appInfo=$null;$poolState=$null;$sharedApps=@();$wps=@();$vdirs=@()
try {
    $apps=Read-AppCmdXml -Arguments @('list','app',$IisApplicationName,'/xml')
    $app=@($apps.SelectNodes('/appcmd/APP')) | Where-Object { $_.GetAttribute('APP.NAME') -eq $IisApplicationName } | Select-Object -First 1
    if($null -eq $app){throw 'Exact IIS application not found; do not guess pool ownership.'}
    $pool=$app.GetAttribute('APPPOOL.NAME')
    $appInfo=[pscustomobject]@{Application=$IisApplicationName;Pool=$pool}
    $poolState=(& $appcmd list apppool $pool /text:state 2>&1) -join "`n"
    if($LASTEXITCODE -ne 0){throw 'Pool state query failed.'}
    $shared=Read-AppCmdXml -Arguments @('list','app',('/apppool.name:'+ $pool),'/xml')
    $sharedApps=@($shared.SelectNodes('/appcmd/APP') | ForEach-Object { $_.GetAttribute('APP.NAME') })
    $workers=Read-AppCmdXml -Arguments @('list','wp',('/apppool.name:'+ $pool),'/xml')
    $wps=@($workers.SelectNodes('/appcmd/WP') | ForEach-Object { [pscustomobject]@{ProcessId=$_.GetAttribute('WP.NAME');Pool=$_.GetAttribute('APPPOOL.NAME')} })
    $dirs=Read-AppCmdXml -Arguments @('list','vdir',('/app.name:'+ $IisApplicationName),'/xml')
    $vdirs=@($dirs.SelectNodes('/appcmd/VDIR') | ForEach-Object { [pscustomobject]@{Name=$_.GetAttribute('VDIR.NAME');PhysicalPath=$_.GetAttribute('physicalPath')} })
} catch {$errors.Add([pscustomobject]@{Stage='IIS';ErrorType=$_.Exception.GetType().Name;Note='Inventory unavailable/incomplete; no fencing conclusion is possible.'})}
$processes=@()
try {$processes=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -match '^(w3wp|dotnet|TodoX.*)\.exe$'} |
    Select-Object ProcessId,ParentProcessId,Name,CreationDate,ExecutablePath)}
catch {$errors.Add([pscustomobject]@{Stage='Processes';ErrorType=$_.Exception.GetType().Name})}
$dllHash=$null;$routing=$null
try {
    $dllHash=(Get-FileHash -LiteralPath (Join-Path $PhysicalPath 'TodoX.Web.dll') -Algorithm SHA256).Hash
    $web=Join-Path $PhysicalPath 'web.config'
    if(Test-Path -LiteralPath $web){
        $xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null;$xml.Load($web)
        $asp=$xml.SelectSingleNode('//aspNetCore')
        if($null -ne $asp){$routing=[pscustomobject]@{HostingModel=$asp.GetAttribute('hostingModel');StdoutEnabled=$asp.GetAttribute('stdoutLogEnabled');StdoutPath=$asp.GetAttribute('stdoutLogFile')}}
    }
} catch {$errors.Add([pscustomobject]@{Stage='Artifact/config routing';ErrorType=$_.Exception.GetType().Name})}
$patterns=@($jobs)+@($jobs | ForEach-Object {$_.Replace('-','')})+@($guard)
$fileIndex=[Collections.Generic.List[object]]::new();$matchIndex=[Collections.Generic.List[object]]::new()
foreach($root in $LogRoot){
 try {
  foreach($file in (Get-ChildItem -LiteralPath $root -File -Recurse -ErrorAction Stop |
      Where-Object {$_.LastWriteTimeUtc -ge $SinceUtc.ToUniversalTime() -and $_.Extension -notin @('.gz','.zip','.rar','.7z') -and ($_.Extension -in @('.log','.jsonl','.ndjson','.txt') -or $_.Name -like '*.log.*')})){
   if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){continue}
   $fileIndex.Add([pscustomobject]@{Path=$file.FullName;Bytes=$file.Length;LastWriteTimeUtc=$file.LastWriteTimeUtc})
   foreach($hit in (Select-String -LiteralPath $file.FullName -Pattern $patterns -SimpleMatch -ErrorAction Stop)){
     $line=$hit.Line.ToLowerInvariant()
     $found=@($jobs | Where-Object {$line.Contains($_) -or $line.Contains($_.Replace('-',''))})
     $present=@($markers | Where-Object {$line.Contains($_.ToLowerInvariant())})
     $matchIndex.Add([pscustomobject]@{Path=$file.FullName;LineNumber=$hit.LineNumber;JobIds=$found;IdentityGuard=$line.Contains($guard.ToLowerInvariant());Markers=$present})
   }
  }
 } catch {$errors.Add([pscustomobject]@{Stage='Log files';Root=$root;ErrorType=$_.Exception.GetType().Name})}
}
$eventIndex=@()
try {
 $eventIndex=@(Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$SinceUtc.ToUniversalTime()} -ErrorAction Stop |
   Where-Object { $message=$_.Message; @($patterns | Where-Object {$message -and $message.IndexOf($_,[StringComparison]::OrdinalIgnoreCase) -ge 0}).Count -gt 0 } |
   ForEach-Object { $event=$_;$message=$event.Message.ToLowerInvariant();[pscustomobject]@{TimeCreated=$event.TimeCreated;Provider=$event.ProviderName;Id=$event.Id;RecordId=$event.RecordId;JobIds=@($jobs | Where-Object {$message.Contains($_) -or $message.Contains($_.Replace('-',''))});IdentityGuard=$message.Contains($guard.ToLowerInvariant())} })
} catch {$errors.Add([pscustomobject]@{Stage='Application events';ErrorType=$_.Exception.GetType().Name;Note='No matching events or access unavailable; not proof of absence.'})}
[pscustomobject]@{
 Host=$env:COMPUTERNAME;CapturedUtc=[datetime]::UtcNow;SinceUtc=$SinceUtc.ToUniversalTime();
 IIS=$appInfo;PoolState=$poolState;ApplicationsSharingPool=$sharedApps;PoolWorkers=$wps;VirtualDirectories=$vdirs;
 Processes=$processes;RequestedPhysicalPath=$PhysicalPath;DllSHA256=$dllHash;StdoutRouting=$routing;
 LogFiles=$fileIndex.ToArray();LogMatches=$matchIndex.ToArray();ApplicationEvents=$eventIndex;
 Errors=$errors.ToArray();WorkerFenced=$false;NoSubmitApproved=$false;
 Limitations='Read-only inventory/index only. Compressed archives are not searched by this collector and must be reviewed separately if retained. File timestamps are not event timestamps; raw adjacent lines and full coverage require secure review. No requests to the web application/provider were issued. No stop/start/recycle/config changes.'
} | ConvertTo-Json -Depth 8
