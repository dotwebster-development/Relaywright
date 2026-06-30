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

## Ugly-Path Validation

Run `Validate Linux Ugly Paths` from GitHub Actions after the release install/update gates pass.

Use a disposable Linux install VM with the same runner labels as the release validation lane:

```text
self-hosted
Linux
X64
relaywright
test
```

The workflow installs the selected release into an isolated service and data root:

```text
Service: relaywright-ugly
Install root: /opt/relaywright-ugly
Data root: /var/lib/relaywright-ugly
Admin HTTPS: 5743
SMTP listener: 2529
Capture SMTP: 2530
```

Modes:

- `ugly-paths`: installs the release, runs the destructive failure-path checks, uploads artifacts, and cleans the VM after success by default.
- `cleanup-only`: removes the isolated ugly-path service, install root, and data root.

The current Linux ugly-path gate covers:

- obstructed spool path: SMTP DATA is rejected, no queue metadata is created, and the service recovers after the path is restored;
- SQLite lock contention: SMTP DATA is not falsely accepted while the database is locked, and the service recovers afterward;
- upstream outage: SMTP intake still queues durably, delivery retries, and the message delivers after the capture relay returns;
- bad HTTPS certificate password: the service fails health/startup, then recovers after restoring the original environment file;
- restart during active delivery: an in-progress message is recovered through stale-claim behavior after restart;
- cold data-directory backup/restore: marker files and service health survive a stopped-service tar/restore cycle.

The workflow does not upload the restored data backup because it may contain Data Protection keys or certificates. It records fingerprints, counts, health output, queue counts, service status, and journal excerpts instead.

## Soak Validation Runner

Long-running soak validation uses the same project label but a different role label so it can run on `soak-linux01` without occupying the destructive installer VM:

```text
self-hosted
Linux
X64
relaywright
soak
```

The role labels are intentionally generic. A future project can reuse the same machines by adding that project's label and routing its workflows with `project-name` plus `deploy`, `test`, or `soak`.
