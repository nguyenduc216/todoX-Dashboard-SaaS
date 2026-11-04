<#
.SYNOPSIS
  Follow-up for Setup-TodoXDeployServer.ps1. Run on the SERVER as Administrator (Fix-TodoXDeployServer.bat).

  1. Fixes the landing health URL (landing has no /system/version page).
  2. Checks that sshd is running and listening, and that the Windows Firewall rule exists.
  3. Tries to create the router port forward  WAN :ExternalPort -> this server :22  via UPnP.
     Many office/data-centre routers have UPnP turned off; then the script prints exactly what
     to enter in the router's NAT / Port Forwarding page.
  4. Writes C:\TodoXDeploy\server-check.txt to send back to Claude.
#>
param(
    [int] $ExternalPort = 22022,
    [string] $LandingHealthUrl = 'https://todox.vn/'
)

$ErrorActionPreference = 'Stop'
$DeployRoot = 'C:\TodoXDeploy'
$configPath = Join-Path $DeployRoot 'deploy-config.json'
$report = New-Object System.Collections.Generic.List[string]
function Say([string] $text, [string] $color = 'Gray') { Write-Host $text -ForegroundColor $color; $report.Add($text) }

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Hay chay bang quyen Administrator.'
}
if (-not (Test-Path $configPath)) { throw "Chua co $configPath - hay chay Setup-TodoXDeployServer.bat truoc." }

# ---------------------------------------------------------------- 1. landing health URL
Say '== 1. Sua URL kiem tra cua landing' Cyan
$cfg = Get-Content $configPath -Raw | ConvertFrom-Json
if ($cfg.Targets.landing) {
    $cfg.Targets.landing.HealthUrl = $LandingHealthUrl
    $cfg | ConvertTo-Json -Depth 5 | Set-Content -Path $configPath -Encoding UTF8
    Say "   landing health = $LandingHealthUrl" Green
}
else {
    Say '   (chua dang ky target landing - bo qua)'
}
foreach ($p in $cfg.Targets.PSObject.Properties) { Say "   target $($p.Name): $($p.Value.SitePath) | pool=$($p.Value.AppPool) | health=$($p.Value.HealthUrl)" }

# ---------------------------------------------------------------- 2. sshd + firewall
Say '== 2. Kiem tra OpenSSH Server' Cyan
$svc = Get-Service sshd -ErrorAction SilentlyContinue
if (-not $svc) { throw 'Chua cai OpenSSH Server - chay lai Setup-TodoXDeployServer.bat.' }
if ($svc.Status -ne 'Running') { Start-Service sshd; $svc.Refresh() }
Say "   sshd: $($svc.Status), startup=$((Get-CimInstance Win32_Service -Filter "Name='sshd'").StartMode)"

$sshdConfig = Get-Content (Join-Path $env:ProgramData 'ssh\sshd_config')
$portLine = $sshdConfig | Where-Object { $_ -match '^\s*Port\s+\d+' } | Select-Object -First 1
$internalPort = if ($portLine) { [int]($portLine -replace '\D', '') } else { 22 }
$listening = Get-NetTCPConnection -State Listen -LocalPort $internalPort -ErrorAction SilentlyContinue
Say "   lang nghe cong $internalPort : $(if ($listening) { 'CO' } else { 'KHONG' })" $(if ($listening) { 'Green' } else { 'Red' })

$rule = Get-NetFirewallRule -DisplayName 'TodoX Deploy SSH' -ErrorAction SilentlyContinue
if (-not $rule) {
    New-NetFirewallRule -DisplayName 'TodoX Deploy SSH' -Direction Inbound -Protocol TCP -LocalPort $internalPort -Action Allow | Out-Null
    Say '   firewall: da tao lai rule TodoX Deploy SSH' Yellow
}
else {
    Say "   firewall: rule TodoX Deploy SSH = $($rule.Enabled)"
}

$keys = Join-Path $env:ProgramData 'ssh\administrators_authorized_keys'
Say "   khoa deploy: $(if ((Test-Path $keys) -and (Select-String -Path $keys -Pattern 'todox-deploy' -Quiet)) { 'CO' } else { 'KHONG' })"

# ---------------------------------------------------------------- 3. router port forward via UPnP
Say "== 3. Mo cong tren router: WAN $ExternalPort -> may nay :$internalPort" Cyan
$lanIp = (Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } |
    Select-Object -First 1).IPv4Address.IPAddress
$gateway = (Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway } | Select-Object -First 1).IPv4DefaultGateway.NextHop
Say "   IP noi bo server: $lanIp | router (gateway): $gateway"

$upnpOk = $false
try {
    $nat = New-Object -ComObject HNetCfg.NATUPnP
    $maps = $nat.StaticPortMappingCollection
    if ($null -ne $maps) {
        $existing = $null
        try { $existing = $maps.Item($ExternalPort, 'TCP') } catch { }
        if ($existing) { $maps.Remove($ExternalPort, 'TCP') }
        [void]$maps.Add($ExternalPort, 'TCP', $internalPort, $lanIp, $true, 'TodoX Deploy SSH')
        $upnpOk = $true
        Say "   UPnP: DA TAO port forward $ExternalPort -> ${lanIp}:$internalPort" Green
    }
    else {
        Say '   UPnP: router khong ho tro hoac dang tat UPnP.' Yellow
    }
}
catch {
    Say "   UPnP: khong dung duoc ($($_.Exception.Message))." Yellow
}

if (-not $upnpOk) {
    Say ''
    Say '   >>> Can tao tay tren trang quan tri router (NAT / Port Forwarding / Virtual Server):' Yellow
    Say "       Giao thuc     : TCP"
    Say "       Cong ngoai    : $ExternalPort"
    Say "       IP noi bo     : $lanIp"
    Say "       Cong noi bo   : $internalPort"
    Say "       Trang router  : http://$gateway  (giong cach da mo cong 5432 cho PostgreSQL)"
}

# ---------------------------------------------------------------- 4. report
Say '== 4. Gui file C:\TodoXDeploy\server-check.txt cho Claude' Cyan
Set-Content -Path (Join-Path $DeployRoot 'server-check.txt') -Value $report -Encoding UTF8
