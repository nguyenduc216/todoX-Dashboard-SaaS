<#
.SYNOPSIS
  One-time setup of the TodoX production server so deploys can run automatically over SSH.
  Run on the SERVER as Administrator (double-click Setup-TodoXDeployServer.bat).

  What it does (asks before each important choice):
    1. Lists IIS sites, you pick the TodoX dashboard site -> finds its folder and app pool.
    2. Installs and starts the Windows OpenSSH Server, PowerShell as the SSH shell.
    3. Authorises ONLY the TodoX deploy key (todox_deploy.pub next to this script), key login only.
    4. Opens the SSH port in Windows Firewall, optionally only for the IP of the deploy machine.
    5. Creates C:\TodoXDeploy with Apply-TodoXRelease.ps1 + deploy-config.json.
    6. Prints a summary to send back to Claude.

  It never touches the database, the site files or appsettings.
#>
$ErrorActionPreference = 'Stop'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$DeployRoot = 'C:\TodoXDeploy'

function Ask([string] $Question, [string] $Default) {
    $suffix = if ($Default) { " [$Default]" } else { '' }
    $answer = Read-Host "$Question$suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer.Trim()
}

function Step([string] $Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Hay chay bang quyen Administrator (chuot phai > Run as administrator).'
}

# ---------------------------------------------------------------- 1. IIS site
Step '1. Dang ky cac website TodoX trong IIS (moi du an = 1 target)'
Import-Module WebAdministration
$sites = @(Get-Website | Sort-Object Name)
if ($sites.Count -eq 0) { throw 'Khong tim thay website nao trong IIS.' }
for ($i = 0; $i -lt $sites.Count; $i++) {
    $s = $sites[$i]
    $bind = ($s.Bindings.Collection | ForEach-Object { $_.bindingInformation }) -join ', '
    Write-Host ("  [{0}] {1}  |  pool={2}  |  {3}  |  {4}" -f ($i + 1), $s.Name, $s.applicationPool, $s.PhysicalPath, $bind)
}

# Each TodoX project in deploy/targets.json (dashboard, landing, ...) is registered under its target name.
# Running the setup again adds/updates targets and keeps the ones already registered.
$KnownTargets = [ordered]@{ dashboard = 'TodoX.Web.dll'; landing = 'TodoX.Landing.dll' }
$configPath = Join-Path $DeployRoot 'deploy-config.json'
$targets = [ordered]@{}
if (Test-Path $configPath) {
    $old = Get-Content $configPath -Raw | ConvertFrom-Json
    if ($old.Targets) { $old.Targets.PSObject.Properties | ForEach-Object { $targets[$_.Name] = $_.Value } }
}

do {
    $targetName = (Ask "Ten target (vd: $($KnownTargets.Keys -join ', '))" 'dashboard').ToLowerInvariant()
    $mainDll = Ask 'File DLL chinh cua du an' $(if ($KnownTargets[$targetName]) { $KnownTargets[$targetName] } else { 'TodoX.Web.dll' })
    $pick = [int](Ask "Nhap so thu tu website IIS cho target '$targetName'" '1')
    $site = $sites[$pick - 1]
    $sitePath = [Environment]::ExpandEnvironmentVariables($site.PhysicalPath)
    Write-Host "  Website : $($site.Name)"
    Write-Host "  Thu muc : $sitePath"
    Write-Host "  AppPool : $($site.applicationPool)"
    if (-not (Test-Path (Join-Path $sitePath $mainDll))) {
        Write-Warning "Khong thay $mainDll trong $sitePath - kiem tra lai website da chon."
        if ((Ask 'Van dang ky target nay? (y/n)' 'n') -ne 'y') { continue }
    }

    $hostBinding = ($site.Bindings.Collection | Where-Object { $_.protocol -eq 'https' } | Select-Object -First 1)
    if (-not $hostBinding) { $hostBinding = $site.Bindings.Collection | Select-Object -First 1 }
    $parts = $hostBinding.bindingInformation.Split(':')
    $hostName = if ($parts.Count -ge 3 -and $parts[2]) { $parts[2] } else { 'localhost' }
    $healthUrl = Ask 'URL kiem tra sau khi deploy (bo trong = chi kiem tra trang chu tra ve 200)' "$($hostBinding.protocol)://$hostName/system/version"

    $targets[$targetName] = [ordered]@{
        SiteName  = $site.Name
        SitePath  = $sitePath
        AppPool   = $site.applicationPool
        MainDll   = $mainDll
        HealthUrl = $healthUrl
    }
    Write-Host "  => Da dang ky target '$targetName'" -ForegroundColor Green
} while ((Ask 'Dang ky them website/du an khac? (y/n)' 'n') -eq 'y')

