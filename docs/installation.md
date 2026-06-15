# Installation

ECode supports four Windows distribution paths. Use the zip/self-contained folder for nightly builds, Velopack for preview or stable builds, Inno Setup as a traditional fallback installer, and MSIX for managed enterprise deployment.

## Requirements

- Windows 10 1809 or newer for ConPTY.
- Microsoft Edge WebView2 Runtime for browser surfaces.
- .NET 10 Desktop Runtime for framework-dependent builds.
- No runtime is required for self-contained, Velopack, Inno Setup, or MSIX packages built from `--self-contained true` output.

Run this after installing or unpacking ECode:

```powershell
ecode doctor
```

`doctor` reports ConPTY, WebView2, PATH, daemon, and config directory status.

## Option 1: zip / self-contained folder

The self-contained folder is the simplest install path and is best for nightly builds or local testing.

Build or download the artifact named like `ecode-win-x64-sc`, then unpack it to a stable folder such as:

```text
%LOCALAPPDATA%\Programs\ECode
```

Start the app:

```powershell
%LOCALAPPDATA%\Programs\ECode\ecode-app.exe
```

Install CLI shell integration with a dry run first:

```powershell
ecode setup status --install-dir "%LOCALAPPDATA%\Programs\ECode"
ecode setup install --install-dir "%LOCALAPPDATA%\Programs\ECode"
ecode setup install --install-dir "%LOCALAPPDATA%\Programs\ECode" --write true
```

Uninstall is manual: close ECode, remove the folder, then remove shell integration:

```powershell
ecode setup uninstall --install-dir "%LOCALAPPDATA%\Programs\ECode"
ecode setup uninstall --install-dir "%LOCALAPPDATA%\Programs\ECode" --write true
```

## Option 2: Velopack installer and feed

Velopack is the preferred preview/stable path because it provides an installer and update feed.

Generate Velopack artifacts:

```powershell
pwsh ./scripts/publish.ps1 -Config Release -Rid win-x64 -Flavor Velopack -VpkCommand vpk
```

The output appears under:

```text
publish/velopack/
```

Publish all files in that directory together, including:

- `RELEASES`
- `.nupkg` packages
- setup executable

Configure update checks by setting a feed root or a direct `RELEASES` URL:

```powershell
$env:ECODE_UPDATE_FEED_URL = "https://example.com/ecode/releases"
ecode update check --feed-url $env:ECODE_UPDATE_FEED_URL
ecode update install --feed-url $env:ECODE_UPDATE_FEED_URL
```

Use `--download-only true` if you want to fetch the setup executable without launching it.

## Option 3: Inno Setup fallback

Inno Setup is the traditional installer fallback for environments where Velopack is not desired.

First publish the app and CLI folders:

```powershell
pwsh ./scripts/publish.ps1 -Config Release -Rid win-x64 -Flavor SelfContained
pwsh ./scripts/publish.ps1 -Config Release -Rid win-x64 -Flavor Cli
```

Then compile:

```powershell
iscc installer/ecode.iss
```

The script installs to:

```text
%LOCALAPPDATA%\Programs\ECode
```

It installs both `ecode-app.exe` and `ecode.exe`, creates Start Menu shortcuts, and removes only the install directory during uninstall.

## Option 4: MSIX enterprise package

MSIX is optional and intended for managed desktop deployment. It uses full-trust desktop packaging for the WPF app.

Generate an unsigned package:

```powershell
pwsh ./scripts/publish.ps1 -Config Release -Rid win-x64 -Flavor MSIX -MakeAppxCommand makeappx.exe
```

To sign during packaging:

```powershell
pwsh ./scripts/publish.ps1 \
  -Config Release \
  -Rid win-x64 \
  -Flavor MSIX \
  -MakeAppxCommand makeappx.exe \
  -MsixCertPath .\certs\ecode.pfx \
  -MsixCertPassword "<password>"
```

Install a signed package:

```powershell
Add-AppxPackage .\publish\msix\ECode-win-x64-1.0.0.0.msix
```

Unsigned packages require a trusted test certificate and developer/test deployment policy before installation.

## User data and uninstall behavior

Runtime data is stored outside install directories:

```text
%USERPROFILE%\.ecode
```

This includes settings, session state, snippets, resume bindings, and logs. Uninstallers should not delete this directory automatically. Remove it manually only when you intentionally want to reset ECode.

## Troubleshooting

- Run `ecode doctor` after install.
- If the CLI is not found, run `ecode setup status` and check PATH warnings.
- If browser surfaces fail, install or repair Microsoft Edge WebView2 Runtime.
- If update checks fail, verify the feed URL and that `RELEASES` is reachable.
