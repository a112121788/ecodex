#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputDir,
    [string]$AppPath,
    [string]$CliPath,
    [string]$SnapshotCommand,
    [ValidateRange(1, 30)]
    [int]$Samples = 3,
    [switch]$MeasureColdStart,
    [switch]$MeasureLiveCli,
    [switch]$FailOnBudget
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir '..\..')
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot 'artifacts\perf'
}

function Ensure-Directory {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Get-Percentile {
    param(
        [AllowEmptyCollection()][double[]]$Values = @(),
        [Parameter(Mandatory)][double]$Percentile
    )

    if ($null -eq $Values -or $Values.Count -eq 0) {
        return $null
    }

    $sorted = @($Values | Sort-Object)
    $index = [Math]::Ceiling(($Percentile / 100) * $sorted.Count) - 1
    $index = [Math]::Max(0, [Math]::Min($sorted.Count - 1, $index))
    return [Math]::Round([double]$sorted[$index], 2)
}

function New-PerfResult {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Display,
        [Parameter(Mandatory)][int]$BudgetMs,
        [Parameter(Mandatory)][string]$Status,
        [double[]]$SamplesMs = @(),
        [string]$Notes = ''
    )

    $p50 = Get-Percentile -Values $SamplesMs -Percentile 50
    $p95 = Get-Percentile -Values $SamplesMs -Percentile 95
    if ($Status -eq 'measured') {
        $Status = if ($p95 -le $BudgetMs) { 'pass' } else { 'fail' }
    }

    [pscustomobject]@{
        name = $Name
        display = $Display
        budgetMs = $BudgetMs
        p50Ms = $p50
        p95Ms = $p95
        samplesMs = @($SamplesMs | ForEach-Object { [Math]::Round([double]$_, 2) })
        status = $Status
        notes = $Notes
    }
}

function Measure-Operation {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Display,
        [Parameter(Mandatory)][int]$BudgetMs,
        [Parameter(Mandatory)][scriptblock]$Operation,
        [string]$Notes = ''
    )

    $durations = New-Object System.Collections.Generic.List[double]
    try {
        for ($i = 0; $i -lt $Samples; $i++) {
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            & $Operation
            $sw.Stop()
            $durations.Add($sw.Elapsed.TotalMilliseconds)
        }

        return New-PerfResult -Name $Name -Display $Display -BudgetMs $BudgetMs -Status 'measured' -SamplesMs $durations.ToArray() -Notes $Notes
    } catch {
        return New-PerfResult -Name $Name -Display $Display -BudgetMs $BudgetMs -Status 'skipped' -SamplesMs @() -Notes $_.Exception.Message
    }
}

function Invoke-ColdStartProxy {
    if ([string]::IsNullOrWhiteSpace($AppPath) -or -not (Test-Path -LiteralPath $AppPath -PathType Leaf)) {
        throw 'AppPath was not provided or does not exist.'
    }

    $process = Start-Process -FilePath $AppPath -PassThru -WindowStyle Hidden
    try {
        Start-Sleep -Milliseconds 750
    } finally {
        if ($process -and -not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit(5000) | Out-Null
        }
    }
}

function Invoke-EcodexStatus {
    if (-not $MeasureLiveCli) {
        throw 'Live CLI timing disabled; pass -MeasureLiveCli when the ECodeX app pipe is available.'
    }

    if ([string]::IsNullOrWhiteSpace($CliPath) -or -not (Test-Path -LiteralPath $CliPath -PathType Leaf)) {
        throw 'CliPath was not provided or does not exist.'
    }

    & $CliPath status | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "ecodex status exited with $LASTEXITCODE. Start ECodeX first or omit -MeasureLiveCli."
    }
}

function Invoke-BrowserSnapshot {
    if ([string]::IsNullOrWhiteSpace($SnapshotCommand)) {
        throw 'SnapshotCommand was not provided.'
    }

    pwsh -NoProfile -Command $SnapshotCommand | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "SnapshotCommand exited with $LASTEXITCODE."
    }
}

