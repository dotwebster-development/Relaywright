# Install Relaywright On Windows

Relaywright for Windows is distributed as a self-contained x64 installer. The host does not need the .NET runtime.

## Recommended Installer

Download the latest Windows installer from GitHub Releases:

```powershell
https://github.com/dotwebster-development/Relaywright/releases/latest
```

Run `Relaywright-<version>-windows-x64-installer.exe` as Administrator.

The installer asks for:

- Install directory, default `C:\Program Files\Relaywright`
- Data directory, default `C:\ProgramData\Relaywright`
- Database provider: SQLite, SQL Server, or MySQL
- SQL Server/MySQL mode: new database or existing database
- SQL Server/MySQL server name, port, database name, user name, and password
- Whether to use default ports
- Admin HTTPS port, default `5443`
- Whether to enable admin HTTP, disabled by default
- Admin HTTP port, default `5080`
- SMTP firewall port, default `25`
- Whether to configure Windows Firewall
- Firewall remote address scope, default `LocalSubnet`
- Optional bootstrap admin account

The Windows service name is fixed as `Relaywright`; its display name is `Relaywright - SMTP relay gateway`.

The installer always creates or reuses a self-signed HTTPS certificate during install. Replace or trust the certificate after install from the admin UI when needed.

If the bootstrap password is left blank, Relaywright starts with the first-run setup page.

SQLite is the default and stores `relay.db` under the selected data directory. SQL Server and MySQL are installation-time choices; the admin UI does not change database providers after install. Existing installs cannot switch database providers in place.

For SQL Server/MySQL, back up the database with DBA/platform tooling. Relaywright's built-in backup and restore workflow is only available for SQLite databases.

## Silent Installer

Operators and CI should use the installer directly instead of a separate install script:

```powershell
.\Relaywright-<version>-windows-x64-installer.exe `
  /VERYSILENT `
  /SUPPRESSMSGBOXES `
  /NORESTART `
  /DIR="C:\Program Files\Relaywright" `
  /DATA_DIR="C:\ProgramData\Relaywright" `
  /DATABASE_PROVIDER=SqlServer `
  /DATABASE_MODE=New `
  /DATABASE_SERVER=sql01.example.local `
  /DATABASE_PORT=1433 `
  /DATABASE_NAME=Relaywright `
  /DATABASE_USER=relaywright `
  /DATABASE_PASSWORD="use-a-secret-value" `
  /HTTPS_PORT=5443 `
  /ENABLE_HTTP=0 `
  /HTTP_PORT=5080 `
  /SMTP_PORT=25 `
  /CONFIGURE_FIREWALL=1 `
  /FIREWALL_REMOTE_ADDRESS=LocalSubnet `
  /BOOTSTRAP_USERNAME=admin `
  /BOOTSTRAP_EMAIL=admin@localhost
```

Use `/DATABASE_PROVIDER=Sqlite` for the default local database. Leave `/BOOTSTRAP_PASSWORD` unset to use first-run setup.

## Update

Run the newer installer as Administrator. It detects the existing service, blocks in-place database-provider changes, installs the new release side-by-side under `releases`, updates the service path, starts Relaywright, waits for `/health`, and keeps the latest five releases.

If the new release fails health validation, the installer restores the previous service path and service environment, then restarts the prior release.

Runtime data is preserved.

## Uninstall

Use Windows Apps & Features. The uninstaller removes the Windows service, Relaywright firewall rules, installed binaries, packages, and release directories.

The data directory is preserved by default. Delete the data directory manually only when you also want to remove the SQLite database, spool, keys, certificates, logs, and backups.

## Diagnostics

Installer diagnostics are written under:

```text
C:\ProgramData\Relaywright\logs\installer-*.log
```

The log records redacted install settings, service status, firewall actions, health-check result, and rollback result. It does not include passwords, certificate passwords, connection-string secrets, tokens, or protected values.

## After Install

Open `https://localhost:5443` or the HTTPS port you selected. If the self-signed certificate is still in use, the browser will warn until you trust or replace it.
