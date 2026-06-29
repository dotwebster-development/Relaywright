# Windows Release Validation

This guide covers the destructive Windows release validation lane.

## VM Roles

- `DOT-WINDOWS01` stays on `.github/workflows/deploy-windows-test.yml` with runner label `relaywright-test-windows`.
- `DOT-WINDOWS02` runs `.github/workflows/validate-windows-release.yml` with runner label `relaywright-install-windows`.

Do not put `relaywright-install-windows` on `DOT-WINDOWS01`. The release validation workflow removes services, firewall rules, install directories, and data directories.

## DOT-WINDOWS02 Runner Setup

Install a GitHub Actions self-hosted runner on `DOT-WINDOWS02` and add these labels:

```text
self-hosted
Windows
relaywright-install-windows
```

Install the runner as a Windows service that can perform administrator tasks. The workflow needs to install Windows services, create certificates, and manage Windows Firewall rules.

Create the GitHub environment:

```text
windows-installer-test
```

No environment secrets are required for the default validation path.

## Workflow Modes

Run `Validate Windows Release` from GitHub Actions.

### `clean-installer`

Use this for every release candidate.

Inputs:

```text
version=1.0.0-rc.5
mode=clean-installer
from_version=
```

The workflow downloads the real installer from the GitHub Release, verifies `SHA256SUMS.txt`, cleans the VM, installs silently with production defaults, validates HTTPS/HTTP/firewall/data/service behavior, uploads artifacts, and then cleans the VM again after success.

If validation fails, the workflow leaves the VM state in place for debugging. Run `cleanup-only` after inspection.

### `update-package`

Use this when testing upgrade behavior from one published release artifact to another.

Inputs:

```text
version=1.0.0-rc.6
mode=update-package
from_version=1.0.0-rc.5
```

The workflow installs the `from_version` Windows ZIP through `scripts/windows/Install-Relaywright.ps1`, creates preservation markers in the data directory, updates to `version`, verifies health/firewall/data preservation, uploads artifacts, and then cleans the VM after success.

This validates the package update path. Keep a separate manual/browser pass for full admin-login, trusted-network, and SMTP traffic behavior until those flows are automated end to end.

### `full-release`

Use this before promoting a release candidate.

Inputs:

```text
version=1.0.0-rc.6
mode=full-release
from_version=1.0.0-rc.5
```

The workflow cleans `DOT-WINDOWS02`, installs `from_version`, writes preservation markers for listener config, spool, backups, and Data Protection keys, updates to `version`, validates service/health/HTTPS/HTTP-disabled/firewall/data preservation, cleans again, then performs a fresh silent installer validation for `version`.

### `cleanup-only`

Use this to reset `DOT-WINDOWS02`.

Inputs:

```text
version=1.0.0-rc.5
mode=cleanup-only
from_version=
```

The workflow removes:

- `Relaywright`
- `RelaywrightReleaseValidation`
- `C:\Program Files\Relaywright`
- `C:\ProgramData\Relaywright`
- `C:\Relaywright\ReleaseValidation`
- Windows Firewall groups `Relaywright` and `Relaywright Release Validation`

The script refuses to recursively remove any directory outside its known allow-list.

## Expected Clean Installer Checks

- `Relaywright` service exists and reaches `Running`.
- `https://127.0.0.1:5443/health` returns `ok`.
- `127.0.0.1:5080` is closed.
- `https://127.0.0.1:5443/Account/Setup` renders first-run setup.
- `C:\ProgramData\Relaywright\relay.db` exists.
- `spool`, `keys`, `backups`, and `certs` directories exist.
- Windows Firewall rules exist for HTTPS and SMTP.
- Windows Firewall remote address is not `Any`.
- HTTP port `5080` is not opened by the Relaywright firewall group.

Artifacts are uploaded for every run, including validation inputs, health output, setup page HTML, service state, firewall state, and installer logs when applicable.
