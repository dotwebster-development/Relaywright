# Relaywright

Windows-first SMTP relay gateway built on ASP.NET Core 10, SQLite, SmtpServer, and MailKit.

## What it does

- Accepts SMTP submissions from trusted device IPs/CIDRs
- Durably spools accepted messages to disk before returning `250 OK`
- Retries outbound delivery to one configured upstream smart host
- Provides a built-in Razor Pages admin UI
- Stores configuration, queue metadata, auth, and operational events in SQLite

## Verified toolchain

This project has been restored, built, and tested with .NET SDK `10.0.300`.

## Default paths

- Data root: `App_Data`
- SQLite database: `App_Data/relay.db`
- Spool files: `App_Data/spool`
- Data Protection keys: `App_Data/keys`

## Bootstrap admin

The app seeds an admin user on first startup from `BootstrapAdmin` in `appsettings.json`.
Change that password immediately after first login.

## Intended startup flow

1. Restore packages with `dotnet restore Relaywright.sln`
2. Run the web app
3. Sign in with the bootstrap admin account
4. Configure the upstream relay and trusted device networks
5. Point printers/devices at the configured SMTP listener port

## Local verification

- Build: `dotnet build Relaywright.sln`
- Tests: `dotnet test tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj`
- Local UI: `dotnet run --project src/Relaywright.Web/Relaywright.Web.csproj --urls http://127.0.0.1:5010`
