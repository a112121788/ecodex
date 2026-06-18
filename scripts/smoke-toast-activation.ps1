[CmdletBinding()]
param(
    [string] $CliPath = "",
    [string] $WorkspaceName = "smoke-toast-activation-$([DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss'))",
    [string] $WorkspaceRoot = "",
    [int] $WaitSeconds = 8,
    [ValidateSet("LifecycleToast", "CodexAttention")]
    [string] $Scenario = "LifecycleToast",
    [switch] $Interactive,
    [switch] $RequireActivationPrerequisites,
    [switch] $Cleanup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$TmpRoot = Join-Path $RepoRoot "tmp"
$ExpectedAppUserModelId = "ECodex.App"
if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = Join-Path $TmpRoot $WorkspaceName
}

$script:Checks = @()
$script:ManualSteps = @()
if ($Scenario -eq "CodexAttention") {
    $script:ManualSteps = @(
        "Keep this PowerShell window focused so ECodex is hidden or inactive before the Codex attention trigger is raised.",
        "Run this script with -Scenario CodexAttention; it writes a Codex-like waiting-input line and records evidence.agentAttentionPayload.",
        "Confirm a Windows Toast appears with title 'Codex 等待输入' and body containing the codex-attention marker.",
        "Click the Toast in the notification banner or Windows notification center.",
        "Expected: ECodex restores from tray/inactive state and focuses the workspace/surface/pane listed in evidence.toastPayload.",
        "Confirm evidence.negativeControl shows ordinary-output did not create an AgentAttention notification.",
        "For real Codex CLI sign-off, repeat with a real Codex approval / waiting-input prompt and paste the observed trigger text into manual notes.",
        "After recording evidence, run evidence.cleanup.command or rerun with -Interactive -Cleanup to remove the smoke workspace."
    )
}
else {
    $script:ManualSteps = @(
        "Keep this PowerShell window focused so ECodex is hidden or inactive before the toast is raised.",
        "Confirm a Windows Toast appears with title 'ECodex Toast Smoke' and body containing the smoke marker.",
        "Click the Toast in the notification banner or Windows notification center.",
        "Expected: ECodex restores from tray/inactive state and focuses the workspace/surface/pane listed in evidence.toastPayload.",
        "Expected fallback: if the target pane is gone before click, ECodex restores and opens the notification panel instead of jumping elsewhere.",
        "After recording evidence, run evidence.cleanup.command or rerun with -Interactive -Cleanup to remove the smoke workspace."
    )
}
$script:Evidence = [ordered]@{
    toastPayload = $null
    agentAttentionPayload = $null
    simulatedTriggerText = $null
    negativeControl = $null
    cleanup = $null
    manualEvidenceTemplate = [ordered]@{
        toastShown = $null
        clickRestoredWindow = $null
        paneFocused = $null
        fallbackVisible = $null
        sourceIsAgentAttention = $null
        ordinaryOutputDidNotNotify = $null
        notes = ""
    }
}
$script:PreparedManualSmoke = $false

function Add-SmokeCheck {
    param(
        [string] $Name,
        [ValidateSet("ok", "warn", "fail", "skip", "manual")]
        [string] $Status,
        [string] $Message,
        [object] $Data = $null
    )

    $check = [ordered]@{
        name = $Name
        status = $Status
        message = $Message
    }
    if ($null -ne $Data) {
        $check["data"] = $Data
    }

    $script:Checks += $check
}

function Write-SmokeSummary {
    param(
        [ValidateSet("passed", "manual", "skipped", "failed")]
        [string] $Status,
        [string] $Reason = ""
    )

    $summary = [ordered]@{
        name = "smoke-toast-activation"
        status = $Status
        reason = $Reason
        checks = $script:Checks
        manualSteps = $script:ManualSteps
        evidence = $script:Evidence
    }

    $summary | ConvertTo-Json -Depth 12
    if ($Status -eq "failed") {
        exit 1
    }

    exit 0
}

function Test-IsWindows {
    return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)
}

