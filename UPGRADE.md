# Upgrade Relaywright

Relaywright upgrades are designed to preserve runtime data by default.

## Before Upgrading

1. Open the admin UI.
2. Create and validate a backup from `System -> Backups`.
3. Confirm you know the selected install root, data directory, service name, and admin HTTPS port.

Backup bundles intentionally do not restore admin passwords, Data Protection keys, protected relay secrets, or admin HTTPS certificate passwords. Keep host-level data backups for full machine recovery.

## Windows

Run the newer `Relaywright-<version>-windows-x64-installer.exe` as Administrator.

The installer:

- Stops the Relaywright service.
- Installs the new build under `releases`.
- Updates the Windows service path.
- Preserves the data directory.
- Starts the service and waits for `/health`.

## Linux

Run:

```bash
sudo bash install-relaywright.sh --version latest --update --non-interactive
```

The installer:

- Downloads and verifies the selected release.
- Installs the new build under `/opt/relaywright/releases`.
- Updates the `current` symlink.
- Preserves the data directory.
- Restarts the systemd service and waits for `/health`.

## Rollback

If a new release fails during install, keep the previous release directory and data directory intact. Point the Windows service path or Linux `current` symlink back to the previous release, then restart the service.

Relaywright currently uses `EnsureCreated` plus manual schema upgrades in `DataSeeder`, so do not downgrade across schema-changing releases without testing against a copy of the data directory.
