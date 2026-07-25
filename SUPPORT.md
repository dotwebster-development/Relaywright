# Relaywright Support

Relaywright is self-hosted infrastructure software. Start with the operator documentation and collect only the minimum redacted information needed to describe the problem.

## Choose The Right Route

| Need | Route |
| --- | --- |
| Installation, configuration, queue, backup, update, or troubleshooting help | Review the [operator manual](https://github.com/dotwebster-development/Relaywright/wiki) |
| Reproducible product defect | Open a [bug report](https://github.com/dotwebster-development/Relaywright/issues/new?template=bug_report.yml) |
| Product improvement or use-case proposal | Open a [feature request](https://github.com/dotwebster-development/Relaywright/issues/new?template=feature_request.yml) |
| Suspected security vulnerability | Follow [SECURITY.md](SECURITY.md) and report it privately |

General support is provided on a best-effort basis. GitHub issues are for actionable product defects and scoped improvements rather than urgent operational response.

## Before Opening A Bug

1. Confirm the problem still occurs on a supported stable version.
2. Review the [troubleshooting guide](https://github.com/dotwebster-development/Relaywright/wiki/Troubleshooting).
3. Record the Relaywright version, operating system, installation method, database provider, and relevant timestamps.
4. Reproduce the problem with the smallest safe configuration and describe the expected and actual behavior.
5. Check existing issues before creating a duplicate.

## Safe Diagnostic Information

Useful information normally includes:

- Relaywright version and package type;
- Windows or Linux version and architecture;
- SQLite, SQL Server, or MySQL provider;
- affected workflow and exact sequence of operator actions;
- queue state, failure category, or redacted error text;
- whether the problem started after an install, update, restart, or configuration change;
- relevant operational-event timestamps in local time with the time zone stated.

Never post:

- passwords, OAuth client secrets, access or refresh tokens;
- certificate or private-key passwords;
- protected configuration values or Data Protection keys;
- message bodies, message spool files, or private SMTP transcripts;
- private certificates or private keys;
- production connection strings;
- customer data, private hostnames, public IP addresses, or email addresses that have not been redacted.

If a maintainer asks for additional diagnostics, confirm that the requested material is safe before posting it publicly.

## Scope

Relaywright supports controlled internal SMTP submission to one configured upstream smart host. It is not a public MX, open relay, spam filter, mailing-list manager, or general-purpose mail server. Requests that require those roles are outside the intended product scope.
