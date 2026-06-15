#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$WebhookUrl = $env:DISCORD_WEBHOOK_URL,
    [string]$ReleaseName = $env:RELEASE_NAME,
    [string]$ReleaseTag = $env:RELEASE_TAG,
    [string]$ReleaseUrl = $env:RELEASE_URL,
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$ReleaseBody = $env:RELEASE_BODY,
    [bool]$Prerelease = ($env:RELEASE_PRERELEASE -match '(?i)^(true|1|yes)$'),
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-Blank {
    param([AllowNull()][string]$Value)

    return [string]::IsNullOrWhiteSpace($Value)
}

function Get-ValueOrFallback {
    param(
        [AllowNull()][string]$Value,
        [string]$Fallback
    )

    if (Test-Blank $Value) {
        return $Fallback
    }

    return $Value.Trim()
}

function Limit-DiscordText {
    param(
        [AllowNull()][string]$Value,
        [int]$MaxLength
    )

    if (Test-Blank $Value) {
        return $null
    }

    $normalized = ($Value -replace "`r`n", "`n").Trim()
    if ($normalized.Length -le $MaxLength) {
        return $normalized
    }

    return $normalized.Substring(0, [Math]::Max(0, $MaxLength - 3)) + "..."
}

function New-DiscordReleasePayload {
    param(
        [AllowNull()][string]$Name,
        [AllowNull()][string]$Tag,
        [AllowNull()][string]$Url,
        [AllowNull()][string]$Repo,
        [AllowNull()][string]$Body,
        [bool]$IsPrerelease
    )

    $safeRepo = Get-ValueOrFallback $Repo "unknown repository"
    $safeTag = Get-ValueOrFallback $Tag "untagged release"
    $safeName = Get-ValueOrFallback $Name "ECode release"
    $channel = if ($IsPrerelease) { "Prerelease" } else { "Stable" }
    $color = if ($IsPrerelease) { 0xF59E0B } else { 0x2563EB }
    $summary = Limit-DiscordText $Body 1200

    $descriptionParts = @("**$safeName**")
    if (-not (Test-Blank $summary)) {
        $descriptionParts += $summary
    }

    $embed = [ordered]@{
        title = "ECode $safeTag published"
        description = ($descriptionParts -join "`n`n")
        color = $color
        fields = @(
            [ordered]@{ name = "Repository"; value = $safeRepo; inline = $true }
            [ordered]@{ name = "Tag"; value = $safeTag; inline = $true }
            [ordered]@{ name = "Channel"; value = $channel; inline = $true }
        )
        footer = [ordered]@{ text = "GitHub Release notification" }
    }

    if (-not (Test-Blank $Url)) {
        $embed["url"] = $Url.Trim()
    }

    return [ordered]@{
        username = "ECode Releases"
        content = "ECode $channel release is available."
        allowed_mentions = [ordered]@{ parse = @() }
        embeds = @($embed)
    }
}

$payload = New-DiscordReleasePayload `
    -Name $ReleaseName `
    -Tag $ReleaseTag `
    -Url $ReleaseUrl `
    -Repo $Repository `
    -Body $ReleaseBody `
    -IsPrerelease $Prerelease
$json = $payload | ConvertTo-Json -Depth 8

if ($DryRun) {
    $json
    return
}

if (Test-Blank $WebhookUrl) {
    throw "DISCORD_WEBHOOK_URL is required unless -DryRun is used."
}

Invoke-RestMethod -Uri $WebhookUrl -Method Post -ContentType "application/json" -Body $json -TimeoutSec 20 | Out-Null
Write-Host "Discord release notification sent for $(Get-ValueOrFallback $ReleaseTag 'untagged release')."
