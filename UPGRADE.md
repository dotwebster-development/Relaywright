# Upgrade Relaywright

Relaywright upgrades are designed to preserve runtime data by default.

For the `0.1.0-beta.1` to `1.0.0` upgrade, keep the existing data directory in place and run the newer installer. The startup schema upgrader preserves existing relay configuration, trusted networks, queue metadata, Data Protection keys, certificates, and backups.

## Before Upgrading

1. Open the admin UI.
2. Create and validate a backup from `System -> Backups`.
3. Confirm you know the selected install root, data directory, and admin HTTPS port. The Windows service name is `Relaywright`.

Backup bundles intentionally do not restore admin passwords, Data Protection keys, protected relay secrets, or admin HTTPS certificate passwords. Keep host-level data backups for full machine recovery.

## Windows

Run the newer `Relaywright-<version>-windows-x64-installer.exe` as Administrator.

The installer:

- Stops the Relaywright service.
- Installs the new build under `releases`.
- Updates the Windows service path.
- Preserves the data directory.
- Starts the service and waits for `/health`.
- Restores the previous service path and service environment if the new release fails health validation.

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

If a new Windows release fails during install, the installer attempts to restore the previous service path and service environment automatically. Review `C:\ProgramData\Relaywright\logs\installer-*.log` for the health-check and rollback result.

If manual rollback is required, keep the previous release directory and data directory intact. Point the Windows service path or Linux `current` symlink back to the previous release, then restart the service.

Relaywright currently uses `EnsureCreated` plus manual schema upgrades in `DataSeeder`, so do not downgrade across schema-changing releases without testing against a copy of the data directory.
