# Contributing

Thanks for helping improve ECodex. This guide covers the local build, test, and pull request flow for the Windows-native app and CLI.

## Project Scope

ECodex is a Windows desktop terminal built with WPF, ConPTY, WebView2, named pipes, and .NET 10. Keep changes aligned with the current roadmap and backlog:

- `spec/06-roadmap.md` for product direction.
- `spec/07-implementation-backlog.md` for implementation items and acceptance notes.
- `md/` for user-facing documentation.

## Prerequisites

Install these before building:

- Windows 10 1809 or newer.
- .NET 10 SDK.
- Node.js 20 or newer for the VitePress docs site.
- PowerShell 7 (`pwsh`) for CI/publish scripts.
- Microsoft Edge WebView2 Runtime for Browser surfaces and browser integration checks.

Optional but useful:

- Visual Studio 2022 or Build Tools.
- Inno Setup for installer validation.
- Velopack tooling/feed access when testing updates.

## First-Time Setup

```powershell
git clone <repo-url> ecodex
cd ecodex
dotnet restore ECodex.sln
npm install
```

If NuGet advisory lookup fails in your environment with `NU1900`, retry local build/test commands with:

```powershell
-p:NuGetAudit=false
```

## Build

Use the checked-in SDK wrapper when available:

```powershell
.\.dotnet\dotnet.exe build ECodex.sln -c Debug -p:NuGetAudit=false
```

Or use the SDK on PATH:

```powershell
dotnet build ECodex.sln -c Debug
```

Run the WPF app:

```powershell
dotnet run --project src\ECodex\ECodex.csproj -c Debug
```

Build the docs site:

```powershell
npm run docs:build
```

## Test

Fast unit tests:

```powershell
.\.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj -p:NuGetAudit=false
```

Full local CI entrypoint:

```powershell
pwsh .\scripts\ci.ps1
```

Optional Windows/WebView2 checks:

```powershell
pwsh .\scripts\ci.ps1 -IncludeSmoke
pwsh .\scripts\ci.ps1 -IncludeBrowserIntegration
```

Publish validation is opt-in because it can be slower and may require packaging tools:

```powershell
pwsh .\scripts\ci.ps1 -IncludePublish -PublishFlavor Cli
```

If a Roslyn `VBCSCompiler` file lock appears during parallel build/test runs, rerun the failed command sequentially.

## Documentation

User docs live in `md/` and are built with VitePress.

When adding or changing docs:

- Update the matching page under `md/`.
- Add or adjust assertions in `tests/ECodex.Tests/CoreTests.cs` when the page is part of a backlog acceptance item.
- Ensure the file is copied by `tests/ECodex.Tests/ECodex.Tests.csproj` if tests read it from the test output directory.
- Run `npm run docs:build`.

## Coding Guidelines

- Keep code focused on the backlog item or bug being fixed.
- Prefer small, reviewable commits.
- Do not mix unrelated refactors with behavior changes.
- Preserve existing WPF styling and control patterns unless the task is explicitly design-focused.
- Prefer `rg` for repository search.
- Do not commit generated build output, local logs, or secrets.

For C#:

- Keep nullable behavior explicit.
- Prefer clear DTOs and contract tests for IPC/API changes.
- Keep stable error codes stable (`invalid_ref`, `not_found`, `stale_ref`, `not_supported`, `timeout`, `internal_error`).
- Add unit tests for services and protocol behavior.

For CLI/API changes:

- Document new commands in `md/cli.md`.
- Update PowerShell completion when adding public CLI commands.
- Add contract tests for new `ecodex.v2` methods.
- Preserve v1 compatibility commands unless the roadmap explicitly removes them.

## Pull Request Flow

Before opening a PR:

1. Rebase or merge the latest `main`.
2. Confirm `git status` only contains intended changes.
3. Run the relevant checks:

```powershell
npm run docs:build
.\.dotnet\dotnet.exe test tests\ECodex.Tests\ECodex.Tests.csproj -p:NuGetAudit=false
.\.dotnet\dotnet.exe build ECodex.sln -c Debug -p:NuGetAudit=false
```

4. For Browser, daemon, installer, update, or setup changes, also run the relevant optional checks from `scripts/ci.ps1`.
5. Update `CHANGELOG.md` under `[Unreleased]` for user-facing changes.
6. Update `spec/07-implementation-backlog.md` when completing a backlog item.

PR description checklist:

- What changed and why.
- How it was tested.
- Screenshots or clips for UI changes.
- Any known limitations or follow-up work.
- Linked issue/backlog id, for example `M7-B-01`.

Use labels that help release notes:

- `feature` / `enhancement` / `minor` for new user-facing capabilities.
- `bug` / `fix` / `patch` for fixes.
- `docs` / `documentation` for documentation-only changes.
- `build`, `ci`, `chore`, or `dependencies` for maintenance.
- `skip-changelog` for changes that should not appear in release notes.

## Security And Secrets

- Never commit API keys, tokens, passwords, or private certificates.
- Scrub `%USERPROFILE%\.ecodex\resume.json`, command logs, transcripts, and `daemon-debug.log` before attaching them to issues.
- For suspected vulnerabilities, use `SECURITY.md` once available instead of opening a public issue.

## Release Notes

Release Drafter reads `.github/release.yml` and groups PRs by labels. Keep PR titles user-readable; they become release-note entries.
