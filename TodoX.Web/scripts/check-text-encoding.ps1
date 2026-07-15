param(
    [string[]]$Path = @()
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)
$extensions = @('.razor', '.cs', '.json', '.md')
$excludedDirs = @('\bin\', '\obj\', '\publish\', '\node_modules\', '\.git\')

$patterns = @(
    [string]([char]0xFFFD),
    [string]([char]0x00C3),
    [string]([char]0x00C4),
    [string]([char]0x00C6),
    [string]([char]0x00D0),
    'Qu?n',
    'm?u',
    'Chua c?',
    '?nh k?t qu?',
    'Nh?t k',
    'T?o avatar',
    'T?i ?nh',
    'L?i ',
    'luu ',
    'th? b?ng'
)

$knownAdminAvatarBadStrings = @(
    'Qu?n l',
    'avatar m?u',
    'Chua c?u',
    '?nh k?t qu?',
    'Nh?t k',
    'T?i ?nh preview'
)

function Should-Skip([string]$fullName) {
    $normalized = $fullName.Replace('/', '\')
    foreach ($dir in $excludedDirs) {
        if ($normalized.IndexOf($dir, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }
    return $false
}

function Get-TargetFiles {
    if ($Path.Count -gt 0) {
        foreach ($item in $Path) {
            $resolved = Resolve-Path -LiteralPath $item
            foreach ($entry in $resolved) {
                if ([System.IO.Directory]::Exists($entry.Path)) {
                    Get-ChildItem -LiteralPath $entry.Path -Recurse -File |
                        Where-Object { $extensions -contains $_.Extension -and -not (Should-Skip $_.FullName) }
                }
                elseif ($extensions -contains [System.IO.Path]::GetExtension($entry.Path)) {
                    Get-Item -LiteralPath $entry.Path
                }
            }
        }
        return
    }

    Get-ChildItem -LiteralPath $repoRoot -Recurse -File |
        Where-Object { $extensions -contains $_.Extension -and -not (Should-Skip $_.FullName) }
}

$findings = New-Object System.Collections.Generic.List[string]

foreach ($file in Get-TargetFiles) {
    try {
        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        $text = $utf8Strict.GetString($bytes)
    }
    catch {
        $findings.Add("$($file.FullName):0:INVALID_UTF8:$($_.Exception.Message)")
        continue
    }

    $lines = $text -split "`r?`n"
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        foreach ($pattern in $patterns) {
            if ($line.IndexOf([string]$pattern, [StringComparison]::Ordinal) -ge 0) {
                $findings.Add("$($file.FullName):$($i + 1):${pattern}:$line")
            }
        }

        if ($file.Name -eq 'AdminAvatarManager.razor') {
            foreach ($pattern in $knownAdminAvatarBadStrings) {
                if ($line.IndexOf($pattern, [StringComparison]::Ordinal) -ge 0) {
                    $findings.Add("$($file.FullName):$($i + 1):${pattern}:$line")
                }
            }
        }
    }
}

if ($findings.Count -gt 0) {
    $findings | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

Write-Host "Encoding check passed."
