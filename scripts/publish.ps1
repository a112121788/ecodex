<#
.SYNOPSIS
  为 Windows 构建并发布 ECode（ECode + ECode.Cli）。

.DESCRIPTION
  复现 README.md 中记录的三种发布形态：
    1) 框架依赖版          -> publish/ecode-win-x64       （体积最小，需要 .NET 10 Desktop Runtime）
    2) 自包含目录版        -> publish/ecode-win-x64-sc    （文件夹形式，可在任意 win-x64 上运行）
    3) CLI                 -> publish/ecode-cli           （自包含，可直接放入 PATH）
    4) Velopack            -> publish/velopack            （installer + RELEASES feed）
    5) MSIX                -> publish/msix                （enterprise opt-in package）

  WPF + ConPTY 与 PublishSingleFile 配合不佳，因此有意省略了单文件
  发布形态。请改用自包含文件夹版本。

.PARAMETER Config
  构建配置。默认值：Release。

.PARAMETER Rid
  目标运行时标识符。默认值：win-x64。

.PARAMETER Flavor
  要生成的产物。默认值：All。
    Framework    -> 形态 1
    SelfContained-> 形态 2
    Cli          -> 形态 3
    Velopack    -> self-contained app + vpk pack
    MSIX        -> self-contained app + CLI + makeappx pack
    All          -> 1 + 2 + 3；installer flavors are opt-in

.PARAMETER OutputRoot
  输出根目录。默认值：<repo>\publish。

.PARAMETER MinimumExeBytes
  产物 exe 的最小字节数阈值。默认值：65536。

.EXAMPLE
  pwsh ./scripts/publish.ps1
  pwsh ./scripts/publish.ps1 -Flavor SelfContained
  pwsh ./scripts/publish.ps1 -Flavor Cli -Rid win-arm64
  pwsh ./scripts/publish.ps1 -Flavor Velopack -VpkCommand vpk
  pwsh ./scripts/publish.ps1 -Flavor MSIX -MakeAppxCommand makeappx.exe
  pwsh ./scripts/publish.ps1 -Config Debug -Flavor Framework
#>

[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Config = 'Release',

    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$Rid = 'win-x64',

    [ValidateSet('All', 'Framework', 'SelfContained', 'Cli', 'Velopack', 'MSIX')]
    [string]$Flavor = 'All',

    [string]$OutputRoot,

    [string]$VelopackPackId = 'ECode',

    [string]$VelopackAuthors = 'ECode',

    [string]$VpkCommand = 'vpk',

    [string]$MakeAppxCommand = 'makeappx.exe',

    [string]$SignToolCommand = 'signtool.exe',

    [string]$MsixPackageName = 'ECode',

    [string]$MsixPublisher = 'CN=ECode',

    [string]$MsixCertPath,

    [string]$MsixCertPassword,

    [ValidateRange(1, [int]::MaxValue)]
    [int]$MinimumExeBytes = 65536
)

$ErrorActionPreference = 'Stop'

# --- 定位仓库根目录（脚本位于 <repo>/scripts 下）-----------------------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Resolve-Path (Join-Path $ScriptDir '..')
if ($OutputRoot) {
    $OutputRoot = (Resolve-Path $OutputRoot).Path
} else {
    $OutputRoot = Join-Path $RepoRoot 'publish'
}

$MainProj = Join-Path $RepoRoot 'src/ECode/ECode.csproj'
$CliProj  = Join-Path $RepoRoot 'src/ECode.Cli/ECode.Cli.csproj'
$Validations = @()

function Get-ProjectVersion {
    $props = [xml](Get-Content -LiteralPath (Join-Path $RepoRoot 'Directory.Build.props') -Raw)
    return $props.Project.PropertyGroup.Version
}

function Get-MsixVersion {
    $core = (Get-ProjectVersion).Split('-', '+')[0]
    $parts = @($core.Split('.'))
    while ($parts.Count -lt 4) { $parts += '0' }
    return ($parts[0..3] -join '.')
}

function Get-MsixArchitecture {
    switch ($Rid) {
        'win-x64' { return 'x64' }
        'win-x86' { return 'x86' }
        'win-arm64' { return 'arm64' }
        default { throw "Unsupported MSIX RID: $Rid" }
    }
}

function Invoke-DotnetPublish {
    param(
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][string]$ArtifactsPath,
        [Parameter(Mandatory)][string[]]$Args
    )
    Reset-PublishDir $ArtifactsPath

    Write-Host ""
    Write-Host ">> dotnet publish $Project --artifacts-path $ArtifactsPath $($Args -join ' ')" -ForegroundColor Cyan
    $publishArgs = @('publish', $Project, '--artifacts-path', $ArtifactsPath, '-p:NuGetAudit=false') + $Args
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Project (exit $LASTEXITCODE)"
    }
}

