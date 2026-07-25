<p align="center">
  <a href="https://relaywright.com/">
    <img src="src/Relaywright.Web/wwwroot/brand/relaywright-logo.svg" alt="Relaywright" width="520">
  </a>
</p>

<p align="center">
  A self-hosted SMTP relay gateway for trusted devices, applications, and internal systems.
</p>

<p align="center">
  <a href="https://github.com/dotwebster-development/Relaywright/actions/workflows/ci.yml"><img src="https://github.com/dotwebster-development/Relaywright/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI status"></a>
  <a href="https://github.com/dotwebster-development/Relaywright/releases/latest"><img src="https://img.shields.io/github/v/release/dotwebster-development/Relaywright?display_name=tag&sort=semver" alt="Latest release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/dotwebster-development/Relaywright" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux-088090" alt="Windows and Linux">
</p>

<p align="center">
  <a href="https://relaywright.com/"><strong>Product overview</strong></a>
  ·
  <a href="https://github.com/dotwebster-development/Relaywright/releases/latest"><strong>Download</strong></a>
  ·
  <a href="https://github.com/dotwebster-development/Relaywright/wiki"><strong>Operator manual</strong></a>
  ·
  <a href="SUPPORT.md"><strong>Support</strong></a>
</p>

<p align="center">
  <a href="https://relaywright.com/">
    <img src="site/assets/dashboard-preview.png" alt="Relaywright dashboard showing the production-readiness checklist" width="1100">
  </a>
</p>

Relaywright gives printers, scanners, line-of-business applications, and appliances a narrow, auditable route to one upstream SMTP smart host. It accepts mail only from trusted IP addresses or CIDRs, applies submission policy, durably stores accepted messages before returning SMTP success, and makes delivery state visible through an operational web console.

Relaywright is not a public MX, spam filter, mailing-list manager, general-purpose mail server, or open relay.

## Highlights

- **Controlled intake** — trusted source rules, sender and recipient restrictions, message-size limits, recipient limits, and per-device rate policy.
- **Durable queueing** — message content and queue metadata are persisted before the SMTP client receives `250 OK`.
- **Reliable delivery** — bounded retry scheduling, failure classification, expiry, bulk queue actions, and delivery history.
- **Operator console** — runtime status, queue review, diagnostics, alerts, backups, configuration history, and security visibility.
- **Upstream authentication** — unauthenticated delivery, basic authentication, or Microsoft OAuth client credentials.
- **Self-hosted deployment** — Windows service and Linux systemd packages with no separate .NET runtime required.
- **Database choice** — SQLite by default, with SQL Server and MySQL available as installer-time options.
- **HTTPS-first administration** — managed certificate options, protected secrets, hardened authentication cookies, and operational security events.

## Quick Start

1. Download the current package from [GitHub Releases](https://github.com/dotwebster-development/Relaywright/releases/latest).
2. Install the Windows service or Linux systemd service and open the admin URL shown by the installer.
3. Create the first administrator, configure the upstream smart host, add trusted devices, and run diagnostics before switching production traffic.

### Windows

Download `Relaywright-<version>-windows-x64-installer.exe` and run it as Administrator.

### Linux

Review the release notes, then download and run the installer for the selected version:

```bash
curl -fsSL https://github.com/dotwebster-development/Relaywright/releases/download/v<version>/install-relaywright.sh \
  -o install-relaywright.sh
sudo bash install-relaywright.sh --repo dotwebster-development/Relaywright --version <version>
```

The Linux installer selects the matching x64, ARM64, or best-effort ARMv7 package. Prefer a 64-bit operating system on Raspberry Pi-class devices so the validated `linux-arm64` package is selected.

Continue with the [installation guide](https://github.com/dotwebster-development/Relaywright/wiki/Install-Relaywright) and [first-run setup](https://github.com/dotwebster-development/Relaywright/wiki/First-Run-Setup).

## Safety Model

Relaywright's core guarantee is simple: accepted SMTP DATA must be durable before the client gets success.

- SMTP submissions pass trusted-network and submission-policy checks before message content is accepted.
- Raw message content is stored in the spool; queue and configuration metadata are stored in SQLite, SQL Server, or MySQL.
- Persisted relay credentials and certificate passwords are protected through ASP.NET Core Data Protection.
- Operational history records configuration, queue, delivery, security, diagnostics, and system activity without storing message bodies or credentials.
- Queue cleanup preserves spool and metadata ordering so accepted messages are not silently orphaned.

Treat the Relaywright host, database, spool, Data Protection keys, certificates, and backups as infrastructure data.

## Supported Platforms

| Platform | Package | Service hosting |
| --- | --- | --- |
| Windows x64 | Installer and zip | Windows Service |
| Linux x64 | Self-contained tarball | systemd |
| Linux ARM64 | Self-contained tarball | systemd |
| Linux ARMv7 | Best-effort self-contained tarball | systemd |

Production installs are HTTPS-first. Admin HTTP is disabled by default, and firewall changes are deliberately scoped or opt-in depending on the platform.

## Runtime Data

Default runtime locations:

| Installation | Data directory |
| --- | --- |
| Local source run | `src/Relaywright.Web/App_Data` |
| Windows installer | `C:\ProgramData\Relaywright` |
| Linux installer | `/var/lib/relaywright` |

Runtime data includes the database, message spool, Data Protection keys, backups, certificates, and listener configuration. Do not commit or publish these files.

SQLite installations can use Relaywright's built-in backup and restore workflow. SQL Server and MySQL databases are backed up with their platform-native tooling.

## Documentation

- [Operator manual](https://github.com/dotwebster-development/Relaywright/wiki)
- [Documentation map](docs/README.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Security policy](SECURITY.md)
- [Support](SUPPORT.md)
- [Installation on Windows](INSTALL_WINDOWS.md)
- [Installation on Linux](INSTALL_LINUX.md)
- [Upgrade guide](UPGRADE.md)
- [Release process](docs/RELEASE_PROCESS.md)
- [Release checklist](docs/RELEASE_CHECKLIST.md)

## Support And Security

Use [SUPPORT.md](SUPPORT.md) to choose the right support route and prepare safe diagnostic information. Public issues must not contain message bodies, credentials, tokens, certificate passwords, protected configuration, private SMTP transcripts, or production-sensitive host details.

Security vulnerabilities must be reported privately using the process in [SECURITY.md](SECURITY.md), not through a public issue.

## Contributing

Development and pull-request expectations are documented in [CONTRIBUTING.md](CONTRIBUTING.md). The repository uses protected `main` and `development` branches and requires changes to pass the relevant build and test checks.

Local requirements:

- .NET SDK `10.0.300` or a compatible later feature band.

```powershell
dotnet restore Relaywright.sln
dotnet build Relaywright.sln
dotnet test tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj
dotnet run --project src/Relaywright.Web/Relaywright.Web.csproj --urls http://127.0.0.1:5010
```

Development configuration may seed a local bootstrap administrator. Production deployments must never use the development default password.

## Release Validation

Stable releases are promoted only after the required Windows and Linux installation, upgrade, SMTP smoke, package vulnerability, and relay safety checks. Release-candidate artifacts remain validation builds until the matching stable tag is published.

See the [release process](docs/RELEASE_PROCESS.md), [Windows validation guide](docs/WINDOWS_RELEASE_VALIDATION.md), [Linux validation guide](docs/LINUX_RELEASE_VALIDATION.md), and recorded [release evidence](docs/release-records/).

## License

Relaywright is available under the [MIT License](LICENSE).
