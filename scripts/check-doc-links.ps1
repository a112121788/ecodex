<#
.SYNOPSIS
  Validate Markdown relative links in ECodex docs and specs.

.DESCRIPTION
  Scans Markdown files under the provided paths and reports relative links whose
  target file or directory does not exist. External URLs, mailto links and pure
  anchors are ignored. Defaults to README.md, spec/ and md/.

.PARAMETER Path
  Files or directories to scan, relative to the repository root unless absolute.

.EXAMPLE
  pwsh ./scripts/check-doc-links.ps1
  pwsh ./scripts/check-doc-links.ps1 -Path spec,md,README.md
#>

[CmdletBinding()]
param(
    [string[]]$Path = @('README.md', 'spec', 'md')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir '..')

function Resolve-ScanPath {
    param([Parameter(Mandatory)][string]$InputPath)

    if ([System.IO.Path]::IsPathRooted($InputPath)) {
        return $InputPath
    }

    return Join-Path $RepoRoot $InputPath
}

function Get-MarkdownFiles {
    param([Parameter(Mandatory)][string[]]$InputPaths)

    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    foreach ($inputPath in $InputPaths) {
        $resolved = Resolve-ScanPath $inputPath
        if (Test-Path -LiteralPath $resolved -PathType Leaf) {
            $item = Get-Item -LiteralPath $resolved
            if ($item.Extension -ieq '.md') {
                $files.Add($item)
            }
            continue
        }

        if (Test-Path -LiteralPath $resolved -PathType Container) {
            Get-ChildItem -LiteralPath $resolved -Filter '*.md' -File -Recurse |
                ForEach-Object { $files.Add($_) }
        }
    }

    return $files
}

function Normalize-MarkdownTarget {
    param([Parameter(Mandatory)][string]$Target)

    $value = $Target.Trim()
    if ([string]::IsNullOrWhiteSpace($value) -or $value.StartsWith('#')) {
        return $null
    }

    if ($value.StartsWith('<') -and $value.EndsWith('>')) {
        $value = $value.Substring(1, $value.Length - 2).Trim()
    }

    if ($value -match '^[a-z][a-z0-9+.-]*:') {
        return $null
    }

    $pathOnly = ($value -split '#', 2)[0]
    $pathOnly = ($pathOnly -split '\?', 2)[0]
    if ([string]::IsNullOrWhiteSpace($pathOnly)) {
        return $null
    }

    return [Uri]::UnescapeDataString($pathOnly)
}

function Test-MarkdownRelativeLinks {
    param(
        [Parameter(Mandatory)][System.IO.FileInfo[]]$MarkdownFiles,
        [Parameter(Mandatory)][string]$Root
    )

    $missing = New-Object System.Collections.Generic.List[string]
    $linkPattern = '!?\[[^\]]*\]\((?<target>[^)]+)\)'

    foreach ($file in $MarkdownFiles) {
        $inFence = $false
        $lineNumber = 0
        foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
            $lineNumber++
            if ($line.TrimStart().StartsWith('```')) {
                $inFence = -not $inFence
                continue
            }

            if ($inFence) {
                continue
            }

            foreach ($match in [regex]::Matches($line, $linkPattern)) {
                $target = $match.Groups['target'].Value
                $pathOnly = Normalize-MarkdownTarget $target
                if ($null -eq $pathOnly) {
                    continue
                }

                $baseDir = Split-Path -Parent $file.FullName
                if ($pathOnly.StartsWith('/')) {
                    $resolved = Join-Path $Root $pathOnly.TrimStart('/', '\')
                } else {
                    $resolved = Join-Path $baseDir $pathOnly
                }

                if (-not (Test-Path -LiteralPath $resolved)) {
                    $relativeFile = [System.IO.Path]::GetRelativePath($Root, $file.FullName)
                    $missing.Add("${relativeFile}:${lineNumber}: missing link target '$target'")
                }
            }
        }
    }

    if ($missing.Count -gt 0) {
        throw "Markdown relative link check failed:`n$($missing -join "`n")"
    }

    Write-Host "Markdown relative links OK ($($MarkdownFiles.Count) files)." -ForegroundColor Green
}

$markdownFiles = @(Get-MarkdownFiles $Path)
if ($markdownFiles.Count -eq 0) {
    Write-Host 'No Markdown files found.' -ForegroundColor DarkYellow
    exit 0
}

Test-MarkdownRelativeLinks -MarkdownFiles $markdownFiles -Root $RepoRoot
