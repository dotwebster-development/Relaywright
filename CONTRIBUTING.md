# Contributing To Relaywright

Relaywright is a Windows-first SMTP relay gateway. Mail durability, trusted-network enforcement, secret handling, and queue-state correctness take precedence over broad rewrites.

## Before You Start

- Read [docs/DEVELOPMENT_GUIDELINES.md](docs/DEVELOPMENT_GUIDELINES.md).
- Review [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) before changing a runtime subsystem.
- Use the `development` branch as the base for normal work.
- Discuss large behavior or architecture changes in an issue before implementing them.
- Never include credentials, message bodies, spool content, certificates, Data Protection keys, backups, or production infrastructure details.

## Development

Requirements:

- .NET SDK `10.0.300` or a compatible later feature band.

```powershell
dotnet restore Relaywright.sln
dotnet build Relaywright.sln
dotnet test tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj
```

Keep changes focused, follow the existing Razor Pages and service boundaries, and add deterministic tests for behavior changes. Tests must not depend on live SMTP servers, Microsoft OAuth endpoints, or machine-specific interfaces.

## Pull Requests

1. Create a topic branch from the latest `development`.
2. Implement one focused change with its tests and documentation impact.
3. Run the relevant build, test, site, or release validation.
4. Complete the pull-request checklist and explain operational or security implications.
5. Target the pull request at `development` unless a maintainer requests another branch.

The protected-branch workflow is described in [docs/BRANCH_WORKFLOW.md](docs/BRANCH_WORKFLOW.md).

## Reporting Problems

Use the structured GitHub issue forms and follow [SUPPORT.md](SUPPORT.md). Suspected vulnerabilities must be reported privately through [SECURITY.md](SECURITY.md).
