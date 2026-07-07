# Install Relaywright

Relaywright release builds are self-contained. The target machine does not need a separate .NET runtime.

## Supported Packages

- Windows x64 installer.
- Windows x64 zip package for manual inspection or advanced recovery.
- Linux x64 tarball.
- Linux ARM64 tarball.
- Linux ARMv7 tarball, currently best-effort until dedicated ARMv7 validation hardware is available.

## Windows Install

Download the latest Windows installer:

```powershell
https://github.com/dotwebster-development/Relaywright/releases/latest
```

Run `Relaywright-<version>-windows-x64-installer.exe` as Administrator.

The installer asks for:

- install directory, default `C:\Program Files\Relaywright`;
- data directory, default `C:\ProgramData\Relaywright`;
- database provider: SQLite, SQL Server, or MySQL;
- SQL Server/MySQL mode: new database or existing database;
- SQL Server/MySQL server name, port, database name, user name, and password;
- whether to use default ports;
- admin HTTPS port;
- optional admin HTTP port, disabled by default;
- SMTP firewall port;
- whether to configure Windows Firewall;
- firewall remote address scope, default `LocalSubnet`;
- optional bootstrap admin account.

The Windows service name is fixed as `Relaywright`; its display name is `Relaywright - SMTP relay gateway`. The installer always creates or reuses a self-signed HTTPS certificate during install.

If you leave the bootstrap password blank, Relaywright starts with the first-run setup page.

Silent Windows installs use installer parameters directly:

```powershell
.\Relaywright-<version>-windows-x64-installer.exe `
  /VERYSILENT `
  /SUPPRESSMSGBOXES `
  /NORESTART `
  /DIR="C:\Program Files\Relaywright" `
  /DATA_DIR="C:\ProgramData\Relaywright" `
  /DATABASE_PROVIDER=Sqlite `
  /HTTPS_PORT=5443 `
  /ENABLE_HTTP=0 `
  /HTTP_PORT=5080 `
  /SMTP_PORT=25 `
  /CONFIGURE_FIREWALL=1 `
  /FIREWALL_REMOTE_ADDRESS=LocalSubnet
```

For SQL Server or MySQL installs, add the structured database parameters instead of a raw connection string:

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
  /FIREWALL_REMOTE_ADDRESS=LocalSubnet
```

Leave `/BOOTSTRAP_PASSWORD` unset to use first-run setup.

## Linux Install

Quick install:

```bash
curl -fsSL https://github.com/dotwebster-development/Relaywright/releases/latest/download/install-relaywright.sh \
  | sudo bash -s -- --version latest
```

Common explicit install:

```bash
sudo bash install-relaywright.sh \
  --version 1.0.2 \
  --runtime auto \
  --install-root /opt/relaywright \
  --data-directory /var/lib/relaywright \
  --database-provider Sqlite \
  --service-name relaywright \
  --https-port 5443 \
  --http-port 0 \
  --smtp-port 25 \
  --configure-firewall \
  --firewall-remote-address local-subnet
```

The Linux installer downloads the selected release, verifies `SHA256SUMS.txt`, installs a systemd service, writes `/etc/relaywright.env`, creates a self-signed HTTPS certificate when needed, and starts Relaywright.

## Database Choice

SQLite is the default and stores `relay.db` under the selected data directory.

SQL Server and MySQL are installation-time choices. The installer collects structured server details and Relaywright initializes an empty database on startup when the supplied account has permission. The admin UI does not switch database providers after install, and existing installs cannot switch providers in place.

In SQL Server or MySQL mode, use database-platform backup tooling. Relaywright's built-in backup and restore workflow is SQLite-only.

## Runtime Data

Default data locations:

- Windows installer: `C:\ProgramData\Relaywright`
- Linux installer: `/var/lib/relaywright`

Runtime data includes the database, spool, Data Protection keys, backups, and certificates. Do not commit or publish these files.

## After Install

Open the admin UI:

- Windows default: `https://localhost:5443`
- Linux default: `https://localhost:5443`

If a self-signed certificate was generated, the browser will warn until you trust or replace the certificate.

## Windows Installer Diagnostics

Windows installer diagnostics are written under:

```text
C:\ProgramData\Relaywright\logs\installer-*.log
```

The log records redacted install settings, service status, firewall actions, health-check result, and rollback result. It does not include passwords, certificate passwords, connection-string secrets, tokens, or protected values.
