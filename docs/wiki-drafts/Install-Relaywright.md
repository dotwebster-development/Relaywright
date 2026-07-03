# Install Relaywright

Relaywright release builds are self-contained. The target machine does not need a separate .NET runtime.

## Supported Packages

- Windows x64 installer.
- Windows x64 zip package.
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
- database connection string when SQL Server or MySQL is selected;
- Windows service name and display name;
- admin HTTPS port;
- optional admin HTTP port, disabled by default;
- SMTP firewall port;
- whether to configure Windows Firewall;
- firewall remote address scope, default `LocalSubnet`;
- whether to generate a self-signed HTTPS certificate;
- optional bootstrap admin account.

If you leave the bootstrap password blank, Relaywright starts with the first-run setup page.

## Linux Install

Quick install:

```bash
curl -fsSL https://github.com/dotwebster-development/Relaywright/releases/latest/download/install-relaywright.sh \
  | sudo bash -s -- --version latest
```

Common explicit install:

```bash
sudo bash install-relaywright.sh \
  --version 1.0.1 \
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

SQL Server and MySQL are installation-time choices for new installs with pre-created empty databases. The admin UI does not switch database providers after install.

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
