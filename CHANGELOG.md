# Changelog

All notable Relaywright release changes are tracked here.

## Unreleased

- Adds a Linux ARM64 release package for Raspberry Pi class and other small-office ARM devices, plus a best-effort ARMv7 package until 32-bit ARM validation hardware is available.
- Updates the Linux installer to auto-detect x64, ARM64, and ARMv7 hosts, with `--runtime` available for explicit overrides.

## 1.0.0

- First stable release line.
- Ships production-safe admin web defaults with HTTPS enabled and HTTP disabled unless explicitly configured.
- Keeps Windows firewall installer defaults scoped to the local subnet instead of all remote addresses.
- Adds CI/release vulnerability gates, upgrade-path regression coverage, and release-readiness validation guidance.
- Includes the ASP.NET Core admin UI, trusted IP/device policy enforcement, durable SMTP spool, queue retry/cleanup, diagnostics, alerts, backups, settings rollback, and Windows/Linux release packaging.

## 0.1.0-beta.1

- First public beta release line.
- Includes the ASP.NET Core admin UI, trusted IP/device policy enforcement, durable SMTP spool, queue retry/cleanup, diagnostics, alerts, backups, and settings rollback.
- Adds production release packaging for Windows x64 and Linux x64.
- Adds Windows installer, Linux installer script, GitHub release artifacts, checksums, and public install documentation.
- Backup and restore handling strips admin credentials, protected relay secrets, Data Protection keys, and sensitive history text from portable backup artifacts.
