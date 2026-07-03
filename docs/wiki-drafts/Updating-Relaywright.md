# Updating Relaywright

Relaywright updates preserve runtime data by default.

Before updating:

1. Open the admin UI.
2. Create and validate a backup from `System -> Backups`.
3. Confirm the install root, data directory, service name, and admin HTTPS port.
4. Review the release notes.

## Windows Update

Run the newer `Relaywright-<version>-windows-x64-installer.exe` as Administrator.

The installer:

- stops the Relaywright service;
- installs the new release side-by-side under `releases`;
- updates the Windows service path;
- preserves the data directory;
- starts Relaywright and waits for `/health`.

## Linux Update

Run:

```bash
sudo bash install-relaywright.sh --version latest --update --non-interactive
```

The installer:

- downloads and verifies the selected release;
- installs the new build under `/opt/relaywright/releases`;
- updates the `current` symlink;
- preserves the data directory;
- restarts the systemd service and waits for `/health`.

## Rollback

If a new release fails during install, keep the previous release directory and data directory intact.

Rollback approach:

- point the Windows service path or Linux `current` symlink back to the previous release;
- restart the service;
- verify `/health`;
- review Logs and Diagnostics.

Do not downgrade across schema-changing releases without testing against a copy of the data directory.

## After Updating

1. Confirm the admin UI opens.
2. Confirm runtime status is healthy.
3. Run [[Diagnostics|Diagnostics]].
4. Send a controlled test email.
5. Check [[Queue Operations|Queue-Operations]] and Logs.