function Ensure-Dir([string]$path) {
    if (-not (Test-Path $path)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
}

function Reset-PublishDir([string]$path) {
    $trimChars = @([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $root = [System.IO.Path]::GetFullPath($OutputRoot).TrimEnd($trimChars)
    $full = [System.IO.Path]::GetFullPath($path).TrimEnd($trimChars)
    $rootPrefix = $root + [System.IO.Path]::DirectorySeparatorChar

    if (-not $full.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean publish directory outside OutputRoot: $full"
    }

    if (Test-Path $full) {
        Write-Host ">> removing stale publish output $full"
        Remove-Item -LiteralPath $full -Recurse -Force
    }
    Ensure-Dir $full
}

function Add-PublishValidation {
    param(
        [Parameter(Mandatory)][string]$FlavorName,
        [Parameter(Mandatory)][string]$ExePath,
        [int]$MinimumBytes = $MinimumExeBytes
    )

    $full = [System.IO.Path]::GetFullPath($ExePath)
    $exists = Test-Path -LiteralPath $full -PathType Leaf
    $size = 0L
    $sha256 = ''
    $version = ''
    $status = 'OK'
    $errorText = ''

    if (-not $exists) {
        $status = 'FAIL'
        $errorText = 'missing'
    } else {
        $item = Get-Item -LiteralPath $full
        $size = $item.Length
        if ($size -lt $MinimumBytes) {
            $status = 'FAIL'
            $errorText = "size<$MinimumBytes"
        }

        $sha256 = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
        $version = $item.VersionInfo.FileVersion
        if ([string]::IsNullOrWhiteSpace($version)) {
            $status = 'FAIL'
            $errorText = if ($errorText) { "$errorText;no-version" } else { 'no-version' }
        }
    }

    $script:Validations += [pscustomobject]@{
        Flavor  = $FlavorName
        File    = $full
        Exists  = $exists
        SizeMB  = [Math]::Round($size / 1MB, 2)
        Version = $version
        Sha256  = $sha256
        Status  = if ($errorText) { "$status ($errorText)" } else { $status }
    }
}

function Add-FileValidation {
    param(
        [Parameter(Mandatory)][string]$FlavorName,
        [Parameter(Mandatory)][string]$FilePath
    )

    $full = [System.IO.Path]::GetFullPath($FilePath)
    $exists = Test-Path -LiteralPath $full -PathType Leaf
    $size = if ($exists) { (Get-Item -LiteralPath $full).Length } else { 0L }
    $sha256 = if ($exists) { (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant() } else { '' }

    $script:Validations += [pscustomobject]@{
        Flavor  = $FlavorName
        File    = $full
        Exists  = $exists
        SizeMB  = [Math]::Round($size / 1MB, 2)
        Version = ''
        Sha256  = $sha256
        Status  = if ($exists) { 'OK' } else { 'FAIL (missing)' }
    }
}

function Invoke-VelopackPack {
    param(
        [Parameter(Mandatory)][string]$PackDir,
        [Parameter(Mandatory)][string]$OutputDir
    )

    Reset-PublishDir $OutputDir
    $version = Get-ProjectVersion
    $args = @(
        'pack',
        '--packId', $VelopackPackId,
        '--packVersion', $version,
        '--packAuthors', $VelopackAuthors,
        '--packDir', $PackDir,
        '--mainExe', 'ecode-app.exe',
        '--outputDir', $OutputDir
    )

    Write-Host ""
    Write-Host ">> $VpkCommand $($args -join ' ')" -ForegroundColor Cyan
    & $VpkCommand @args
    if ($LASTEXITCODE -ne 0) {
        throw "vpk pack failed (exit $LASTEXITCODE). Install Velopack CLI or pass -VpkCommand."
    }

    Add-FileValidation -FlavorName 'VelopackFeed' -FilePath (Join-Path $OutputDir 'RELEASES')
    $setup = Get-ChildItem -LiteralPath $OutputDir -Filter '*Setup*.exe' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($setup) {
        Add-FileValidation -FlavorName 'VelopackSetup' -FilePath $setup.FullName
    } else {
        Add-FileValidation -FlavorName 'VelopackSetup' -FilePath (Join-Path $OutputDir 'ECodeSetup.exe')
    }
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory)][string]$SourceDir,
        [Parameter(Mandatory)][string]$DestinationDir
    )

    Ensure-Dir $DestinationDir
    Copy-Item -LiteralPath (Join-Path $SourceDir '.') -Destination $DestinationDir -Recurse -Force
}

function New-ResizedPngAsset {
    param(
        [Parameter(Mandatory)][string]$SourcePng,
        [Parameter(Mandatory)][string]$DestinationPng,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height
    )

    Add-Type -AssemblyName PresentationCore
    $sourceUri = [Uri]::new([System.IO.Path]::GetFullPath($SourcePng))
    $decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create(
        $sourceUri,
        [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $frame = $decoder.Frames[0]
    $scale = [System.Windows.Media.ScaleTransform]::new($Width / $frame.PixelWidth, $Height / $frame.PixelHeight)
    $bitmap = [System.Windows.Media.Imaging.TransformedBitmap]::new($frame, $scale)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.File]::Create($DestinationPng)
    try {
        $encoder.Save($stream)
    } finally {
        $stream.Dispose()
    }
}

function Write-MsixManifest {
    param(
        [Parameter(Mandatory)][string]$OutputPath
    )

    $template = Join-Path $RepoRoot 'installer/AppXManifest.xml'
    [xml]$manifest = Get-Content -LiteralPath $template -Raw
    $ns = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $ns.AddNamespace('m', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $identity = $manifest.SelectSingleNode('/m:Package/m:Identity', $ns)
    $identity.SetAttribute('Name', $MsixPackageName)
    $identity.SetAttribute('Publisher', $MsixPublisher)
    $identity.SetAttribute('Version', (Get-MsixVersion))
    $identity.SetAttribute('ProcessorArchitecture', (Get-MsixArchitecture))
    $manifest.Save($OutputPath)
}

function Invoke-MsixPack {
    param(
        [Parameter(Mandatory)][string]$AppDir,
        [Parameter(Mandatory)][string]$CliDir,
        [Parameter(Mandatory)][string]$OutputDir
    )

    $staging = Join-Path $OutputRoot '.msix-staging'
    Reset-PublishDir $staging
    Copy-DirectoryContents -SourceDir $AppDir -DestinationDir $staging
    Copy-DirectoryContents -SourceDir $CliDir -DestinationDir $staging

    $assetsDir = Join-Path $staging 'Assets'
    Ensure-Dir $assetsDir
    $sourceIcon = Join-Path $RepoRoot 'src/ECode/Assets/app-icon.png'
    New-ResizedPngAsset -SourcePng $sourceIcon -DestinationPng (Join-Path $assetsDir 'StoreLogo.png') -Width 50 -Height 50
    New-ResizedPngAsset -SourcePng $sourceIcon -DestinationPng (Join-Path $assetsDir 'Square44x44Logo.png') -Width 44 -Height 44
    New-ResizedPngAsset -SourcePng $sourceIcon -DestinationPng (Join-Path $assetsDir 'Square150x150Logo.png') -Width 150 -Height 150
    Write-MsixManifest -OutputPath (Join-Path $staging 'AppXManifest.xml')

    Reset-PublishDir $OutputDir
    $packagePath = Join-Path $OutputDir ("$MsixPackageName-$Rid-" + (Get-MsixVersion) + '.msix')
    $makeAppxArgs = @('pack', '/d', $staging, '/p', $packagePath, '/overwrite')

    Write-Host ""
    Write-Host ">> $MakeAppxCommand $($makeAppxArgs -join ' ')" -ForegroundColor Cyan
    & $MakeAppxCommand @makeAppxArgs
    if ($LASTEXITCODE -ne 0) {
        throw "makeappx pack failed (exit $LASTEXITCODE). Install Windows SDK or pass -MakeAppxCommand."
    }

    if ($MsixCertPath) {
        $signArgs = @('sign', '/fd', 'SHA256', '/f', $MsixCertPath)
        if ($MsixCertPassword) {
            $signArgs += @('/p', $MsixCertPassword)
        }
        $signArgs += $packagePath
        Write-Host ">> $SignToolCommand $($signArgs -join ' ')" -ForegroundColor Cyan
        & $SignToolCommand @signArgs
        if ($LASTEXITCODE -ne 0) {
            throw "signtool sign failed (exit $LASTEXITCODE)."
        }
    } else {
        Write-Host ">> MSIX package is unsigned. Sign it before Add-AppxPackage." -ForegroundColor Yellow
    }

    Add-FileValidation -FlavorName 'MSIX' -FilePath $packagePath
}

function Write-ValidationTable {
    if ($Validations.Count -eq 0) { return }

    Write-Host ""
    Write-Host "=== artifact validation ===" -ForegroundColor Yellow
    $Validations | Format-Table Flavor, Exists, SizeMB, Version, Sha256, File, Status -AutoSize

    $failed = @($Validations | Where-Object { $_.Status -notlike 'OK*' })
    if ($failed.Count -gt 0) {
        throw "Publish artifact validation failed for $($failed.Count) artifact(s)."
    }
}

Ensure-Dir $OutputRoot
# 把 MSBuild 的 bin/obj 放进发布目录下的隔离缓存，避免覆盖正在运行的开发版 exe。
$BuildRoot = Join-Path $OutputRoot '.build'

$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
Write-Host "=== ECode publish === Config=$Config Rid=$Rid Flavor=$Flavor Time=$stamp ===" -ForegroundColor Yellow

$ran = @()
$selfContainedOut = Join-Path $OutputRoot "ecode-$Rid-sc"
$cliOut = Join-Path $OutputRoot "ecode-cli"
$selfContainedPublished = $false
$cliPublished = $false

if ($Flavor -in @('All', 'Framework')) {
    $out = Join-Path $OutputRoot "ecode-$Rid"
    $artifacts = Join-Path $BuildRoot 'framework'
    Reset-PublishDir $out
    Invoke-DotnetPublish -Project $MainProj -ArtifactsPath $artifacts -Args @(
        '-c', $Config,
        '-r', $Rid,
        '--self-contained', 'false',
        '-o', $out
    )
    $exe = Join-Path $out 'ecode-app.exe'
    Add-PublishValidation -FlavorName 'Framework' -ExePath $exe
    $ran += "Framework      -> $exe"
}

if ($Flavor -in @('All', 'SelfContained')) {
    $out = $selfContainedOut
    $artifacts = Join-Path $BuildRoot 'self-contained'
    Reset-PublishDir $out
    Invoke-DotnetPublish -Project $MainProj -ArtifactsPath $artifacts -Args @(
        '-c', $Config,
        '-r', $Rid,
        '--self-contained', 'true',
        '-o', $out
    )
    $exe = Join-Path $out 'ecode-app.exe'
    Add-PublishValidation -FlavorName 'SelfContained' -ExePath $exe
    $ran += "SelfContained  -> $exe"
    $selfContainedPublished = $true
}

if ($Flavor -in @('All', 'Cli')) {
    $out = $cliOut
    $artifacts = Join-Path $BuildRoot 'cli'
    Reset-PublishDir $out
    Invoke-DotnetPublish -Project $CliProj -ArtifactsPath $artifacts -Args @(
        '-c', $Config,
        '-r', $Rid,
        '--self-contained', 'true',
        '-o', $out
    )
    $exe = Join-Path $out 'ecode.exe'
    Add-PublishValidation -FlavorName 'Cli' -ExePath $exe
    $ran += "Cli            -> $exe"
    $cliPublished = $true
}

if ($Flavor -eq 'Velopack') {
    if (-not $selfContainedPublished) {
        $artifacts = Join-Path $BuildRoot 'velopack-self-contained'
        Reset-PublishDir $selfContainedOut
        Invoke-DotnetPublish -Project $MainProj -ArtifactsPath $artifacts -Args @(
            '-c', $Config,
            '-r', $Rid,
            '--self-contained', 'true',
            '-o', $selfContainedOut
        )
        $selfContainedPublished = $true
    }

    $velopackOut = Join-Path $OutputRoot 'velopack'
    Invoke-VelopackPack -PackDir $selfContainedOut -OutputDir $velopackOut
    $ran += "Velopack       -> $velopackOut"
}

if ($Flavor -eq 'MSIX') {
    if (-not $selfContainedPublished) {
        $artifacts = Join-Path $BuildRoot 'msix-app'
        Reset-PublishDir $selfContainedOut
        Invoke-DotnetPublish -Project $MainProj -ArtifactsPath $artifacts -Args @(
            '-c', $Config,
            '-r', $Rid,
            '--self-contained', 'true',
            '-o', $selfContainedOut
        )
        $selfContainedPublished = $true
    }

    if (-not $cliPublished) {
        $artifacts = Join-Path $BuildRoot 'msix-cli'
        Reset-PublishDir $cliOut
        Invoke-DotnetPublish -Project $CliProj -ArtifactsPath $artifacts -Args @(
            '-c', $Config,
            '-r', $Rid,
            '--self-contained', 'true',
            '-o', $cliOut
        )
        $cliPublished = $true
    }

    $msixOut = Join-Path $OutputRoot 'msix'
    Invoke-MsixPack -AppDir $selfContainedOut -CliDir $cliOut -OutputDir $msixOut
    $ran += "MSIX           -> $msixOut"
}

Write-Host ""
Write-Host "=== done ===" -ForegroundColor Green
foreach ($r in $ran) { Write-Host "  $r" }
Write-ValidationTable
