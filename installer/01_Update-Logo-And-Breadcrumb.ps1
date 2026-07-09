param(
    [string]$RepoRoot = "D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Join-Path $RepoRoot "TodoX.Web"
$LayoutFile = Join-Path $ProjectRoot "Components\Layout\MainLayout.razor"
$CssFile = Join-Path $ProjectRoot "wwwroot\css\app.css"

if (-not (Test-Path $LayoutFile)) {
    throw "Không tìm thấy MainLayout.razor: $LayoutFile"
}

if (-not (Test-Path $CssFile)) {
    throw "Không tìm thấy app.css: $CssFile"
}

Write-Host "TodoX Logo + Breadcrumb Fix" -ForegroundColor Yellow
Write-Host "RepoRoot: $RepoRoot" -ForegroundColor Cyan

$backupDir = Join-Path $RepoRoot ("backup\logo-breadcrumb-fix-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

Copy-Item $LayoutFile (Join-Path $backupDir "MainLayout.razor.bak") -Force
Copy-Item $CssFile (Join-Path $backupDir "app.css.bak") -Force

$layout = Get-Content $LayoutFile -Raw

# Replace common image logo tags with TodoX wordmark.
$layout = [regex]::Replace(
    $layout,
    '<img\s+[^>]*?(logo|TodoX)[^>]*?>',
    '<span class="todox-wordmark">Todo<span>X</span></span>',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
)

# If no wordmark exists after replacement, inject one inside first MudDrawer.
if ($layout -notmatch 'todox-wordmark') {
    $layout = [regex]::Replace(
        $layout,
        '(<MudDrawer[\s\S]*?>)',
        '$1' + "`r`n" + '    <div class="todox-brand"><span class="todox-wordmark">Todo<span>X</span></span></div>',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )
}

# Remove common logo/image wrapper if left over.
$layout = [regex]::Replace(
    $layout,
    '<div\s+class="[^"]*(brand-image|logo-image|sidebar-logo-image)[^"]*"[\s\S]*?</div>',
    '',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
)

# Add spacing class to MudMainContent.
$layout = [regex]::Replace(
    $layout,
    '<MudMainContent\s+Class="([^"]*)"',
    '<MudMainContent Class="$1 todox-main-spacing"',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
)

if ($layout -notmatch 'todox-main-spacing') {
    $layout = [regex]::Replace(
        $layout,
        '(<MudMainContent[\s\S]*?>)',
        '$1' + "`r`n" + '    <div class="todox-main-spacing-marker"></div>',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )
}

Set-Content -Path $LayoutFile -Value $layout -Encoding UTF8

$cssAppend = @"

/* =========================================================
   TodoX Sprint 2B - Logo text + breadcrumb spacing fix
   ========================================================= */

.todox-brand,
.sidebar-brand,
.brand-area,
.logo-area {
    display: flex;
    align-items: center;
    min-height: 76px;
    padding: 20px 24px;
}

.todox-wordmark {
    display: inline-flex;
    align-items: center;
    color: #ffffff;
    font-size: 34px;
    font-weight: 900;
    letter-spacing: -1.2px;
    line-height: 1;
    text-decoration: none;
    white-space: nowrap;
}

.todox-wordmark span {
    color: #ffb700;
}

.todox-main-spacing {
    padding-top: 92px !important;
}

.todox-main-spacing-marker {
    height: 72px;
}

.todox-breadcrumb,
.breadcrumb,
.breadcrumb-bar,
.page-breadcrumb {
    margin-top: 8px;
    margin-bottom: 20px;
    position: relative;
    z-index: 1;
}

.todox-page-header,
.page-header,
.content-header {
    margin-top: 6px;
}

@media (max-width: 960px) {
    .todox-wordmark {
        font-size: 28px;
    }

    .todox-brand,
    .sidebar-brand,
    .brand-area,
    .logo-area {
        min-height: 64px;
        padding: 16px 20px;
    }

    .todox-main-spacing {
        padding-top: 78px !important;
    }

    .todox-main-spacing-marker {
        height: 60px;
    }
}

@media (max-width: 600px) {
    .todox-wordmark {
        font-size: 24px;
    }

    .todox-main-spacing {
        padding-top: 72px !important;
    }

    .todox-main-spacing-marker {
        height: 54px;
    }
}
"@

$css = Get-Content $CssFile -Raw
if ($css -notmatch 'TodoX Sprint 2B - Logo text') {
    Add-Content -Path $CssFile -Value $cssAppend -Encoding UTF8
}

Write-Host "Done." -ForegroundColor Green
Write-Host "Backup folder: $backupDir" -ForegroundColor Cyan
Write-Host "Bây giờ chạy: dotnet build .\TodoX.Web\TodoX.Web.csproj" -ForegroundColor Yellow