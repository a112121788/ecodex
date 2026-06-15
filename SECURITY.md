# Security Policy

Thank you for helping keep ECodeX users safe. Please report suspected vulnerabilities privately so maintainers have time to investigate and prepare a fix before details are public.

## Supported Versions

ECodeX is still pre-1.0. Security fixes are prioritized on the `main` branch and the latest published release line.

| Version | Supported |
|---|---|
| Latest release | Yes |
| `main` branch | Yes, for upcoming fixes |
| Older prerelease builds | Best effort |

If you are unsure whether a build is supported, include the output of:

```powershell
ecodex version
ecodex --json doctor
```

## Report A Vulnerability

Do not open a public issue for an unpatched vulnerability.

Preferred private channels:

1. Use GitHub's private vulnerability reporting / Security Advisory flow for this repository, if it is enabled.
2. If private reporting is not available, contact the project maintainers privately through the repository owner's published contact channel.
3. If neither private route is available, open a public issue with only a minimal statement such as "security report requested" and no exploit details; a maintainer will arrange a private channel.

Include as much of the following as you can safely share:

- Affected version or commit.
- Windows version and architecture.
- Installation method: zip/self-contained folder, Velopack, Inno Setup, or MSIX.
- Steps to reproduce.
- Impact and expected attacker capabilities.
- Whether the issue is already being exploited.
- Minimal logs or screenshots with secrets removed.

## What To Avoid Sharing Publicly

Please redact secrets and sensitive local data before attaching files:

- API keys, tokens, passwords, private certificates, SSH keys, and cookies.
- `%USERPROFILE%\.ecodex\resume.json` command text that contains internal hostnames or credentials.
- Command logs and terminal transcripts under `%USERPROFILE%\.ecodex\logs\`.
- `%USERPROFILE%\.ecodex\daemon-debug.log` lines containing private paths, commands, or environment details.
- Project-specific `.ecodex\ecodex.json` entries that run private deployment or migration commands.

## Scope

Security reports are in scope when they affect ECodeX or its distributed tooling, including:

- WPF app, CLI, daemon, named-pipe IPC, and `ecodex.v2` APIs.
- Browser surface automation and WebView2 integration.
- Installer/update flows, including Velopack feed handling and package scripts.
- Resume bindings, command logs, transcript capture, and secret redaction.
- PATH/profile setup changes made by `ecodex setup`.

Out of scope:

- Vulnerabilities in third-party websites opened inside a Browser surface.
- Issues requiring a malicious local administrator.
- General hardening suggestions without a concrete exploit path.
- Dependency CVEs that do not affect shipped or reachable code paths.

## Handling Process

Maintainers aim to:

| Stage | Target |
|---|---|
| Acknowledge report | Within 3 business days |
| Initial triage | Within 7 business days |
| Fix or mitigation plan | As soon as practical based on severity |
| Public advisory | After a fix or mitigation is available |

The exact timeline can vary for complex issues, installer/update problems, or reports that require coordinated dependency fixes.

## Coordinated Disclosure

Please give maintainers a reasonable opportunity to release a fix before publishing exploit details. We will credit reporters in release notes or advisories when requested, unless you prefer to remain anonymous.

## Security-Relevant Development Notes

When contributing security-sensitive changes:

- Add tests for stable error codes, trust decisions, or redaction behavior.
- Keep sensitive values out of logs and snapshots.
- Prefer explicit user confirmation or trust state before running commands.
- Update `CHANGELOG.md`, docs, and release notes for user-visible security changes.
