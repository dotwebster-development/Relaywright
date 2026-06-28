# Relaywright

Windows-first SMTP relay gateway built on ASP.NET Core 10, SQLite, SmtpServer, and MailKit.

## What it does

- Accepts SMTP submissions from trusted device IPs/CIDRs
- Applies global and per-device submission policy before SMTP DATA is accepted
- Durably spools accepted messages to disk before returning `250 OK`
- Retries outbound delivery to one configured upstream smart host
- Provides a built-in Razor Pages admin UI
- Stores configuration, queue metadata, auth, diagnostics, alerts, backup history, and operational events in SQLite
- Provides runtime pause/resume, queue retry/purge, diagnostics, alerts, and backup bundles from the admin UI

## Verified toolchain

This project has been restored, built, and tested with .NET SDK `10.0.300`.

## Default paths

- Data root: `App_Data`
- SQLite database: `App_Data/relay.db`
- Spool files: `App_Data/spool`
- Data Protection keys: `App_Data/keys`
- Backup bundles: `App_Data/backups`

## Bootstrap admin

On first startup, if no admin user exists and no `BootstrapAdmin` password is configured, the app shows a first-run setup page where you create the initial admin user name and password.

For automated environments, `BootstrapAdmin` can still be configured to seed the initial admin user.

## Intended startup flow

1. Restore packages with `dotnet restore Relaywright.sln`
2. Run the web app
3. Create or sign in with the initial admin account
4. Configure the upstream relay, submission policy, and trusted device networks
5. Point printers/devices at the configured SMTP listener port

## Local verification

- Build: `dotnet build Relaywright.sln`
- Tests: `dotnet test tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj`
- Local UI: `dotnet run --project src/Relaywright.Web/Relaywright.Web.csproj --urls http://127.0.0.1:5010`
