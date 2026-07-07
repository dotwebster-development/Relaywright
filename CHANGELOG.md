# Changelog

All notable Relaywright release changes are tracked here.

## Unreleased

## 1.0.2

- Overhauls the Windows installer around operator choices for data location, database provider, ports, firewall scope, optional bootstrap admin, and final review.
- Integrates Windows install, update, uninstall, preflight checks, redacted diagnostics, health verification, and update rollback into the installer package.
- Replaces raw SQL connection-string entry with structured SQLite, SQL Server, and MySQL fields while keeping passwords out of installer logs and review output.
- Keeps the Windows service name stable as `Relaywright` and sets the display name to `Relaywright - SMTP relay gateway`.
- Shortens retention for large generated GitHub Actions package handoff artifacts so release and deployment workflows do not exhaust Actions storage.

## 1.0.1

- Adds typed server-side validation and browser hints across user-editable admin fields, including relay settings, trusted networks, submission policy, alerts, backups, web listener/certificates, diagnostics, account forms, queue search, and log search.
- Keeps passwords, certificate passwords, OAuth secrets, backup passwords, restore passwords, message bodies, and search fields free of character allowlists while enforcing length and control-character checks.
- Hardens service-layer validation for relay configuration, trusted networks, submission policy, alerts, backups, web listener settings, and HTTPS certificate operations.
- Adds an in-app update status check and refreshed website content.
- Adds a Linux ARM64 release package for Raspberry Pi class and other small-office ARM devices, plus a best-effort ARMv7 package until 32-bit ARM validation hardware is available.
- Updates the Linux installer to auto-detect x64, ARM64, and ARMv7 hosts, with `--runtime` available for explicit overrides.
- Updates release/install defaults to point at the current `dotwebster-development/Relaywright` release repository.

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