function Test-IsUnderDirectory {
    param(
        [string] $Path,
        [string] $Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    return $fullPath.Equals($fullRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Resolve-ECodexCli {
    if (-not [string]::IsNullOrWhiteSpace($CliPath)) {
        if (-not (Test-Path -LiteralPath $CliPath)) {
            throw "ECodex CLI was not found at CliPath: $CliPath"
        }

        return (Resolve-Path -LiteralPath $CliPath).Path
    }

    $repoCli = Join-Path $RepoRoot "src\ECodex.Cli\bin\Debug\net10.0-windows\ecodex.exe"
    if (Test-Path -LiteralPath $repoCli) {
        return (Resolve-Path -LiteralPath $repoCli).Path
    }

    $command = Get-Command ecodex -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    return $null
}

function Get-JsonProperty {
    param(
        [object] $Object,
        [string] $Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Invoke-ECodexJson {
    param([string[]] $Arguments)

    $output = & $script:ResolvedCli @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | Out-String).Trim()

    if ($exitCode -ne 0) {
        throw "ecodex exited with $exitCode for args: $($Arguments -join ' '). Output: $text"
    }

    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "ecodex returned empty output for args: $($Arguments -join ' ')"
    }

    try {
        return $text | ConvertFrom-Json
    }
    catch {
        throw "ecodex returned non-JSON output for args: $($Arguments -join ' '). Output: $text"
    }
}

function Assert-ECodexSuccess {
    param(
        [object] $Response,
        [string] $StepName
    )

    $errorValue = Get-JsonProperty $Response "error"
    if ($null -ne $errorValue) {
        $details = $errorValue | ConvertTo-Json -Compress
        throw "$StepName failed: $details"
    }

    $okValue = Get-JsonProperty $Response "ok"
    if ($null -ne $okValue -and $okValue -eq $false) {
        $details = $Response | ConvertTo-Json -Compress
        throw "$StepName failed: $details"
    }
}

function Get-ECodexResult {
    param([object] $Response)

    $result = Get-JsonProperty $Response "result"
    if ($null -ne $result) {
        return $result
    }

    return $Response
}

function Get-ShortcutInfo {
    param([string] $Path)

    $targetPath = ""
    $workingDirectory = ""
    $appUserModelId = $null

    try {
        $wsh = New-Object -ComObject WScript.Shell
        $shortcut = $wsh.CreateShortcut($Path)
        $targetPath = [string]$shortcut.TargetPath
        $workingDirectory = [string]$shortcut.WorkingDirectory
    }
    catch {
        Add-SmokeCheck "shortcut-read" "warn" "Could not read shortcut target for ${Path}: $($_.Exception.Message)"
    }

    try {
        $shell = New-Object -ComObject Shell.Application
        $folder = $shell.Namespace((Split-Path -Parent $Path))
        if ($null -ne $folder) {
            $item = $folder.ParseName((Split-Path -Leaf $Path))
            if ($null -ne $item) {
                $appUserModelId = $item.ExtendedProperty("System.AppUserModel.ID")
            }
        }
    }
    catch {
        $appUserModelId = $null
    }

    return [ordered]@{
        path = $Path
        targetPath = $targetPath
        workingDirectory = $workingDirectory
        appUserModelId = $appUserModelId
    }
}

function Get-ECodexShortcuts {
    $userPrograms = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    $commonPrograms = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)
    $userDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $commonDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)

    $candidates = @(
        (Join-Path $userPrograms "ECodex\ECodex.lnk"),
        (Join-Path $userPrograms "ECodex.lnk"),
        (Join-Path $commonPrograms "ECodex\ECodex.lnk"),
        (Join-Path $commonPrograms "ECodex.lnk"),
        (Join-Path $userDesktop "ECodex.lnk"),
        (Join-Path $commonDesktop "ECodex.lnk")
    ) | Select-Object -Unique

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            Get-ShortcutInfo $candidate
        }
    }
}

function Test-ToastPermission {
    $key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications"
    $value = $null
    try {
        $value = (Get-ItemProperty -Path $key -Name ToastEnabled -ErrorAction SilentlyContinue).ToastEnabled
    }
    catch {
        $value = $null
    }

    if ($value -eq 0) {
        Add-SmokeCheck "windows-toast-permission" "fail" "Windows toast notifications are disabled in HKCU PushNotifications ToastEnabled=0." @{ registryValue = $value }
        return $false
    }

    Add-SmokeCheck "windows-toast-permission" "ok" "Windows toast notifications are not globally disabled." @{ registryValue = $value }
    return $true
}

