
$ErrorActionPreference = "Stop"

$AgentRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigPath = Join-Path $AgentRoot "config.json"

if (-not (Test-Path $ConfigPath)) {
    throw "config.json not found: $ConfigPath"
}

$Config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$RepoRoot = $Config.RepoRoot
$LogDir = Join-Path $AgentRoot "logs"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$LogFile = Join-Path $LogDir ("deploy-agent-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

function Log($message, $level = "INFO") {
    $line = "[{0}] [{1}] {2}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $level, $message
    Write-Host $line
    Add-Content -Path $LogFile -Value $line
}

function Is-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Run-Cmd($file, $arguments, $throwOnError = $true) {
    Log "RUN: $file $arguments"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $file
    $psi.Arguments = $arguments
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi
    [void]$p.Start()
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit()

    if ($stdout) { $stdout -split "`r?`n" | Where-Object { $_ } | ForEach-Object { Log $_ "OUT" } }
    if ($stderr) { $stderr -split "`r?`n" | Where-Object { $_ } | ForEach-Object { Log $_ "ERR" } }

    Log "EXITCODE: $($p.ExitCode)"
    if ($throwOnError -and $p.ExitCode -ne 0) {
        throw "Command failed: $file $arguments"
    }
    return $p.ExitCode
}

function Get-Paths {
    $project = Join-Path $RepoRoot $Config.ProjectPath
    $publish = Join-Path $RepoRoot $Config.PublishPath
    $legacy = Join-Path $RepoRoot $Config.LegacyArtifactPath
    $appcmd = Join-Path $env:windir "System32\inetsrv\appcmd.exe"
    return @{
        Project = $project
        Publish = $publish
        Legacy = $legacy
        AppCmd = $appcmd
        Ancm = "C:\Program Files\IIS\Asp.Net Core Module\V2"
    }
}

function Check-System {
    Log "===== CHECK SYSTEM START ====="
    Log "Admin: $(Is-Admin)"
    Log "RepoRoot: $RepoRoot"
    Log "AgentRoot: $AgentRoot"
    Log "LogFile: $LogFile"

    $p = Get-Paths
    Log "ProjectPath: $($p.Project) exists=$(Test-Path $p.Project)"
    Log "PublishPath: $($p.Publish) exists=$(Test-Path $p.Publish)"
    Log "LegacyArtifactPath: $($p.Legacy) exists=$(Test-Path $p.Legacy)"
    Log "appcmd.exe: $($p.AppCmd) exists=$(Test-Path $p.AppCmd)"
    Log "ANCM V2: $($p.Ancm) exists=$(Test-Path $p.Ancm)"

    Log "dotnet --list-runtimes"
    Run-Cmd "dotnet" "--list-runtimes" $false | Out-Null

    Log "dotnet --list-sdks"
    Run-Cmd "dotnet" "--list-sdks" $false | Out-Null

    if (Test-Path $p.AppCmd) {
        Log "IIS sites:"
        Run-Cmd $p.AppCmd "list site" $false | Out-Null
        Log "IIS vdir:"
        Run-Cmd $p.AppCmd "list vdir `"$($Config.IisSiteName)/`" /text:*" $false | Out-Null
        Log "IIS apppool:"
        Run-Cmd $p.AppCmd "list apppool" $false | Out-Null
    }

    if (Test-Path (Join-Path $p.Publish "web.config")) {
        Log "web.config:"
        Get-Content (Join-Path $p.Publish "web.config") | ForEach-Object { Log $_ "WEB.CONFIG" }
    } else {
        Log "web.config missing in publish folder" "WARN"
    }

    Log "===== CHECK SYSTEM END ====="
}

function Install-IIS-Features {
    Log "Installing IIS required features..."
    $features = @(
        "IIS-WebServerRole",
        "IIS-WebServer",
        "IIS-CommonHttpFeatures",
        "IIS-DefaultDocument",
        "IIS-StaticContent",
        "IIS-HttpErrors",
        "IIS-HttpLogging",
        "IIS-RequestFiltering",
        "IIS-ManagementConsole",
        "IIS-ManagementScriptingTools",
        "IIS-IIS6ManagementCompatibility",
        "IIS-Metabase"
    )

    foreach ($f in $features) {
        Log "Enable feature: $f"
        Run-Cmd "dism.exe" "/online /enable-feature /featurename:$f /all /norestart" $false | Out-Null
    }
}

function Install-HostingBundle-IfMissing {
    $p = Get-Paths
    if (Test-Path $p.Ancm) {
        Log "ANCM V2 already installed. Skip Hosting Bundle."
        return
    }

    $url = "https://download.visualstudio.microsoft.com/download/pr/8b379f2b-9865-4c40-8f8f-7e5a0e4ec1f9/01626607f836f1720ced2de93150d6a8/dotnet-hosting-10.0.9-win.exe"
    $download = Join-Path $env:TEMP "dotnet-hosting-10.0.9-win.exe"

    Log "Downloading .NET 10 Hosting Bundle..."
    Log "URL: $url"
    try {
        Invoke-WebRequest -Uri $url -OutFile $download -UseBasicParsing
    } catch {
        Log "Automatic download failed: $($_.Exception.Message)" "ERROR"
        Log "Please install .NET 10 Hosting Bundle manually, then run this agent again." "ERROR"
        throw
    }

    if (-not (Test-Path $download)) {
        throw "Hosting Bundle download failed: $download"
    }

    Log "Installing Hosting Bundle silently..."
    Run-Cmd $download "/install /quiet /norestart" $true | Out-Null

    Start-Sleep -Seconds 5

    if (Test-Path $p.Ancm) {
        Log "Hosting Bundle installed successfully."
    } else {
        Log "ANCM still missing after install. A machine restart may be required." "WARN"
    }
}

function Clean-And-Publish {
    $p = Get-Paths
    if (-not (Test-Path $p.Project)) {
        throw "Project file not found: $($p.Project)"
    }

    Log "Removing legacy artifacts folder..."
    if (Test-Path $p.Legacy) {
        Remove-Item $p.Legacy -Recurse -Force
        Log "Deleted: $($p.Legacy)"
    } else {
        Log "Legacy folder not found, skip."
    }

    Log "Removing publish folder..."
    if (Test-Path $p.Publish) {
        Remove-Item $p.Publish -Recurse -Force
        Log "Deleted: $($p.Publish)"
    }
    New-Item -ItemType Directory -Force -Path $p.Publish | Out-Null

    Log "dotnet clean"
    Run-Cmd "dotnet" "clean `"$($p.Project)`"" $true | Out-Null

    Log "dotnet publish"
    Run-Cmd "dotnet" "publish `"$($p.Project)`" -c Release -o `"$($p.Publish)`"" $true | Out-Null

    $required = @("web.config","TodoX.Web.dll","TodoX.Web.exe","TodoX.Web.deps.json","TodoX.Web.runtimeconfig.json","wwwroot")
    foreach ($r in $required) {
        $full = Join-Path $p.Publish $r
        if (-not (Test-Path $full)) {
            throw "Missing publish output: $full"
        }
        Log "Publish OK: $r"
    }

    if (Test-Path (Join-Path $p.Publish "publish")) {
        Log "WARNING: Nested publish folder found. Removing it." "WARN"
        Remove-Item (Join-Path $p.Publish "publish") -Recurse -Force
    }
}

function Deploy-IIS {
    $p = Get-Paths
    if (-not (Test-Path $p.AppCmd)) {
        throw "appcmd.exe not found. IIS Management Tools missing."
    }

    if (-not (Test-Path $p.Publish)) {
        throw "Publish folder not found: $($p.Publish)"
    }

    Log "Stopping IIS site..."
    Run-Cmd $p.AppCmd "stop site /site.name:`"$($Config.IisSiteName)`"" $false | Out-Null

    Log "Setting IIS physical path..."
    Run-Cmd $p.AppCmd "set vdir `"$($Config.IisSiteName)/`" /physicalPath:`"$($p.Publish)`"" $true | Out-Null

    Log "Reading vdir after update..."
    Run-Cmd $p.AppCmd "list vdir `"$($Config.IisSiteName)/`" /text:*" $false | Out-Null

    Log "Starting IIS site..."
    Run-Cmd $p.AppCmd "start site /site.name:`"$($Config.IisSiteName)`"" $false | Out-Null

    Log "iisreset"
    Run-Cmd "iisreset" "" $false | Out-Null
}

function Verify-Web {
    $url = "http://$($Config.LocalDomain)"
    Log "Verifying URL: $url"
    try {
        $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 15
        Log "HTTP Status: $($resp.StatusCode)"
        if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400) {
            Log "Website looks OK."
        } else {
            Log "Website returned non-success status." "WARN"
        }
    } catch {
        Log "Website verify failed: $($_.Exception.Message)" "ERROR"
    }
}

function Full-Deploy {
    Check-System
    Install-IIS-Features
    Install-HostingBundle-IfMissing
    Clean-And-Publish
    Deploy-IIS
    Verify-Web
    Check-System
}

function Show-Menu {
    Clear-Host
    Write-Host "========================================="
    Write-Host " TodoX Deploy Agent - Sprint 2B"
    Write-Host "========================================="
    Write-Host ""
    Write-Host "[1] Check system"
    Write-Host "[2] Install missing components"
    Write-Host "[3] Clean build and publish"
    Write-Host "[4] Deploy IIS path"
    Write-Host "[5] Full deploy"
    Write-Host "[6] Verify website"
    Write-Host "[7] Exit"
    Write-Host ""
}

try {
    Log "TodoX Deploy Agent started."
    Log "Admin: $(Is-Admin)"
    if (-not (Is-Admin)) {
        Log "WARNING: Not running as Administrator. Some actions will fail." "WARN"
    }

    do {
        Show-Menu
        $choice = Read-Host "Choose"
        switch ($choice) {
            "1" { Check-System; Read-Host "Press Enter to continue" }
            "2" { Install-IIS-Features; Install-HostingBundle-IfMissing; Read-Host "Press Enter to continue" }
            "3" { Clean-And-Publish; Read-Host "Press Enter to continue" }
            "4" { Deploy-IIS; Read-Host "Press Enter to continue" }
            "5" { Full-Deploy; Read-Host "Press Enter to continue" }
            "6" { Verify-Web; Read-Host "Press Enter to continue" }
            "7" { break }
            default { Write-Host "Invalid choice"; Start-Sleep -Seconds 1 }
        }
    } while ($choice -ne "7")

    Log "TodoX Deploy Agent finished. LogFile=$LogFile"
} catch {
    Log "FAILED: $($_.Exception.Message)" "ERROR"
    Log "Stack: $($_.ScriptStackTrace)" "ERROR"
    Write-Host ""
    Write-Host "FAILED. Please send this log file to ChatGPT:"
    Write-Host $LogFile
    Read-Host "Press Enter to exit"
}