# ---------------------------------------------------------------- 2. OpenSSH server
Step '2. Cai OpenSSH Server'
$cap = Get-WindowsCapability -Online -Name 'OpenSSH.Server*' | Select-Object -First 1
if ($cap.State -ne 'Installed') {
    Write-Host '  Dang cai OpenSSH Server (co the mat vai phut)...'
    Add-WindowsCapability -Online -Name $cap.Name | Out-Null
}
Set-Service sshd -StartupType Automatic
Start-Service sshd
New-Item -Path 'HKLM:\SOFTWARE\OpenSSH' -Force | Out-Null
New-ItemProperty -Path 'HKLM:\SOFTWARE\OpenSSH' -Name DefaultShell -PropertyType String -Force `
    -Value "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" | Out-Null
Write-Host '  OpenSSH Server dang chay, shell mac dinh = PowerShell.'

# ---------------------------------------------------------------- 3. Deploy key
Step '3. Cho phep khoa SSH cua may deploy'
$pubFile = Join-Path $Here 'todox_deploy.pub'
$pubKey = if (Test-Path $pubFile) { (Get-Content $pubFile -Raw).Trim() } else { Read-Host 'Dan public key (dong bat dau bang ssh-ed25519)' }
if ($pubKey -notmatch '^ssh-(ed25519|rsa) ') { throw 'Public key khong hop le.' }

# Administrators authenticate from administrators_authorized_keys (Windows OpenSSH rule).
$authKeys = Join-Path $env:ProgramData 'ssh\administrators_authorized_keys'
$existing = if (Test-Path $authKeys) { Get-Content $authKeys } else { @() }
if ($existing -notcontains $pubKey) { Add-Content -Path $authKeys -Value $pubKey -Encoding ascii }
& icacls $authKeys /inheritance:r /grant 'Administrators:F' /grant 'SYSTEM:F' | Out-Null
Write-Host "  Da them khoa vao $authKeys"

$sshdConfig = Join-Path $env:ProgramData 'ssh\sshd_config'
$port = [int](Ask 'Cong SSH' '22')
$keyOnly = (Ask 'Chi cho dang nhap bang khoa, tat dang nhap bang mat khau? (y/n)' 'y') -eq 'y'
$cfg = Get-Content $sshdConfig
$cfg = $cfg | Where-Object { $_ -notmatch '^\s*(Port|PasswordAuthentication|PubkeyAuthentication)\s' }
$cfg = @("Port $port", 'PubkeyAuthentication yes', ("PasswordAuthentication " + ($(if ($keyOnly) { 'no' } else { 'yes' })))) + $cfg
Set-Content -Path $sshdConfig -Value $cfg -Encoding ascii
Restart-Service sshd

# ---------------------------------------------------------------- 4. Firewall
Step '4. Mo cong tren Windows Firewall'
$allowFrom = Ask 'IP public cua may deploy (cach nhau dau phay; bo trong = moi IP)' ''
Get-NetFirewallRule -DisplayName 'TodoX Deploy SSH' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
$ruleArgs = @{ DisplayName = 'TodoX Deploy SSH'; Direction = 'Inbound'; Protocol = 'TCP'; LocalPort = $port; Action = 'Allow' }
if ($allowFrom) { $ruleArgs.RemoteAddress = $allowFrom.Split(',') | ForEach-Object { $_.Trim() } }
New-NetFirewallRule @ruleArgs | Out-Null
if (-not $allowFrom) { Write-Warning 'Cong SSH mo cho moi IP. Nen gioi han IP khi co the.' }

# ---------------------------------------------------------------- 5. Deploy folder
Step "5. Tao $DeployRoot"
foreach ($d in 'incoming', 'logs', 'staging', 'backups') { New-Item -ItemType Directory -Force -Path (Join-Path $DeployRoot $d) | Out-Null }
Copy-Item (Join-Path $Here 'Apply-TodoXRelease.ps1') (Join-Path $DeployRoot 'Apply-TodoXRelease.ps1') -Force
Unblock-File (Join-Path $DeployRoot 'Apply-TodoXRelease.ps1')
[ordered]@{
    BackupRoot = (Join-Path $DeployRoot 'backups')
    Incoming   = (Join-Path $DeployRoot 'incoming')
    Targets    = $targets
} | ConvertTo-Json -Depth 5 | Set-Content -Path $configPath -Encoding UTF8

# ---------------------------------------------------------------- 6. Summary
Step '6. XONG - gui thong tin duoi day cho Claude'
$ips = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' }).IPAddress -join ', '
$fp = & "$env:SystemRoot\System32\OpenSSH\ssh-keygen.exe" -lf (Join-Path $env:ProgramData 'ssh\ssh_host_ed25519_key.pub')
$summary = @"
TODOX DEPLOY SERVER
  Server name : $env:COMPUTERNAME
  IP noi bo   : $ips
  SSH port    : $port
  SSH user    : $env:USERNAME
  Host key    : $fp
  Targets     :
$(($targets.GetEnumerator() | ForEach-Object { "    - $($_.Key): site=$($_.Value.SiteName) | folder=$($_.Value.SitePath) | pool=$($_.Value.AppPool) | health=$($_.Value.HealthUrl)" }) -join "`r`n")
"@
Write-Host $summary -ForegroundColor Green
$summaryFile = Join-Path $DeployRoot 'server-info.txt'
Set-Content -Path $summaryFile -Value $summary -Encoding UTF8
Write-Host "Da luu vao $summaryFile. Them IP public / ten mien cua server neu IP tren la IP noi bo."
