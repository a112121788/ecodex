<#
.SYNOPSIS
  Runs the local ECode CI checks.

.DESCRIPTION
  This script is the single local entrypoint for the M0 engineering baseline.
  By default it restores, builds, runs unit tests, validates PowerShell scripts,
  and performs dry-run checks for Windows-only smoke/publish steps.

.PARAMETER Config
  Build configuration. Defaults to Debug.

.PARAMETER Rid
  Runtime identifier used by optional publish checks. Defaults to win-x64.

.PARAMETER IncludeSmoke
  Run the ConPTY smoke test. This requires Windows and is skipped otherwise.

.PARAMETER IncludeBrowserIntegration
  Run browser scripting integration tests. This requires Windows/WebView2 and is skipped otherwise.

.PARAMETER IncludePublish
  Run scripts/publish.ps1 for the selected publish flavor instead of only
  validating the publish script surface.

.PARAMETER PublishFlavor
  Publish flavor passed to scripts/publish.ps1 when IncludePublish is set.

.PARAMETER SkipRestore
  Skip dotnet restore.

.EXAMPLE
  pwsh ./scripts/ci.ps1
  pwsh ./scripts/ci.ps1 -Config Release
  pwsh ./scripts/ci.ps1 -IncludeSmoke
  pwsh ./scripts/ci.ps1 -IncludePublish -PublishFlavor Cli
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Config = 'Debug',

    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$Rid = 'win-x64',

    [switch]$IncludeSmoke,

    [switch]$IncludeBrowserIntegration,

    [switch]$IncludePublish,

    [ValidateSet('All', 'Framework', 'SelfContained', 'Cli', 'Velopack', 'MSIX')]
    [string]$PublishFlavor = 'Cli',

    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir '..')
$Solution = Join-Path $RepoRoot 'ECode.sln'
$UnitTestProject = Join-Path $RepoRoot 'tests/ECode.Tests/ECode.Tests.csproj'
$SmokeProject = Join-Path $RepoRoot 'tests/ECode.Smoke/ECode.Smoke.csproj'
$PublishScript = Join-Path $RepoRoot 'scripts/publish.ps1'

$StepCount = 0
$StartedAt = Get-Date

function Write-Step {
    param([Parameter(Mandatory)][string]$Title)
    $script:StepCount++
    Write-Host ""
    Write-Host ("[{0}] {1}" -f $script:StepCount, $Title) -ForegroundColor Cyan
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    Write-Host (">> {0} {1}" -f $FilePath, ($Arguments -join ' ')) -ForegroundColor DarkGray
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Test-IsWindows {
    if ($PSVersionTable.PSEdition -eq 'Desktop') { return $true }
    return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)
}

function Test-PowerShellScriptSyntax {
    param([Parameter(Mandatory)][string]$Path)

    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -gt 0) {
        $messages = $errors | ForEach-Object { "{0}:{1}: {2}" -f $_.Extent.StartLineNumber, $_.Extent.StartColumnNumber, $_.Message }
        throw "PowerShell syntax errors in ${Path}:`n$($messages -join "`n")"
    }
}

function Test-MarkdownRelativeLinks {
    param([Parameter(Mandatory)][string]$Root)

    $candidatePaths = @(
        (Join-Path $Root 'README.md'),
        (Join-Path $Root 'spec'),
        (Join-Path $Root 'docs')
    )

    $markdownFiles = @()
    foreach ($path in $candidatePaths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $markdownFiles += Get-Item -LiteralPath $path
        } elseif (Test-Path -LiteralPath $path -PathType Container) {
            $markdownFiles += Get-ChildItem -LiteralPath $path -Filter '*.md' -File -Recurse
        }
    }

    $missing = New-Object System.Collections.Generic.List[string]
    $linkPattern = '(!?\[[^\]]*\]\((?<target>[^)]+)\))'

    foreach ($file in $markdownFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($match in [regex]::Matches($content, $linkPattern)) {
            $target = $match.Groups['target'].Value.Trim()
            if ([string]::IsNullOrWhiteSpace($target) -or $target.StartsWith('#')) {
                continue
            }

            if ($target.StartsWith('<') -and $target.EndsWith('>')) {
                $target = $target.Substring(1, $target.Length - 2)
            }

            if ($target -match '^[a-z][a-z0-9+.-]*:') {
                continue
            }

            $pathOnly = ($target -split '#', 2)[0]
            $pathOnly = ($pathOnly -split '\?', 2)[0]
            $pathOnly = ($pathOnly -split '\s+', 2)[0]
            if ([string]::IsNullOrWhiteSpace($pathOnly)) {
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
                $missing.Add("${relativeFile}: missing link target '$target'")
            }
        }
    }

    if ($missing.Count -gt 0) {
        throw "Markdown relative link check failed:`n$($missing -join "`n")"
    }

    Write-Host "Markdown relative links OK ($($markdownFiles.Count) files)." -ForegroundColor Green
}

