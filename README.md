# Relaywright

Relaywright is a self-hosted SMTP relay gateway for trusted devices, apps, and internal systems that need a controlled path to an upstream smart host.

It is built with ASP.NET Core, SQLite, SmtpServer, and MailKit. Release builds are self-contained for Windows and Linux, so the target machine does not need a separate .NET runtime.

## Status

Relaywright is preparing for a stable `v1.0.0` release. Until that tag exists, treat published release candidates as test/validation builds rather than a production support promise.

## Why It Exists

Many environments still have printers, scanners, line-of-business apps, or appliances that can send SMTP but should not be trusted as open relays. Relaywright gives those systems a narrow, auditable relay point:

- accept SMTP only from trusted IPs/CIDRs;
- apply sender, recipient, size, recipient-count, and rate policy before message DATA is accepted;
- write accepted messages to disk and queue metadata before returning `250 OK`;
- retry delivery to one configured upstream SMTP smart host;
- provide an admin UI for configuration, queue operations, diagnostics, backups, alerts, and operational history.

## What It Is Not

Relaywright is not a general-purpose mail server, public MX, spam filter, mailing-list manager, or open relay. It is designed for controlled internal submission from known devices to a configured upstream relay.

## Safety Model

Relaywright's most important rule is simple: accepted SMTP DATA must be durable before the client gets success.

The relay uses:

- trusted-network checks for SMTP submissions;
- submission policy before DATA is accepted;
- SQLite for configuration and queue metadata;
- a disk spool for raw message content;
- ASP.NET Core Data Protection for persisted secrets;
- operational events for visible configuration, queue, delivery, diagnostics, and system activity.

Rejected submissions are rejected before message content is spooled.

## Platforms

Relaywright supports:

- Windows service hosting;
- Linux systemd hosting;
- self-contained `win-x64`, `linux-x64`, and `linux-arm64` release artifacts, with a best-effort `linux-arm` package for older 32-bit ARM hosts.

The admin UI is HTTPS-first for production installs. The admin HTTP listener is disabled by default unless explicitly enabled. Firewall handling is scoped by default on Windows and opt-in on Linux.

## Install

Download release artifacts from GitHub Releases.

Windows:

```powershell
Relaywright-<version>-windows-x64-installer.exe
```

Run the installer as Administrator.

Linux:

```bash
curl -fsSL https://github.com/dotwebster-development/Relaywright/releases/download/v<version>/install-relaywright.sh \
  | sudo bash -s -- --repo dotwebster-development/Relaywright --version <version>
```

Replace `<version>` with a published version such as `1.0.0-rc.7`.

The Linux installer auto-selects the matching package for x64, ARM64, or 32-bit ARMv7 hosts. For Raspberry Pi class devices, prefer a 64-bit OS so the installer selects the validated `linux-arm64` package; `linux-arm` is best-effort until dedicated ARMv7 validation is available.

## Runtime Data

Default runtime data locations depend on how Relaywright is started:

- Local development/source run: `src/Relaywright.Web/App_Data`
- Windows installer: `C:\ProgramData\Relaywright`
- Linux installer: `/var/lib/relaywright`

Runtime data includes:

- `relay.db` for SQLite data;
- `spool` for accepted message files;
- `keys` for Data Protection keys;
- `backups` for backup bundles;
- `certs` for generated/admin certificate material.

Do not commit runtime data, Data Protection keys, certificates, or backups to source control.

## Local Development

Requirements:

- .NET SDK `10.0.300` or a compatible later feature band.

Useful commands:

```powershell
dotnet restore Relaywright.sln
dotnet build Relaywright.sln
dotnet test tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj
dotnet run --project src/Relaywright.Web/Relaywright.Web.csproj --urls http://127.0.0.1:5010
```

Development configuration may seed a local bootstrap admin. Production deployments must not use the development default password.

## Release Validation

Relaywright uses release-candidate artifacts for validation. The current release process requires:

- Windows clean install and upgrade validation;
- Linux clean install and upgrade validation;
- deterministic trusted/untrusted SMTP smoke testing;
- Linux soak and ugly-path validation;
- local Debug/Release tests;
- vulnerable package gate.

See:

- [Architecture](docs/ARCHITECTURE.md)
- [Development guidelines](docs/DEVELOPMENT_GUIDELINES.md)
- [Branch workflow](docs/BRANCH_WORKFLOW.md)
- [Release process](docs/RELEASE_PROCESS.md)
- [Release checklist](docs/RELEASE_CHECKLIST.md)
- [Windows release validation](docs/WINDOWS_RELEASE_VALIDATION.md)
- [Linux release validation](docs/LINUX_RELEASE_VALIDATION.md)