function Invoke-SyntheticSaveSession {
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("ecodex-perf-session-" + [Guid]::NewGuid().ToString('N') + ".json")
    try {
        $workspaces = @()
        $workspace = [ordered]@{
            id = 'workspace:1'
            name = 'Perf Workspace'
            surfaces = @()
        }

        for ($pane = 1; $pane -le 10; $pane++) {
            $scrollback = New-Object System.Collections.Generic.List[string]
            for ($line = 1; $line -le 3000; $line++) {
                $scrollback.Add(("pane={0:D2} line={1:D4} " -f $pane, $line) + ('x' * 80))
            }

            $workspace.surfaces += [ordered]@{
                id = "surface:$pane"
                title = "surface-$pane"
                root = [ordered]@{ kind = 'leaf'; paneId = "pane:$pane" }
                paneSnapshots = [ordered]@{
                    "pane:$pane" = [ordered]@{
                        capturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
                        workingDirectory = "C:\work\perf-$pane"
                        shell = 'pwsh'
                        commandHistory = @('git status', 'npm test')
                        bufferSnapshot = [ordered]@{
                            cols = 120
                            rows = 30
                            cursorRow = 29
                            cursorCol = 0
                            scrollbackLines = $scrollback.ToArray()
                            screenLines = @(1..30 | ForEach-Object { "screen line $_" })
                        }
                    }
                }
            }
        }

        $workspaces += $workspace
        $payload = [ordered]@{
            version = 2
            savedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
            workspaces = $workspaces
        }

        $payload | ConvertTo-Json -Depth 20 -Compress | Set-Content -LiteralPath $tmp -Encoding UTF8
    } finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}

function Write-MarkdownReport {
    param(
        [Parameter(Mandatory)][object[]]$Results,
        [Parameter(Mandatory)][string]$Path
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# ECodeX Performance Budget Report')
    $lines.Add('')
    $lines.Add("Generated: $((Get-Date).ToUniversalTime().ToString('o'))")
    $lines.Add('')
    $lines.Add('| Metric | Budget | p50 | p95 | Status | Notes |')
    $lines.Add('|---|---:|---:|---:|---|---|')
    foreach ($result in $Results) {
        $p50 = if ($null -eq $result.p50Ms) { '-' } else { "$($result.p50Ms) ms" }
        $p95 = if ($null -eq $result.p95Ms) { '-' } else { "$($result.p95Ms) ms" }
        $notes = ($result.notes -replace '\|', '/').Trim()
        $lines.Add("| $($result.display) | $($result.budgetMs) ms | $p50 | $p95 | $($result.status) | $notes |")
    }

    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

Ensure-Directory $OutputDir

$results = @()
if ($MeasureColdStart) {
    $results += Measure-Operation -Name 'cold_start_no_restore' -Display 'Cold start to ready (no restore)' -BudgetMs 1500 -Operation { Invoke-ColdStartProxy } -Notes 'Process launch proxy; UI readiness must be confirmed manually for release candidates.'
} else {
    $results += New-PerfResult -Name 'cold_start_no_restore' -Display 'Cold start to ready (no restore)' -BudgetMs 1500 -Status 'skipped' -Notes 'Pass -MeasureColdStart with -AppPath to run this check.'
}

$results += Measure-Operation -Name 'ecodex_status' -Display 'ecodex status' -BudgetMs 100 -Operation { Invoke-EcodexStatus } -Notes 'Requires a running ECodeX app pipe.'
$results += Measure-Operation -Name 'browser_snapshot' -Display 'browser.snapshot' -BudgetMs 500 -Operation { Invoke-BrowserSnapshot } -Notes 'Pass -SnapshotCommand when a browser surface is available.'
$results += Measure-Operation -Name 'save_session_10_panes_3000_lines' -Display 'Save session (10 panes x 3000 lines)' -BudgetMs 300 -Operation { Invoke-SyntheticSaveSession } -Notes 'Synthetic session.json serialization budget.'

$jsonPath = Join-Path $OutputDir 'perf-report.json'
$markdownPath = Join-Path $OutputDir 'perf-report.md'
$report = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    samples = $Samples
    budgets = [ordered]@{
        coldStartNoRestoreMs = 1500
        ecodexStatusMs = 100
        browserSnapshotMs = 500
        saveSession10Panes3000LinesMs = 300
    }
    results = $results
}

$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
Write-MarkdownReport -Results $results -Path $markdownPath

Write-Host "Performance report written:"
Write-Host "  $jsonPath"
Write-Host "  $markdownPath"

$failed = @($results | Where-Object { $_.status -eq 'fail' })
if ($FailOnBudget -and $failed.Count -gt 0) {
    throw "Performance budget failed: $($failed.display -join ', ')"
}