Push-Location $RepoRoot
try {
    Write-Host "=== ECode local CI === Config=$Config Rid=$Rid PublishFlavor=$PublishFlavor Time=$($StartedAt.ToString('yyyy-MM-dd HH:mm:ss')) ===" -ForegroundColor Yellow

    Write-Step 'Toolchain check'
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw 'dotnet was not found on PATH. Install the .NET 10 SDK before running CI.'
    }
    Invoke-Checked $dotnet.Source @('--info')

    if (-not $SkipRestore) {
        Write-Step 'Restore dependencies'
        Invoke-Checked $dotnet.Source @('restore', $Solution)
    }

    Write-Step 'Build solution'
    $buildArgs = @('build', $Solution, '--configuration', $Config, '--no-restore')
    Invoke-Checked $dotnet.Source $buildArgs

    Write-Step 'Run unit tests'
    $testArgs = @('test', $UnitTestProject, '--configuration', $Config, '--no-build', '--verbosity', 'normal', '--filter', 'Category!=WindowsOnlyIntegration')
    Invoke-Checked $dotnet.Source $testArgs

    Write-Step 'Browser integration gate'
    if ($IncludeBrowserIntegration) {
        if (-not (Test-IsWindows)) {
            throw 'Browser scripting integration requires Windows/WebView2. Re-run on Windows or omit -IncludeBrowserIntegration.'
        }
        Invoke-Checked $dotnet.Source @('test', $UnitTestProject, '--configuration', $Config, '--no-build', '--verbosity', 'normal', '--filter', 'Category=WindowsOnlyIntegration')
    } else {
        Write-Host 'Dry-run only: use -IncludeBrowserIntegration on Windows/WebView2 to run browser scripting integration.' -ForegroundColor DarkYellow
    }

    Write-Step 'Validate PowerShell scripts'
    Test-PowerShellScriptSyntax (Join-Path $RepoRoot 'scripts/ci.ps1')
    Test-PowerShellScriptSyntax $PublishScript
    Write-Host 'PowerShell syntax OK.' -ForegroundColor Green

    Write-Step 'Validate documentation links'
    Test-MarkdownRelativeLinks $RepoRoot

    Write-Step 'Smoke test gate'
    if ($IncludeSmoke) {
        if (-not (Test-IsWindows)) {
            throw 'ConPTY smoke test requires Windows. Re-run on Windows or omit -IncludeSmoke.'
        }
        Invoke-Checked $dotnet.Source @('run', '--project', $SmokeProject, '--configuration', $Config, '--no-build')
    } else {
        Write-Host 'Dry-run only: use -IncludeSmoke on Windows to run tests/ECode.Smoke.' -ForegroundColor DarkYellow
    }

    Write-Step 'Publish gate'
    if ($IncludePublish) {
        $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
        if (-not $pwsh) {
            throw 'pwsh was not found on PATH. Install PowerShell 7 before running publish checks.'
        }
        Invoke-Checked $pwsh.Source @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PublishScript, '-Config', 'Release', '-Rid', $Rid, '-Flavor', $PublishFlavor)
    } else {
        Write-Host "Dry-run only: publish.ps1 syntax and parameter surface validated. Use -IncludePublish to publish $PublishFlavor." -ForegroundColor DarkYellow
    }

    $elapsed = (Get-Date) - $StartedAt
    Write-Host ""
    Write-Host ("=== local CI passed in {0:n1}s ===" -f $elapsed.TotalSeconds) -ForegroundColor Green
} finally {
    Pop-Location
}
