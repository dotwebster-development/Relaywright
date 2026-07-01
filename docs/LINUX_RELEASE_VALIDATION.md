# Linux Release Validation

This guide covers the destructive Linux release validation lane.

## VM Role

Use a disposable Linux install VM with a GitHub Actions self-hosted runner labeled:

```text
self-hosted
Linux
X64
relaywright
test
```

For the current shared-runner naming model, this is `test-linux01`. Do not use the active Linux deployment VM for this lane. The validation workflow removes services, firewall rules, install directories, and data directories.

Create the GitHub environment:

```text
linux-installer-test
```

If the runner account does not have passwordless `sudo`, add the environment secret `RELAYWRIGHT_LINUX_RELEASE_VALIDATION_SUDO_PASSWORD`.

## Workflow Modes

Run `Validate Linux Release` from GitHub Actions.

Use `runner_architecture=X64` for the normal Linux validation VM. Use `runner_architecture=ARM64` for the ARM64 validation VM.

### `clean-installer`

Use this for every release candidate.

```text
version=1.0.0-rc.5
mode=clean-installer
from_version=
```

The workflow downloads `install-relaywright.sh` and `SHA256SUMS.txt` from the GitHub Release, verifies the checksum, cleans the VM, runs the installer with HTTPS-only defaults and scoped firewall rules, validates systemd/health/data/firewall behavior, uploads artifacts, and then cleans the VM after success.

### `update-package`

Use this to test upgrade behavior from one published release to another.

```text
version=1.0.0-rc.6
mode=update-package
from_version=1.0.0-rc.5
```

The workflow installs `from_version`, creates preservation markers for listener config, spool, backups, and Data Protection keys, updates to `version`, validates health and data preservation, uploads artifacts, and then cleans the VM after success.

### `full-release`

Use this before promoting a release candidate.

```text
version=1.0.0-rc.6
mode=full-release
from_version=1.0.0-rc.5
```

This runs the update validation, cleans the VM, runs the fresh install validation, and cleans again after success.

### `cleanup-only`

Use this to reset the Linux install VM after failed runs or manual investigation.

## Expected Checks

- The systemd service is active.
- `https://127.0.0.1:5443/health` returns `ok` for fresh installs.
- HTTP admin port `5080` is closed by default.
- `relay.db`, `spool`, `keys`, `backups`, and `certs` are created under the data directory.
- Firewall rules exist for HTTPS and SMTP with source-scoped `local-subnet` behavior.
- HTTP firewall rules are not present.

The validation VM should have either active `ufw` or active `firewalld`; `ufw` is common on Ubuntu and `firewalld` is common on RHEL/Fedora. If no supported firewall is active, release validation fails because firewall defaults cannot be proven.

## ARM Validation

Linux releases publish x64 and ARM64 tarballs, plus a best-effort ARMv7 tarball until dedicated 32-bit ARM validation hardware is available. The installer auto-detects the host package unless `--runtime` is supplied.

Before advertising a release as ARM-ready, run at least a clean install on a real ARM64 runner or device, preferably a 64-bit Raspberry Pi OS or Ubuntu Server host. A separate self-hosted runner can use labels such as:

```text
self-hosted
Linux
ARM64
relaywright
test
```

Use `--runtime linux-arm64` only when intentionally overriding auto-detection. Treat `linux-arm` as unvalidated best-effort unless a real 32-bit ARMv7 runner or device has passed the same clean-install checks.