function Add-FocusAssistCheck {
    Add-SmokeCheck "focus-assist" "manual" "Check Windows Focus assist / Do not disturb manually; when enabled it can hide the toast banner while keeping it in notification center."
}

function Test-InstallerShortcutContract {
    $installerPath = Join-Path $RepoRoot "installer\ecodex.iss"
    if (-not (Test-Path -LiteralPath $installerPath)) {
        Add-SmokeCheck "installer-shortcuts" "warn" "installer/ecodex.iss was not found; shortcut registration cannot be checked."
        return
    }

    $installer = Get-Content -LiteralPath $installerPath -Raw
    $data = [ordered]@{
        startMenuShortcut = $installer.Contains('Name: "{group}\ECodex"', [System.StringComparison]::Ordinal)
        desktopShortcut = $installer.Contains('Name: "{autodesktop}\ECodex"', [System.StringComparison]::Ordinal)
        expectedAppUserModelId = $ExpectedAppUserModelId
        definesExpectedAppUserModelId = $installer.Contains("#define MyAppUserModelID `"$ExpectedAppUserModelId`"", [System.StringComparison]::Ordinal)
        assignsShortcutAppUserModelId = $installer.Contains('AppUserModelID: "{#MyAppUserModelID}"', [System.StringComparison]::Ordinal)
    }

    $status = if ($data.startMenuShortcut -and $data.desktopShortcut -and $data.definesExpectedAppUserModelId -and $data.assignsShortcutAppUserModelId) { "ok" } else { "warn" }
    Add-SmokeCheck "installer-shortcuts" $status "Inno installer shortcut entries checked; AppUserModelID remains a live-smoke prerequisite for unpackaged WPF activation." $data
}

function Test-NotificationMatches {
    param(
        [object] $Notification,
        [string] $Marker,
        [string] $ExpectedSource = "",
        [string] $ExpectedTitle = ""
    )

    $body = [string](Get-JsonProperty $Notification "body")
    if (-not $body.Contains($Marker, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedSource)) {
        $source = [string](Get-JsonProperty $Notification "source")
        if (-not $source.Equals($ExpectedSource, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedTitle)) {
        $title = [string](Get-JsonProperty $Notification "title")
        if (-not $title.Equals($ExpectedTitle, [System.StringComparison]::Ordinal)) {
            return $false
        }
    }

    return $true
}

function Wait-ForNotification {
    param(
        [string] $WorkspaceId,
        [string] $SurfaceId,
        [string] $PaneId,
        [string] $Marker,
        [string] $ExpectedSource = "",
        [string] $ExpectedTitle = ""
    )

    $last = $null
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(1, $WaitSeconds))
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $list = Invoke-ECodexJson @('--json', 'notification', 'list', '--unread', 'true', '--workspace', $WorkspaceId, '--surface', $SurfaceId, '--pane', $PaneId, '--limit', '20')
        Assert-ECodexSuccess $list "notification.list"
        $result = Get-ECodexResult $list
        $notifications = Get-JsonProperty $result "notifications"
        $last = $notifications
        foreach ($notification in @($notifications)) {
            if (Test-NotificationMatches -Notification $notification -Marker $Marker -ExpectedSource $ExpectedSource -ExpectedTitle $ExpectedTitle) {
                return $notification
            }
        }

        Start-Sleep -Milliseconds 300
    }

    $details = $last | ConvertTo-Json -Depth 8 -Compress
    throw "Notification with marker '$Marker' did not appear. ECodex may be foreground-active, or lifecycle notifications may be disabled. Last notifications: $details"
}

function Assert-NoNotification {
    param(
        [string] $WorkspaceId,
        [string] $SurfaceId,
        [string] $PaneId,
        [string] $Marker,
        [string] $ExpectedSource = "",
        [int] $TimeoutSeconds = 2
    )

    $last = $null
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(1, $TimeoutSeconds))
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $list = Invoke-ECodexJson @('--json', 'notification', 'list', '--unread', 'true', '--workspace', $WorkspaceId, '--surface', $SurfaceId, '--pane', $PaneId, '--limit', '20')
        Assert-ECodexSuccess $list "notification.list"
        $result = Get-ECodexResult $list
        $notifications = Get-JsonProperty $result "notifications"
        $last = $notifications
        foreach ($notification in @($notifications)) {
            if (Test-NotificationMatches -Notification $notification -Marker $Marker -ExpectedSource $ExpectedSource) {
                $details = $notification | ConvertTo-Json -Depth 8 -Compress
                throw "Unexpected notification with marker '$Marker' was found: $details"
            }
        }

        Start-Sleep -Milliseconds 300
    }

    return $last
}

if (-not (Test-IsWindows)) {
    Add-SmokeCheck "os" "skip" "Toast activation live smoke requires Windows."
    Write-SmokeSummary "skipped" "Requires Windows because Toast, shortcuts, and notification center are Windows-only."
}

Add-SmokeCheck "os" "ok" "Running on Windows."
Test-InstallerShortcutContract
$toastPermissionOk = Test-ToastPermission
Add-FocusAssistCheck

$shortcutInfos = @(Get-ECodexShortcuts)
$shortcutWithAumid = $null
if ($shortcutInfos.Count -eq 0) {
    Add-SmokeCheck "installed-shortcut" "warn" "No ECodex Start Menu or Desktop shortcut was found. Install with Inno/Velopack or create a shortcut before signing off Toast click activation."
}
else {
    $shortcutWithAumid = $shortcutInfos | Where-Object { [string]$_["appUserModelId"] -eq $ExpectedAppUserModelId } | Select-Object -First 1
    $shortcutStatus = if ($null -ne $shortcutWithAumid) { "ok" } else { "warn" }
    Add-SmokeCheck "installed-shortcut" $shortcutStatus "Installed ECodex shortcut(s) inspected; unpackaged WPF Toast/taskbar activation should use the expected AppUserModelID." @{ expectedAppUserModelId = $ExpectedAppUserModelId; shortcuts = $shortcutInfos }
}

if (-not $toastPermissionOk) {
    $status = if ($RequireActivationPrerequisites) { "failed" } else { "skipped" }
    Write-SmokeSummary $status "Windows toast notifications are globally disabled."
}

if ($RequireActivationPrerequisites -and ($shortcutInfos.Count -eq 0 -or $null -eq $shortcutWithAumid)) {
    Write-SmokeSummary "failed" "Toast activation prerequisites are incomplete: Start Menu/Desktop shortcut with AppUserModelID '$ExpectedAppUserModelId' was not confirmed."
}

$script:ResolvedCli = Resolve-ECodexCli
if ([string]::IsNullOrWhiteSpace($script:ResolvedCli)) {
    Add-SmokeCheck "cli" "skip" "ECodex CLI was not found. Build the CLI first or pass -CliPath."
    Write-SmokeSummary "skipped" "ECodex CLI was not found."
}

Add-SmokeCheck "cli" "ok" "Resolved ECodex CLI." @{ path = $script:ResolvedCli }

$workspaceId = $null
$workspaceRootFull = [System.IO.Path]::GetFullPath($WorkspaceRoot)
$tmpRootFull = [System.IO.Path]::GetFullPath($TmpRoot)
$removeWorkspaceRoot = Test-IsUnderDirectory $workspaceRootFull $tmpRootFull

try {
    try {
        $status = Invoke-ECodexJson @('--json', 'status')
        Assert-ECodexSuccess $status "status"
        Add-SmokeCheck "app-status" "ok" "ECodex app pipe responded to status."
    }
    catch {
        Add-SmokeCheck "app-status" "skip" "ECodex app is not running or status failed: $($_.Exception.Message)"
        Write-SmokeSummary "skipped" "ECodex app is not running."
    }

    New-Item -ItemType Directory -Path $workspaceRootFull -Force | Out-Null

    $workspaceCreate = Invoke-ECodexJson @('--json', 'workspace', 'create', '--name', $WorkspaceName, '--cwd', $workspaceRootFull)
    Assert-ECodexSuccess $workspaceCreate "workspace.create"
    $workspaceResult = Get-ECodexResult $workspaceCreate
    $workspace = Get-JsonProperty $workspaceResult "workspace"
    if ($null -eq $workspace) {
        throw "workspace.create did not return result.workspace."
    }

    $workspaceRef = Get-JsonProperty $workspace "ref"
    $workspaceId = Get-JsonProperty $workspace "id"
    $workspaceTarget = if (-not [string]::IsNullOrWhiteSpace($workspaceRef)) { $workspaceRef } else { $workspaceId }
    if ([string]::IsNullOrWhiteSpace($workspaceTarget) -or [string]::IsNullOrWhiteSpace($workspaceId)) {
        throw "workspace.create did not return workspace ref and id."
    }

    $paneList = Invoke-ECodexJson @('--json', 'pane', 'list', '--workspace', $workspaceTarget)
    Assert-ECodexSuccess $paneList "pane.list"
    $paneResult = Get-ECodexResult $paneList
    $surface = Get-JsonProperty $paneResult "surface"
    $panes = @(Get-JsonProperty $paneResult "panes")
    if ($null -eq $surface -or $panes.Count -eq 0) {
        throw "pane.list did not return a surface with panes."
    }

    $pane = $panes | Where-Object { (Get-JsonProperty $_ "isFocused") -eq $true } | Select-Object -First 1
    if ($null -eq $pane) {
        $pane = $panes[0]
    }

    $surfaceId = Get-JsonProperty $surface "id"
    $paneId = Get-JsonProperty $pane "id"
    if ([string]::IsNullOrWhiteSpace($surfaceId) -or [string]::IsNullOrWhiteSpace($paneId)) {
        throw "pane.list did not return surface id and pane id."
    }

    if ($Scenario -eq "CodexAttention") {
        $marker = "codex-attention-smoke-$([Guid]::NewGuid().ToString('N'))"
        $simulatedTriggerText = "Codex is waiting for user input. Please respond in the terminal. $marker"
        $script:Evidence["simulatedTriggerText"] = $simulatedTriggerText
        $paneWrite = Invoke-ECodexJson @('--json', 'pane', 'write', '--workspace', $workspaceTarget, "echo $simulatedTriggerText", '--submit', 'true')
        Assert-ECodexSuccess $paneWrite "pane.write.codex-attention"

        $notification = Wait-ForNotification -WorkspaceId $workspaceId -SurfaceId $surfaceId -PaneId $paneId -Marker $marker -ExpectedSource "AgentAttention" -ExpectedTitle "Codex 等待输入"
        $notificationId = Get-JsonProperty $notification "id"
        $source = Get-JsonProperty $notification "source"
        $title = Get-JsonProperty $notification "title"
        $body = Get-JsonProperty $notification "body"

        $script:Evidence["agentAttentionPayload"] = [ordered]@{
            notificationId = $notificationId
            source = $source
            title = $title
            body = $body
            marker = $marker
        }
        $script:Evidence["toastPayload"] = [ordered]@{
            action = "jumpToNotification"
            notificationId = $notificationId
            workspaceId = $workspaceId
            workspaceRef = $workspaceRef
            surfaceId = $surfaceId
            paneId = $paneId
            marker = $marker
            source = $source
        }

        Add-SmokeCheck "codex-attention-notification-created" "ok" "AgentAttention notification was created with workspace/surface/pane context." $script:Evidence["agentAttentionPayload"]

        $ordinaryMarker = "ordinary-output-$([Guid]::NewGuid().ToString('N'))"
        $ordinaryText = "Build succeeded without waiting for input $ordinaryMarker"
        $ordinaryWrite = Invoke-ECodexJson @('--json', 'pane', 'write', '--workspace', $workspaceTarget, "echo $ordinaryText", '--submit', 'true')
        Assert-ECodexSuccess $ordinaryWrite "pane.write.ordinary-output"
        Assert-NoNotification -WorkspaceId $workspaceId -SurfaceId $surfaceId -PaneId $paneId -Marker $ordinaryMarker -ExpectedSource "AgentAttention" | Out-Null

        $script:Evidence["negativeControl"] = [ordered]@{
            marker = $ordinaryMarker
            expectedSource = "AgentAttention"
            ordinaryText = $ordinaryText
        }
        Add-SmokeCheck "codex-attention-negative-control" "ok" "ordinary-output did not create an AgentAttention notification." $script:Evidence["negativeControl"]
    }
    else {
        $marker = "toast-smoke-$([Guid]::NewGuid().ToString('N'))"
        $paneWrite = Invoke-ECodexJson @('--json', 'pane', 'write', '--workspace', $workspaceTarget, $marker, '--submit', 'true')
        Assert-ECodexSuccess $paneWrite "pane.write"

        $hook = Invoke-ECodexJson @('--json', 'hook', 'event', '--phase', 'end', '--command', "ECodex Toast Smoke $marker", '--exit-code', '0', '--cwd', $workspaceRootFull, '--workspace-id', $workspaceId, '--surface-id', $surfaceId, '--pane-id', $paneId)
        Assert-ECodexSuccess $hook "hook.event"
        $notification = Wait-ForNotification -WorkspaceId $workspaceId -SurfaceId $surfaceId -PaneId $paneId -Marker $marker
        $notificationId = Get-JsonProperty $notification "id"

        $script:Evidence["toastPayload"] = [ordered]@{
            action = "jumpToNotification"
            notificationId = $notificationId
            workspaceId = $workspaceId
            workspaceRef = $workspaceRef
            surfaceId = $surfaceId
            paneId = $paneId
            marker = $marker
        }

        Add-SmokeCheck "notification-created" "ok" "Lifecycle notification was created with workspace/surface/pane context." $script:Evidence["toastPayload"]
    }

    $script:Evidence["cleanup"] = [ordered]@{
        workspaceId = $workspaceId
        workspaceRoot = $workspaceRootFull
        command = "ecodex --json workspace close $workspaceId; Remove-Item -LiteralPath '$workspaceRootFull' -Recurse -Force"
    }
    $script:PreparedManualSmoke = $true

    if ($Interactive) {
        $toastShown = Read-Host "Did the Windows Toast appear? (y/n)"
        $clickRestored = Read-Host "After clicking the Toast, did ECodex restore/focus? (y/n)"
        $paneFocused = Read-Host "Did it focus the target pane? (y/n)"
        $fallbackVisible = Read-Host "If you tested a missing pane, was fallback visible? (y/n/skip)"
        $sourceIsAgentAttention = if ($Scenario -eq "CodexAttention") { Read-Host "Was the notification source AgentAttention in evidence? (y/n)" } else { "skip" }
        $ordinaryOutputDidNotNotify = if ($Scenario -eq "CodexAttention") { Read-Host "Did ordinary-output avoid AgentAttention notification? (y/n)" } else { "skip" }
        $script:Evidence["manualEvidenceTemplate"] = [ordered]@{
            toastShown = $toastShown
            clickRestoredWindow = $clickRestored
            paneFocused = $paneFocused
            fallbackVisible = $fallbackVisible
            sourceIsAgentAttention = $sourceIsAgentAttention
            ordinaryOutputDidNotNotify = $ordinaryOutputDidNotNotify
            notes = "Recorded by -Interactive"
        }

        $scenarioPassed = $toastShown -match '^(y|yes)$' -and $clickRestored -match '^(y|yes)$' -and $paneFocused -match '^(y|yes)$'
        if ($Scenario -eq "CodexAttention") {
            $scenarioPassed = $scenarioPassed -and
                $sourceIsAgentAttention -match '^(y|yes)$' -and
                $ordinaryOutputDidNotNotify -match '^(y|yes)$'
        }

        if ($scenarioPassed) {
            Write-SmokeSummary "passed" "Manual Toast click smoke passed."
        }

        Write-SmokeSummary "failed" "Manual Toast click smoke did not pass."
    }

    Write-SmokeSummary "manual" "Notification prepared; complete the manual Toast click steps and record evidence."
}
catch {
    Add-SmokeCheck "smoke-error" "fail" $_.Exception.Message
    Write-SmokeSummary "failed" $_.Exception.Message
}
finally {
    $keepWorkspaceForManualClick = $script:PreparedManualSmoke -and -not $Interactive -and -not $Cleanup
    if (-not $keepWorkspaceForManualClick -and -not [string]::IsNullOrWhiteSpace($workspaceId)) {
        try {
            Invoke-ECodexJson @('--json', 'workspace', 'close', $workspaceId) | Out-Null
        }
        catch {
            Add-SmokeCheck "cleanup" "warn" "Failed to close smoke workspace ${workspaceId}: $($_.Exception.Message)"
        }
    }

    if (-not $keepWorkspaceForManualClick -and $removeWorkspaceRoot -and (Test-Path -LiteralPath $workspaceRootFull)) {
        Remove-Item -LiteralPath $workspaceRootFull -Recurse -Force -ErrorAction SilentlyContinue
    }
}
