# Backups And Restore

Relaywright can create, validate, download, delete, and restore backup bundles from the admin UI.

Open:

```text
System -> Backups
```

## What Backups Include

For SQLite installs, Relaywright backup bundles cover:

- database snapshot;
- message spool files referenced by the database snapshot;
- certificates and listener configuration where applicable;
- backup metadata needed for restore validation.

Successful backups are validated immediately so the dashboard can report restore readiness.

## What Backups Do Not Restore

Portable backup bundles intentionally do not restore:

- admin accounts;
- admin passwords;
- protected relay secrets;
- Data Protection keys;
- admin HTTPS certificate passwords;
- sensitive diagnostic or operational failure text.

Keep host-level backups for full machine recovery, especially Data Protection keys and service-level secrets.

## Encrypted Manual Backups

Manual backups can be encrypted with a one-time password. The password is not stored.

Scheduled backups remain unencrypted because Relaywright does not store a scheduled-backup encryption password.

## Scheduled Backups

Use scheduled backups to create regular SQLite backup bundles. Configure interval and retention carefully so backup storage does not fill the data volume.

## Restore

Restore stages a backup and requests a restart. Restore replaces database, spool, certificates, and listener settings after restart.

Before restore:

1. Download or copy the current data directory if possible.
2. Confirm the backup validates.
3. Confirm you know the admin account recovery path.
4. Plan a service restart window.

For SQL Server and MySQL installs, use database-platform backup and restore tooling. Relaywright's built-in restore path is SQLite-only.
