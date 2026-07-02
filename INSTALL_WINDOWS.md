# Install Relaywright On Windows

Relaywright for Windows is distributed as a self-contained x64 installer. The host does not need the .NET runtime.

## Recommended Installer

Download the latest Windows installer from GitHub Releases:

```powershell
https://github.com/relaywright/relaywright/releases/latest
```

Run `Relaywright-<version>-windows-x64-installer.exe` as Administrator.

The installer asks for:

- Install directory, default `C:\Program Files\Relaywright`
- Data directory, default `C:\ProgramData\Relaywright`
- Database provider: SQLite, SQL Server, or MySQL
- Database connection string when SQL Server or MySQL is selected
- Windows service name and display name
- Admin HTTPS port
- Optional admin HTTP port, disabled by default
- SMTP firewall port
- Whether to configure Windows Firewall
- Firewall remote address scope, default `LocalSubnet`
- Whether to generate a self-signed HTTPS certificate
- Optional bootstrap admin account

If the bootstrap password is left blank, Relaywright starts with the first-run setup page.

SQLite is the default and stores `relay.db` under the selected data directory. SQL Server and MySQL are installation-time choices for new installs with a pre-created empty database; the admin UI does not change database providers after install. Existing SQLite installs are preserved on update unless you explicitly reinstall/configure a new instance with server database settings.

For SQL Server/MySQL, back up the database with DBA/platform tooling. Relaywright's built-in backup and restore workflow is only available for SQLite databases.

## Advanced Script Install

The installer wraps `scripts/windows/Install-Relaywright.ps1`. Advanced operators can use the script directly with an expanded release folder:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\scripts\windows\Install-Relaywright.ps1 `
  -PackagePath C:\Downloads\relaywright-win-x64 `
  -InstallRoot "C:\Program Files\Relaywright" `
  -DataDirectory "C:\ProgramData\Relaywright" `
  -DatabaseProvider SqlServer `
  -DatabaseConnectionStringFile C:\Secure\relaywright-db-connection.txt `
  -HttpsPort 5443 `
  -EnableHttp `
  -HttpPort 5080 `
  -SmtpPort 25 `
  -ConfigureFirewall `
  -FirewallRemoteAddress "192.168.1.0/24" `
  -NonInteractive
```

## Update

Run the newer installer. It stops the service, installs the new release side-by-side under `releases`, updates the service path, starts Relaywright, and keeps the latest five releases.

Runtime data is preserved.

## Uninstall

Use Windows Apps & Features, or run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File "C:\Program Files\Relaywright\tools\Install-Relaywright.ps1" `
  -Uninstall `
  -InstallRoot "C:\Program Files\Relaywright" `
  -DataDirectory "C:\ProgramData\Relaywright" `
  -ServiceName Relaywright `
  -NonInteractive
```

Add `-RemoveData` only when you also want to delete the SQLite database, spool, keys, certificates, and backups.

## After Install

Open `https://localhost:5443` or the HTTPS port you selected. If you used the self-signed certificate option, the browser will warn until you trust or replace the certificate.
